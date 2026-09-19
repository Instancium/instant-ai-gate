# Define directories to hide entirely
$hiddenDirectories = @('bin', 'obj', 'wwwroot')

# Define directories to show only by name (hide their contents)
$shallowDirectories = @('node_modules', 'logs', 'llama.cpp')

# 1. Hide the entire folder for completely excluded directories
Get-ChildItem -Recurse -Directory -Include $hiddenDirectories | 
    ForEach-Object { $_.Attributes = 'Hidden' }

# 2. Hide only the contents of directories where you want to see just the name
Get-ChildItem -Recurse -Directory -Include $shallowDirectories | 
    Get-ChildItem -Recurse | 
    ForEach-Object { $_.Attributes = 'Hidden' }

# 3. Print the directory tree
tree /F

# 4. Restore attributes for completely hidden folders
Get-ChildItem -Recurse -Directory -Include $hiddenDirectories -Force | 
    ForEach-Object { $_.Attributes = 'Normal' }

# 5. Restore attributes for the contents of partially hidden folders
Get-ChildItem -Recurse -Directory -Include $shallowDirectories -Force | 
    Get-ChildItem -Recurse -Force | 
    ForEach-Object { $_.Attributes = 'Normal' }
