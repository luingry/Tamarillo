<#
    Release build → tests → module package.
      powershell -File scripts/build.ps1            build + tests + artifacts/tamarillo-<ver>.zip + catalog entry
      powershell -File scripts/build.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

dotnet build (Join-Path $root 'Tamarillo.sln') -c $Configuration -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

if (-not $SkipTests) {
    & (Join-Path $root "tests/Tamarillo.Tests/bin/$Configuration/net48/Tamarillo.Tests.exe")
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

& (Join-Path $PSScriptRoot 'pack.ps1') -Configuration $Configuration -NoBuild
