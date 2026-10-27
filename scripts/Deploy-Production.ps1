[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [Parameter(Mandatory=$true)][string]$SiteName,
    [Parameter(Mandatory=$true)][string]$PublicUrl,
    [Parameter(Mandatory=$true)][string]$DataDirectory,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$CommitSha
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
Import-Module WebAdministration -ErrorAction Stop

$package = Resolve-SafeDirectory $PackagePath
$source = Resolve-SafeDirectory (Join-Path $package 'app')
$data = Resolve-SafeDirectory $DataDirectory
$backupRoot = Resolve-SafeDirectory ($data + '-backups')
$uri = [Uri]$PublicUrl
if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) { throw 'Production requires an absolute HTTPS URL without credentials, query or fragment.' }
$release = Get-Content -LiteralPath (Join-Path $package 'release.json') -Raw | ConvertFrom-Json
if ($release.commit -ne $CommitSha) { throw 'The artifact does not match the requested commit.' }
if (-not (Test-Path -LiteralPath (Join-Path $source 'CampeonatoMagic.dll'))) { throw 'Missing application package.' }

$sites = @(Get-Website)
$target = @($sites | Where-Object Name -EQ $SiteName)
if ($target.Count -ne 1) { throw "IIS site '$SiteName' must exist before deployment. Run Initialize-IisSite.ps1 on the server." }
$site = $target[0]
$destination = Resolve-SafeDirectory ([Environment]::ExpandEnvironmentVariables($site.PhysicalPath))
$pool = [string]$site.ApplicationPool
if (-not $pool -or @($sites | Where-Object { $_.Name -ne $SiteName -and $_.ApplicationPool -eq $pool }).Count -gt 0) { throw 'The site must use its own application pool.' }
if (@(Get-WebApplication | Where-Object ApplicationPool -EQ $pool).Count -gt 0) { throw 'The application pool is shared with another IIS application.' }
$poolItem = Get-Item -LiteralPath "IIS:\AppPools\$pool"
if ($poolItem.enable32BitAppOnWin64 -or $poolItem.processModel.maxProcesses -ne 1) { throw 'The pool must run x64 with one worker process.' }
if (-not (Get-WebGlobalModule | Where-Object Name -EQ 'AspNetCoreModuleV2')) { throw 'Install the .NET 10 Hosting Bundle before deploying.' }
$runtime = & "$env:ProgramFiles\dotnet\dotnet.exe" --list-runtimes
if ($LASTEXITCODE -ne 0 -or -not ($runtime -match '^Microsoft.AspNetCore.App 10\.')) { throw 'The .NET 10 ASP.NET Core runtime is required.' }
foreach ($other in $sites | Where-Object Name -NE $SiteName) {
    $otherPath = [Environment]::ExpandEnvironmentVariables($other.PhysicalPath)
    if (Test-DirectoryOverlap $destination $otherPath) { throw "The physical directory overlaps IIS site '$($other.Name)'." }
}
foreach ($path in @($source, $data, $backupRoot, $package)) {
    if (Test-DirectoryOverlap $destination $path) { throw 'Package, runtime data and backups must stay outside the site directory.' }
}
if (Test-DirectoryOverlap $data $backupRoot) { throw 'Backups must be outside the data directory.' }
foreach ($protected in @($env:SystemRoot, $env:ProgramFiles, ${env:ProgramFiles(x86)})) {
    if ($protected -and (Test-DirectoryOverlap $destination $protected)) { throw 'The IIS destination overlaps a system directory.' }
}
foreach ($path in @($source,$destination,$data,$backupRoot)) { Assert-NoReparsePoints $path }
if (Test-Path -LiteralPath (Join-Path $destination 'app_offline.htm')) { throw 'The site is already in maintenance mode; deployment stopped.' }
$oldConfig = Join-Path $destination 'web.config'
if (Test-Path -LiteralPath $oldConfig) {
    [xml]$previous = Get-Content -LiteralPath $oldConfig -Raw
    $oldDataNode = $previous.SelectSingleNode('//environmentVariable[@name="DataDirectory"]')
    if ($oldDataNode -and (Resolve-SafeDirectory $oldDataNode.GetAttribute('value')) -ne $data) {
        throw 'The configured data directory differs from the existing deployment. Migrate it explicitly before changing the variable.'
    }
}
if (Test-Path -LiteralPath (Join-Path $destination 'App_Data\league.db')) { throw 'Move the legacy App_Data database to the external data directory before deployment.' }

New-Item -ItemType Directory -Path $data,$backupRoot -Force | Out-Null
Set-PrivateDirectoryAcl -Path $data -ApplicationPool $pool
Set-PrivateDirectoryAcl -Path $backupRoot
$backup = Join-Path $backupRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $CommitSha.Substring(0,8) + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$backup = Resolve-SafeDirectory $backup
New-Item -ItemType Directory -Path $backup | Out-Null
Set-ProductionWebConfig -Path (Join-Path $source 'web.config') -PreviousPath $oldConfig -DataDirectory $data -CommitSha $CommitSha

function Stop-TargetPool {
    if ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') { Stop-WebAppPool -Name $pool }
    $deadline = (Get-Date).AddSeconds(45)
    do {
        [xml]$workers = (& "$env:windir\system32\inetsrv\appcmd.exe" list wp "/apppool.name:$pool" /xml | Out-String)
        if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the IIS worker process.' }
        if ((Get-WebAppPoolState -Name $pool).Value -eq 'Stopped' -and $workers.SelectNodes('//WP').Count -eq 0) { return }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw 'The application pool did not release its worker process in 45 seconds.'
}

$restartPool = $false
try {
    $restartPool = $true
    Stop-TargetPool
    foreach ($folder in @('site','data')) { New-Item -ItemType Directory -Path (Join-Path $backup $folder) | Out-Null }
    Get-ChildItem -LiteralPath $destination -Force | Copy-Item -Destination (Join-Path $backup 'site') -Recurse -Force
    Get-ChildItem -LiteralPath $data -Force | Copy-Item -Destination (Join-Path $backup 'data') -Recurse -Force
    @{ commit=$CommitSha; site=$SiteName; sitePath=$destination; dataPath=$data; savedAt=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'backup.json') -Encoding UTF8
    Write-Host "Backup completed: $backup"
    # Both absolute paths were validated above; data and secrets are excluded from /MIR.
    & robocopy $source $destination /MIR /COPY:DAT /DCOPY:DAT /R:2 /W:2 /XD App_Data logs /XF appsettings.Production.json app_offline.htm /NFL /NDL /NJH /NJS
    $copyCode = $LASTEXITCODE
    if ($copyCode -gt 7) { throw "Robocopy failed with exit code $copyCode. Restore snapshot: $backup" }
    $global:LASTEXITCODE = 0
}
finally {
    if ($restartPool) { Start-WebAppPool -Name $pool }
}

if ((Get-Website -Name $SiteName).State -ne 'Started') { Start-Website -Name $SiteName }
$healthUrl = $PublicUrl.TrimEnd('/') + '/health/ready'
$ready = $false
for ($attempt=1; $attempt -le 12; $attempt++) {
    try {
        $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 10
        if ($health.status -eq 'ready' -and $health.commit -eq $CommitSha) { $ready=$true; break }
    }
    catch { Write-Host "Readiness attempt $attempt did not succeed." }
    if ($attempt -lt 12) { Start-Sleep -Seconds 5 }
}
if (-not $ready) { throw "The new commit is not ready at $healthUrl. Backup available at $backup. No database rollback was performed." }
$home = Invoke-WebRequest -Uri $PublicUrl -UseBasicParsing -TimeoutSec 15
if ($home.StatusCode -ne 200 -or $home.Content -notmatch 'Commander Chilesystems') { throw "The home page did not pass validation. Backup: $backup" }
Write-Host "Deployment verified: $PublicUrl ($CommitSha). Backup: $backup"
$global:LASTEXITCODE = 0
