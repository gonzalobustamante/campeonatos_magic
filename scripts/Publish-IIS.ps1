param([string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\iis'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\CampeonatoMagic.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained false -o $OutputPath
if ($LASTEXITCODE -ne 0) { throw 'La publicación falló.' }
Write-Host "Publicación lista en $([System.IO.Path]::GetFullPath($OutputPath))"
Write-Host 'Consulta README.md para configurar el sitio, los datos y el administrador en IIS.'
