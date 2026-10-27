[CmdletBinding()]
param(
    [string]$SiteName = 'MagicCampeonatos',
    [string]$HostName = 'magic.chilesystems.com',
    [string]$PhysicalPath = 'C:\inetpub\MagicCampeonatos',
    [string]$DataDirectory = 'C:\ProgramData\Chilesystems\MagicCampeonatos',
    [string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as administrator on the Windows Server.' }
Import-Module WebAdministration
$physical = Resolve-SafeDirectory $PhysicalPath
$data = Resolve-SafeDirectory $DataDirectory
$backups = Resolve-SafeDirectory ($data + '-backups')
if (Test-DirectoryOverlap $physical $data) { throw 'Runtime data must be outside the published website.' }
foreach ($existing in @(Get-Website | Where-Object Name -NE $SiteName)) {
    if (Test-DirectoryOverlap $physical ([Environment]::ExpandEnvironmentVariables($existing.PhysicalPath))) { throw "Directory overlaps site '$($existing.Name)'." }
    if ($existing.ApplicationPool -eq $SiteName) { throw 'Application pool is already used by a different site.' }
    if (@($existing.Bindings.Collection | Where-Object bindingInformation -EQ "*:443:$HostName").Count -gt 0) { throw 'The HTTPS hostname is already bound to another website.' }
}
$certificates = @(Get-ChildItem Cert:\LocalMachine\My | Where-Object {
    $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and $_.NotBefore -le (Get-Date) -and
    @($_.DnsNameList | Where-Object { $HostName -like $_.Unicode }).Count -gt 0
} | Sort-Object NotAfter -Descending)
if ($CertificateThumbprint) { $certificates = @($certificates | Where-Object Thumbprint -EQ ($CertificateThumbprint -replace ' ','')) }
if ($certificates.Count -eq 0) { throw "Install a valid certificate for $HostName in LocalMachine\My before running this script." }
$certificate = $certificates[0]
$site = Get-Website | Where-Object Name -EQ $SiteName
if ($site -and ((Resolve-SafeDirectory $site.PhysicalPath) -ne $physical -or $site.ApplicationPool -ne $SiteName)) { throw 'An existing site has a different directory or application pool. No changes were made.' }
if (@(Get-WebApplication | Where-Object ApplicationPool -EQ $SiteName).Count -gt 0) { throw 'The application pool is used by another IIS application.' }

New-Item -ItemType Directory -Path $physical,$data,$backups -Force | Out-Null
if (-not (Test-Path -LiteralPath "IIS:\AppPools\$SiteName")) { New-WebAppPool -Name $SiteName | Out-Null }
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name enable32BitAppOnWin64 -Value $false
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name processModel.maxProcesses -Value 1
if (-not $site) { New-Website -Name $SiteName -PhysicalPath $physical -ApplicationPool $SiteName -Port 80 -HostHeader $HostName | Out-Null }
$https = Get-WebBinding -Name $SiteName -Protocol https | Where-Object bindingInformation -EQ "*:443:$HostName"
if (-not $https) { New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -SslFlags 1 }
$https = Get-WebBinding -Name $SiteName -Protocol https | Where-Object bindingInformation -EQ "*:443:$HostName"
$https.AddSslCertificate($certificate.Thumbprint, 'My')
Set-PrivateDirectoryAcl -Path $data -ApplicationPool $SiteName
Set-PrivateDirectoryAcl -Path $backups
& icacls $physical /grant "IIS AppPool\${SiteName}:(OI)(CI)(RX)" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unable to grant website read access.' }
Start-WebAppPool -Name $SiteName
Start-Website -Name $SiteName
Write-Host "IIS ready: $SiteName -> $physical; https://$HostName; data: $data"
Write-Host 'The first workflow deploy will install the application. Then configure the administrator with --set-admin.'
