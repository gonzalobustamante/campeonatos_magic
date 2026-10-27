Set-StrictMode -Version Latest

function Resolve-SafeDirectory {
    param([Parameter(Mandatory=$true)][string]$Path)
    if (-not [IO.Path]::IsPathRooted($Path) -or $Path -notmatch '^[A-Za-z]:\\' -or $Path.StartsWith('\\')) {
        throw "Use an absolute local directory: $Path"
    }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $root = [IO.Path]::GetPathRoot($full).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ($full -eq $root) { throw 'A drive root cannot be a deployment directory.' }
    $ancestor = $full
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse points are not allowed: $ancestor" }
        }
        $parent = Split-Path -Path $ancestor -Parent
        if ($parent -eq $ancestor) { break }
        $ancestor = $parent
    }
    return $full
}

function Set-PrivateDirectoryAcl {
    param([string]$Path, [string]$ApplicationPool)
    $safePath = Resolve-SafeDirectory $Path
    $permissions = @('*S-1-5-18:(OI)(CI)(F)', '*S-1-5-32-544:(OI)(CI)(F)')
    if ($ApplicationPool) { $permissions += "IIS AppPool\${ApplicationPool}:(OI)(CI)(M)" }
    & icacls $safePath /inheritance:r /grant:r $permissions | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not protect directory permissions: $safePath" }
}

function Test-DirectoryOverlap {
    param([string]$First, [string]$Second)
    $a = [IO.Path]::GetFullPath($First).TrimEnd('\', '/')
    $b = [IO.Path]::GetFullPath($Second).TrimEnd('\', '/')
    return $a.Equals($b, [StringComparison]::OrdinalIgnoreCase) -or
        $a.StartsWith($b + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $b.StartsWith($a + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparsePoints {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        $links = @(Get-ChildItem -LiteralPath $Path -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
        if ($links.Count -gt 0) { throw "Unexpected reparse points under $Path. Deployment stopped." }
    }
}

function Set-ProductionWebConfig {
    param([string]$Path, [string]$DataDirectory, [string]$CommitSha, [string]$PreviousPath)
    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $asp = $document.SelectSingleNode('//system.webServer/aspNetCore')
    if ($null -eq $asp) { throw 'Missing aspNetCore element in web.config.' }
    $values = @{}
    if ($PreviousPath -and (Test-Path -LiteralPath $PreviousPath)) {
        [xml]$previous = Get-Content -LiteralPath $PreviousPath -Raw
        foreach ($node in $previous.SelectNodes('//aspNetCore/environmentVariables/environmentVariable')) {
            $values[$node.GetAttribute('name')] = $node.GetAttribute('value')
        }
    }
    $values['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $values['DOTNET_ENVIRONMENT'] = 'Production'
    $values['DataDirectory'] = $DataDirectory
    $values['Deployment__CommitSha'] = $CommitSha
    $environment = $asp.SelectSingleNode('environmentVariables')
    if ($null -ne $environment) { [void]$asp.RemoveChild($environment) }
    $environment = $document.CreateElement('environmentVariables')
    foreach ($name in $values.Keys) {
        $entry = $document.CreateElement('environmentVariable')
        $entry.SetAttribute('name', $name)
        $entry.SetAttribute('value', $values[$name])
        [void]$environment.AppendChild($entry)
    }
    [void]$asp.AppendChild($environment)
    $document.Save($Path)
}
