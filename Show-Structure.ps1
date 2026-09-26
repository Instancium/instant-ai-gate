function Invoke-CustomTree {
    param(
        [string]$TargetDirectory = '.',
        [string[]]$ExcludeDirectories = @('bin', 'obj', 'wwwroot'),
        [string[]]$ShallowDirectories = @('node_modules', 'logs', 'llama.cpp'),
        [string]$LinePrefix = ''
    )

    # Fetch all items and sort directories to appear first
    $directoryItems = Get-ChildItem -Path $TargetDirectory -ErrorAction SilentlyContinue |
        Sort-Object -Property @{Expression={$_.PSIsContainer};Descending=$true}, Name

    $totalItems = $directoryItems.Count

    for ($index = 0; $index -lt $totalItems; $index++) {
        $currentItem = $directoryItems[$index]
        $isFinalItem = ($index -eq ($totalItems - 1))

        # ASCII characters for tree branches
        $branchSymbol = if ($isFinalItem) { "\--- " } else { "+--- " }
        $childPrefix = if ($isFinalItem) { "     " } else { "|    " }

        if ($currentItem.PSIsContainer) {
            # Skip the directory completely if it is in the exclusion list
            if ($ExcludeDirectories -contains $currentItem.Name) {
                continue
            }

            # Return the directory name to the pipeline
            "${LinePrefix}${branchSymbol}$($currentItem.Name)"

            # Recurse inside only if it is NOT in the shallow list
            if ($ShallowDirectories -notcontains $currentItem.Name) {
                Invoke-CustomTree -TargetDirectory $currentItem.FullName `
                                  -ExcludeDirectories $ExcludeDirectories `
                                  -ShallowDirectories $ShallowDirectories `
                                  -LinePrefix "${LinePrefix}${childPrefix}"
            }
        } else {
            # Return the file name to the pipeline
            "${LinePrefix}${branchSymbol}$($currentItem.Name)"
        }
    }
}

# Execute the custom tree function and save it directly to a UTF-8 file
Invoke-CustomTree -TargetDirectory '.' | Out-File -FilePath 'project_structure.txt' -Encoding utf8