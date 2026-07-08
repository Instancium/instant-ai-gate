[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('nuget', 'github', 'local')][string]$Source = 'local',
    [string]$ApiKey,
    [string]$Version,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$SkipCompress,
    [string]$OutputDirectory = "nuget-packages"
)

$ErrorActionPreference = "Stop"

$platforms = [ordered]@{
    "Windows" = @{
        SourcePath = "Windows\x64"
        ArchiveName = "windows-x64.7z"
        ProjectDir = "src\Runtimes\InstantAIGate.Runtimes.Windows"
        Exclusions = @("*.exe", "*llama-batched-bench*", "*llama-bench*", "*llama-cli*", "*llama-completion*", "*llama-fit-params*", "*llama-perplexity*", "*llama-quantize*")
    }
    "Linux" = @{
        SourcePath = "Linux\x64"
        ArchiveName = "linux-x64.7z"
        ProjectDir = "src\Runtimes\InstantAIGate.Runtimes.Linux"
        Exclusions = @("*llama-batched-bench*", "*llama-bench*", "*llama-cli*", "*llama-completion*", "*llama-fit-params*", "*llama-perplexity*", "*llama-quantize*")
    }
}

$nugetSources = @{
    'nuget' = 'https://api.nuget.org/v3/index.json'
    'github' = 'https://nuget.pkg.github.com/Instancium/index.json'
    'local' = $OutputDirectory
}

function Write-Status {
    param(
        [string]$Message,
        [ValidateSet('Info', 'Success', 'Warning', 'Error')][string]$Level = 'Info'
    )
    $colors = @{ 'Info' = 'Cyan'; 'Success' = 'Green'; 'Warning' = 'Yellow'; 'Error' = 'Red' }
    $prefixes = @{ 'Info' = '[INFO]'; 'Success' = '[OK]'; 'Warning' = '[WARN]'; 'Error' = '[FAIL]' }
    Write-Host "$($prefixes[$Level]) $Message" -ForegroundColor $colors[$Level]
}

function Find-7Zip {
    $command = Get-Command "7z.exe" -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($path in @("C:\Program Files\7-Zip\7z.exe", "C:\Program Files (x86)\7-Zip\7z.exe")) {
        if (Test-Path $path) { return $path }
    }
    return $null
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$runtimesSourcePath = Join-Path $repoRoot ".runtimes"

if (-not (Test-Path $runtimesSourcePath)) {
    Write-Status "Missing .runtimes source directory. Operation aborted." -Level Error
    exit 1
}

if (-not $SkipCompress -and -not $SkipBuild) {
    $sevenZipPath = Find-7Zip
    if (-not $sevenZipPath) {
        Write-Status "7-Zip executable not found. Install from https://7-zip.org" -Level Error
        exit 1
    }

    foreach ($platformKey in $platforms.Keys) {
        $platform = $platforms[$platformKey]
        $sourceDir = Join-Path $runtimesSourcePath $platform.SourcePath
        $projectDir = Join-Path $repoRoot $platform.ProjectDir
        $archivePath = Join-Path $projectDir $platform.ArchiveName

        if (-not (Test-Path $sourceDir)) {
            Write-Status "Skipping missing source directory: $sourceDir" -Level Warning
            continue
        }

        if (-not (Test-Path $projectDir)) {
            New-Item -ItemType Directory -Path $projectDir -Force | Out-Null
        }

        $exclusionArgs = $platform.Exclusions | ForEach-Object { "-xr!$_" }
        $originalSizeMb = [math]::Round((Get-ChildItem $sourceDir -File | Measure-Object Length -Sum).Sum / 1MB, 1)

        Write-Status "Compressing $platformKey binaries ($originalSizeMb MB)..." -Level Info

        if (Test-Path $archivePath) { Remove-Item $archivePath -Force }

        if ($PSCmdlet.ShouldProcess($platformKey, "7z compress")) {
            $arguments = @("a", "-t7z", "-mx=9", "-mmt=on", $archivePath) + $exclusionArgs + @("$sourceDir\*")
            & $sevenZipPath @arguments | Out-Null

            if ($LASTEXITCODE -ne 0) {
                Write-Status "7-Zip compression failed for $platformKey" -Level Error
                exit $LASTEXITCODE
            }

            $compressedSizeMb = [math]::Round((Get-Item $archivePath).Length / 1MB, 1)
            $ratio = [math]::Round($compressedSizeMb / $originalSizeMb * 100, 0)
            Write-Status "Generated $($platform.ArchiveName): $originalSizeMb MB -> $compressedSizeMb MB ($ratio%)" -Level Success
        }
    }
}

$outputDirectoryPath = Join-Path $repoRoot $OutputDirectory
if (-not (Test-Path $outputDirectoryPath)) {
    New-Item -ItemType Directory -Path $outputDirectoryPath -Force | Out-Null
}

if (-not $SkipBuild) {
    Write-Status "Building runtime packages..." -Level Info
    
    $packArguments = @('--configuration', $Configuration, '--output', $outputDirectoryPath)
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $packArguments += "/p:PackageVersion=$Version"
    }

    foreach ($platformKey in $platforms.Keys) {
        $projectPath = Join-Path $repoRoot $platforms[$platformKey].ProjectDir
        
        if (-not (Test-Path $projectPath)) {
            Write-Status "Skipping missing project directory: $projectPath" -Level Warning
            continue
        }

        if ($PSCmdlet.ShouldProcess($platformKey, "dotnet pack")) {
            dotnet pack $projectPath @packArguments
            
            if ($LASTEXITCODE -ne 0) {
                Write-Status "Build failed for $platformKey" -Level Error
                exit $LASTEXITCODE
            }
            Write-Status "Successfully built package for $platformKey" -Level Success
        }
    }
}

$packages = Get-ChildItem -Path $outputDirectoryPath -Filter "InstantAIGate.Runtimes.*.nupkg" -File | Where-Object { $_.Name -notlike "*.symbols.nupkg" }

if ($packages.Count -eq 0) {
    Write-Status "No NuGet packages found in output directory." -Level Error
    exit 1
}

foreach ($package in $packages) {
    $sizeMb = [math]::Round($package.Length / 1MB, 2)
    $validation = if ($sizeMb -lt 250) { "OK" } else { "EXCEEDS 250MB NIGER.ORG LIMIT" }
    Write-Status "Validated package: $($package.Name) ($sizeMb MB) [$validation]" -Level Info
}

if ($Source -ne 'local') {
    if ([string]::IsNullOrWhiteSpace($ApiKey)) {
        Write-Status "API key is required for remote publishing ($Source)." -Level Error
        exit 1
    }

    $targetSource = $nugetSources[$Source]
    Write-Status "Publishing packages to $Source..." -Level Info

    foreach ($package in $packages) {
        if ($PSCmdlet.ShouldProcess($package.Name, "nuget push")) {
            dotnet nuget push $package.FullName --source $targetSource --api-key $ApiKey --skip-duplicate
            
            if ($LASTEXITCODE -ne 0) {
                Write-Status "Failed to publish $($package.Name)" -Level Warning
            } else {
                Write-Status "Successfully published $($package.Name)" -Level Success
            }
        }
    }
}