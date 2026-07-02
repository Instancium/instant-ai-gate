[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('nuget','github','local')][string]$Source = 'local',
    [string]$ApiKey,
    [string]$Version,
    [ValidateSet('Release','Debug')][string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$SkipCompress,
    [string]$OutputDirectory = "nuget-packages"
)
$ErrorActionPreference = "Stop"
$backends = [ordered]@{
    "win-x64/cpu"    = @()
    "win-x64/cuda"   = @("*.exe","llama-batched-bench-impl.dll","llama-bench-impl.dll","llama-cli-impl.dll","llama-completion-impl.dll","llama-fit-params-impl.dll","llama-perplexity-impl.dll","llama-quantize-impl.dll")
    "linux-x64/cpu"  = @()
    "linux-x64/cuda" = @("llama-batched-bench-impl.so","llama-bench-impl.so","llama-cli-impl.so","llama-completion-impl.so","llama-fit-params-impl.so","llama-perplexity-impl.so","llama-quantize-impl.so")
}
$runtimeProjects = @(
    "src/Runtimes/InstantAIGate.Runtimes.win-x64.Cpu/InstantAIGate.Runtimes.win-x64.Cpu.csproj",
    "src/Runtimes/InstantAIGate.Runtimes.linux-x64.Cpu/InstantAIGate.Runtimes.linux-x64.Cpu.csproj",
    "src/Runtimes/InstantAIGate.Runtimes.win-x64.Cuda/InstantAIGate.Runtimes.win-x64.Cuda.csproj",
    "src/Runtimes/InstantAIGate.Runtimes.linux-x64.Cuda/InstantAIGate.Runtimes.linux-x64.Cuda.csproj"
)
$nugetSources = @{ 'nuget'='https://api.nuget.org/v3/index.json'; 'github'='https://nuget.pkg.github.com/Instancium/index.json'; 'local'=$OutputDirectory }
function Write-Status { param([string]$Message,[ValidateSet('Info','Success','Warning','Error')][string]$Level='Info'); $colors=@{'Info'='Cyan';'Success'='Green';'Warning'='Yellow';'Error'='Red'}; $pfx=switch($Level){'Info'{'[INFO]'}'Success'{'[OK]'}'Warning'{'[WARN]'}'Error'{'[FAIL]'}}; Write-Host "$pfx $Message" -ForegroundColor $colors[$Level] }
function Find-7Zip { $f=Get-Command "7z.exe" -ErrorAction SilentlyContinue; if($f){return $f.Source}; foreach($c in @("C:\Program Files\7-Zip\7z.exe","C:\Program Files (x86)\7-Zip\7z.exe")){if(Test-Path $c){return $c}}; return $null }
$repoRoot = Split-Path $PSScriptRoot -Parent
$runtimesPath = Join-Path $repoRoot ".runtimes"
if (-not (Test-Path $runtimesPath)) { Write-Status ".runtimes folder not found. Maintainers only." -Level Error; exit 1 }
Write-Status "Validated .runtimes folder exists" -Level Success
foreach ($p in $runtimeProjects) { if (-not (Test-Path (Join-Path $repoRoot $p))) { Write-Status "Project not found: $p" -Level Error; exit 1 } }
Write-Status "Validated all runtime projects exist" -Level Success
if (-not $SkipCompress -and -not $SkipBuild) {
    $sz = Find-7Zip
    if (-not $sz) { Write-Status "7-Zip not found. Install from https://7-zip.org" -Level Error; exit 1 }
    Write-Status "Using 7-Zip: $sz" -Level Info
    foreach ($key in $backends.Keys) {
        $parts = $key -split '/'; $rid=$parts[0]; $bk=$parts[1]
        $bkPath = Join-Path $runtimesPath "$rid\$bk"
        if (-not (Test-Path $bkPath)) { Write-Status "Skipping missing: $bkPath" -Level Warning; continue }
        $archPath = Join-Path $bkPath "$bk.7z"
        $excArgs = $backends[$key] | ForEach-Object { "-xr!$_" }
        $origMb = [math]::Round((Get-ChildItem $bkPath -File | Measure-Object Length -Sum).Sum/1MB,1)
        Write-Status "Compressing $key ($origMb MB)..." -Level Info
        if (Test-Path $archPath) { Remove-Item $archPath -Force }
        if ($PSCmdlet.ShouldProcess($key,"7z compress")) {
            $args7z = @("a","-t7z","-mx=9","-mmt=on",$archPath) + $excArgs + @("$bkPath\*")
            & $sz @args7z | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Status "7z failed for $key" -Level Error; exit $LASTEXITCODE }
            $compMb = [math]::Round((Get-Item $archPath).Length/1MB,1)
            Write-Status "Compressed ${key}: $origMb MB -> $compMb MB ($([math]::Round($compMb/$origMb*100,0))%)" -Level Success
        }
    }
    Write-Status "All backends compressed" -Level Success
}
$outputPath = Join-Path $repoRoot $OutputDirectory
if (-not (Test-Path $outputPath)) { New-Item -ItemType Directory -Path $outputPath -Force | Out-Null }
if (-not $SkipBuild) {
    Write-Status "Building runtime packages..." -Level Info
    $packArgs = @('--configuration',$Configuration,'--output',$outputPath)
    if ($Version) { $packArgs += "/p:PackageVersion=$Version" }
    foreach ($proj in $runtimeProjects) {
        $pPath = Join-Path $repoRoot $proj; $pName = [IO.Path]::GetFileNameWithoutExtension($proj)
        Write-Status "Building $pName..." -Level Info
        if ($PSCmdlet.ShouldProcess($pName,"dotnet pack")) {
            dotnet pack $pPath @packArgs
            if ($LASTEXITCODE -ne 0) { Write-Status "Failed: $pName" -Level Error; exit $LASTEXITCODE }
            Write-Status "Built $pName" -Level Success
        }
    }
    Write-Status "All packages built" -Level Success
}
$packages = Get-ChildItem -Path $outputPath -Filter "InstantAIGate.Runtimes.*.nupkg" -File | Where-Object { $_.Name -notlike "*.symbols.nupkg" }
if ($packages.Count -eq 0) { Write-Status "No packages found in $outputPath" -Level Error; exit 1 }
Write-Status "Found $($packages.Count) package(s):" -Level Info
foreach ($pkg in $packages) { $mb=[math]::Round($pkg.Length/1MB,2); $chk=if($mb -lt 250){"OK"}else{"EXCEEDS 250MB LIMIT"}; Write-Status "  $($pkg.Name) ($mb MB) [$chk]" -Level Info }
if ($Source -ne 'local') {
    if (-not $ApiKey) { Write-Status "ApiKey required for $Source" -Level Error; exit 1 }
    $tgt = $nugetSources[$Source]
    Write-Status "Publishing to $Source ($tgt)..." -Level Info
    foreach ($pkg in $packages) {
        if ($PSCmdlet.ShouldProcess($pkg.Name,"nuget push")) {
            dotnet nuget push $pkg.FullName --source $tgt --api-key $ApiKey --skip-duplicate
            if ($LASTEXITCODE -ne 0) { Write-Status "Failed to publish $($pkg.Name)" -Level Warning } else { Write-Status "Published $($pkg.Name)" -Level Success }
        }
    }
    Write-Status "Publishing completed" -Level Success
} else {
    Write-Status "Packages saved to: $outputPath" -Level Success
    Write-Status "To publish: .\scripts\Publish-RuntimePackages.ps1 -Source nuget -ApiKey 'key' -SkipBuild" -Level Info
}
Write-Status "Done!" -Level Success