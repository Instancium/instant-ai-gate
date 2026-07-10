Get-ChildItem -Recurse -Directory -Include bin, obj, wwwroot | ForEach-Object { $_.Attributes = 'Hidden' }
tree /F
Get-ChildItem -Recurse -Directory -Include bin, obj, wwwroot -Force | ForEach-Object { $_.Attributes = 'Normal' }
