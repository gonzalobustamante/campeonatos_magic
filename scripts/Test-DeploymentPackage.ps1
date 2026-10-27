param([Parameter(Mandatory=$true)][string]$Path)
$ErrorActionPreference = 'Stop'
$required = @('CampeonatoMagic.dll', 'CampeonatoMagic.exe', 'CampeonatoMagic.runtimeconfig.json', 'web.config', 'appsettings.json', 'wwwroot\css\site.css', 'wwwroot\js\site.js')
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $Path $file) -PathType Leaf)) { throw "Incomplete package: $file" }
}
$sensitiveFiles = @(Get-ChildItem -LiteralPath $Path -Recurse -Force -File | Where-Object {
    $_.Name -eq 'admin.json' -or $_.Name -like '*.db' -or $_.Name -like '*.db-wal' -or $_.Name -like '*.db-shm' -or $_.FullName -match '[\\/]App_Data[\\/]|[\\/]preview-data[\\/]|[\\/]keys[\\/]'
})
if ($sensitiveFiles.Count -ne 0) { throw 'The package contains runtime data or credentials. Do not deploy.' }
Write-Host 'IIS package validated; no runtime data included.'
