$ErrorActionPreference = 'Stop'
$revitDir = Join-Path $env:ProgramFiles 'Autodesk\Revit 2026'
$runtime = & (Join-Path $PSScriptRoot 'scripts\resolve-revit-runtime.ps1') -RevitVersion 2026 -RevitDir $revitDir
if ($runtime -ne '8') { throw 'This package is for Revit 2026.0-2026.4 on .NET 8 only.' }
& (Join-Path $PSScriptRoot 'build-native.ps1') -RevitVersion 2026 -RevitRuntime 8
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$built = Join-Path $PSScriptRoot 'artifacts\addin\2026\net8.0-windows\Hack2DExport.dll'
$payloadDir = Join-Path $PSScriptRoot 'installer\payload'
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
Copy-Item -LiteralPath $built -Destination (Join-Path $payloadDir 'Hack2DExport.dll') -Force
$publish = Join-Path $PSScriptRoot 'artifacts\installer-publish\2026'
dotnet publish (Join-Path $PSScriptRoot 'installer\Hack2DExportInstaller.csproj') -c Release -o $publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$installer = Join-Path $dist 'Hack2DExport-Revit2026-Installer.exe'
Copy-Item -LiteralPath (Join-Path $publish 'Hack2DExport-Revit2026-Installer.exe') -Destination $installer -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'installer\README.txt') -Destination (Join-Path $dist 'README-Revit2026.txt') -Force
$zip = Join-Path $dist 'Hack2DExport-Revit2026.zip'
Compress-Archive -LiteralPath $installer, (Join-Path $dist 'README-Revit2026.txt') -DestinationPath $zip -Force
Get-FileHash -LiteralPath $installer -Algorithm SHA256 | Select-Object Path,Hash
