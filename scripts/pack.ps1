<#
    Packages the module for the SwissTechKnife host (docs/MODULES.md in the host repo):
      artifacts/tamarillo-<ver>.zip     module.json at the root, no SwissTechKnife.Sdk.dll
      artifacts/catalog-entry.json      the entry to paste into the host's catalog/catalog.json
    The version comes from Directory.Build.props and is written into module.json.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Tamarillo'
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

# module.json follows the build version.
$manifestPath = Join-Path $project 'module.json'
$utf8 = New-Object Text.UTF8Encoding $false
$manifest = [IO.File]::ReadAllText($manifestPath, $utf8)
# Only the first "version" (the module's); the ones in releaseNotes name past versions.
$manifest = ([regex]'"version":\s*"[^"]*"').Replace($manifest, "`"version`": `"$version`"", 1)
[IO.File]::WriteAllText($manifestPath, $manifest, $utf8)

if (-not $NoBuild) {
    dotnet build $project -c $Configuration -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

$output = Join-Path $project "bin/$Configuration/net48"
Copy-Item $manifestPath $output -Force
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
$zipPath = Join-Path $artifacts "tamarillo-$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }

$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    Get-ChildItem $output -Recurse -File |
        Where-Object { $_.Name -notlike 'SwissTechKnife.Sdk.*' -and $_.Extension -notin '.pdb', '.xml' } |
        ForEach-Object {
            $relative = $_.FullName.Substring($output.Length).TrimStart('\', '/').Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $relative, 'Optimal') | Out-Null
        }
} finally { $zip.Dispose() }

$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $zipPath).Length
$entry = [ordered]@{
    id          = 'tamarillo'
    name        = 'Tamarillo'
    tagline     = 'Arraste, segure o atalho e solte: converta e trate arquivos numa roda.'
    description = "Segure Ctrl + Alt + Shift enquanto arrasta arquivos e uma roda aparece sob o cursor com o que dá para fazer com eles: converter imagens, áudio, vídeo, PDF e documentos, redimensionar, comprimir, girar, reconhecer texto, compactar e mais. Solte sobre a ação e o resultado aparece ao lado do original, com o progresso num aviso discreto.`n`nTudo roda neste computador com os componentes do Windows; FFmpeg e Office ampliam os formatos quando estão instalados."
    version     = $version
    icon        = 'M11,5.5 A7.5,7.5 0 1 0 18.5,13 L11,13 Z M13.5,10.5 L13.5,3 A7.5,7.5 0 0 1 21,10.5 Z'
    accent      = '#E65A70'
    settings    = $true
    package     = "https://github.com/luingry/Tamarillo/releases/download/v$version/tamarillo-$version.zip"
    sha256      = $hash
    size        = $size
}
[IO.File]::WriteAllText((Join-Path $artifacts 'catalog-entry.json'), ($entry | ConvertTo-Json -Depth 4), $utf8)
Write-Host "Packed $zipPath ($size bytes, sha256 $hash)" -ForegroundColor Green
