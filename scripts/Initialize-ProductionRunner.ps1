[CmdletBinding()]
param(
    [string]$RunnerDirectory = 'C:\actions-runner-magic',
    [string]$RunnerName = "$env:COMPUTERNAME-MagicCampeonatos",
    [Security.SecureString]$RegistrationToken
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$repository = 'https://github.com/gonzalobustamante/campeonatos_magic'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as administrator on the production server.' }
$directory = Resolve-SafeDirectory $RunnerDirectory
if (Test-Path -LiteralPath (Join-Path $directory '.runner')) {
    $registered = Get-Content -LiteralPath (Join-Path $directory '.runner') -Raw | ConvertFrom-Json
    if ($registered.gitHubUrl.TrimEnd('/') -ne $repository) { throw 'This directory belongs to another runner; use a new empty directory.' }
    Write-Host 'This repository runner is already registered. No existing runner was modified.'
    return
}
if ((Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -gt 0) { throw 'Choose an empty directory dedicated to this new runner.' }

# Pinned official GitHub Actions runner and its published SHA-256 checksum.
$version = '2.338.2'
$expectedSha = 'a1f17aa987fc8158bc7521d284912267a0d01b7c2c4684b33c86783b5e8221cc'
$download = "https://github.com/actions/runner/releases/download/v$version/actions-runner-win-x64-$version.zip"
New-Item -ItemType Directory -Path $directory -Force | Out-Null
Set-PrivateDirectoryAcl -Path $directory
$archive = Join-Path $env:TEMP ("magic-runner-" + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $download -OutFile $archive -UseBasicParsing
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedSha) { throw 'Runner download checksum does not match the official release.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $directory
}
finally { if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force } }
if ($null -eq $RegistrationToken) {
    Write-Host "Get the temporary token from $repository/settings/actions/runners/new (Windows x64)."
    $RegistrationToken = Read-Host 'Registration token (hidden)' -AsSecureString
}
$tokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($RegistrationToken)
try {
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPointer)
    Push-Location $directory
    try {
        # LocalSystem has the IIS and filesystem permissions needed for this dedicated deployment runner.
        & .\config.cmd --unattended --url $repository --token $token --name $RunnerName --labels production --work _work --runasservice --windowslogonaccount 'NT AUTHORITY\SYSTEM'
        if ($LASTEXITCODE -ne 0) { throw 'GitHub runner registration failed.' }
    }
    finally { Pop-Location }
}
finally {
    $token = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPointer)
}
$runnerServices = @(Get-CimInstance Win32_Service | Where-Object { $_.Name -like 'actions.runner.*' -and $_.PathName -like "*$directory\*" })
if ($runnerServices.Count -ne 1) { throw 'Registration finished; check the dedicated runner service in Windows Services.' }
Start-Service -Name $runnerServices[0].Name
Write-Host "Runner configured for campeonatos_magic with labels self-hosted, Windows, X64, production. Service: $($runnerServices[0].Name)"
