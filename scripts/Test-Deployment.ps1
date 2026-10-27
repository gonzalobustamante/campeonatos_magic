param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$scripts = Join-Path $RepositoryRoot 'scripts'
foreach ($script in Get-ChildItem -LiteralPath $scripts -Filter '*.ps1') {
    $tokens = $null; $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw "PowerShell parse failed in $($script.Name): $parseErrors" }
}
. (Join-Path $scripts 'Deployment.Common.ps1')

function Assert-Test {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}
function Assert-Rejected {
    param([scriptblock]$Action)
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-Test $rejected 'Unsafe or invalid input was accepted.'
}
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $tempRoot ('magic-deploy-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    Assert-Rejected { Resolve-SafeDirectory 'relative/path' }
    Assert-Rejected { Resolve-SafeDirectory 'C:\' }
    Assert-Rejected { Resolve-SafeDirectory '\\server\share\folder' }
    $site = Join-Path $testRoot 'site'
    $siteSibling = Join-Path $testRoot 'site-backups'
    Assert-Test (Test-DirectoryOverlap $site (Join-Path $site 'child')) 'Nested paths were not detected.'
    Assert-Test (-not (Test-DirectoryOverlap $site $siteSibling)) 'Sibling directories were incorrectly treated as nested.'
    New-Item -ItemType Directory -Path $site | Out-Null
    Assert-Test ((Resolve-SafeDirectory $site) -eq $site) 'Valid path did not resolve.'

    $newConfig = Join-Path $testRoot 'web.config'
    $oldConfig = Join-Path $testRoot 'previous.config'
    '<configuration><location path="."><system.webServer><aspNetCore processPath=".\CampeonatoMagic.exe" hostingModel="inprocess"><environmentVariables><environmentVariable name="DataDirectory" value="wrong" /></environmentVariables></aspNetCore></system.webServer></location></configuration>' | Set-Content $newConfig
    '<configuration><system.webServer><aspNetCore><environmentVariables><environmentVariable name="PrivateSetting" value="retained-test-value" /><environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" /></environmentVariables></aspNetCore></system.webServer></configuration>' | Set-Content $oldConfig
    Set-ProductionWebConfig -Path $newConfig -PreviousPath $oldConfig -DataDirectory 'C:\ProgramData\MagicTest' -CommitSha ('a' * 40)
    [xml]$config = Get-Content $newConfig -Raw
    $nodes = @($config.SelectNodes('//environmentVariable'))
    Assert-Test (@($nodes | Where-Object name -EQ 'DataDirectory').Count -eq 1) 'Duplicate data directory configuration.'
    Assert-Test ($config.SelectSingleNode('//environmentVariable[@name="PrivateSetting"]').value -eq 'retained-test-value') 'Existing server configuration was lost.'
    Assert-Test ($config.SelectSingleNode('//environmentVariable[@name="ASPNETCORE_ENVIRONMENT"]').value -eq 'Production') 'Production environment was not enforced.'
    Assert-Test ($config.SelectSingleNode('//environmentVariable[@name="Deployment__CommitSha"]').value -eq ('a' * 40)) 'Release identity was lost.'
    Assert-Test ($config.SelectSingleNode('//aspNetCore').processPath -eq '.\CampeonatoMagic.exe') 'Application hosting configuration was changed.'

    $fixture = Join-Path $testRoot 'package'
    New-Item -ItemType Directory -Path (Join-Path $fixture 'wwwroot\css'),(Join-Path $fixture 'wwwroot\js') -Force | Out-Null
    foreach ($file in @('CampeonatoMagic.dll','CampeonatoMagic.exe','CampeonatoMagic.runtimeconfig.json','web.config','appsettings.json','wwwroot\css\site.css','wwwroot\js\site.js')) {
        New-Item -ItemType File -Path (Join-Path $fixture $file) | Out-Null
    }
    & (Join-Path $scripts 'Test-DeploymentPackage.ps1') -Path $fixture
    New-Item -ItemType File -Path (Join-Path $fixture 'admin.json') | Out-Null
    Assert-Rejected { & (Join-Path $scripts 'Test-DeploymentPackage.ps1') -Path $fixture }
    Write-Host 'Deployment checks passed: script parsing, safe paths, configuration preservation and sensitive-file exclusion.'
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTestRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path $resolvedTestRoot -Leaf) -match '^magic-deploy-test-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    } else { throw 'Refusing to remove an unexpected test directory.' }
}
