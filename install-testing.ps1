$ErrorActionPreference = 'Stop'
$settingsPath = Join-Path $env:APPDATA 'Agentulator\agentulator.settings.json'
$settings = if (Test-Path -LiteralPath $settingsPath) { Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json } else { $null }
$toolRoot = if ($settings -and $settings.toolRoot) { $settings.toolRoot } else { Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Agentulator\tools' }
$source = Join-Path $PSScriptRoot 'plugin\View2dExport'
$target = Join-Path $toolRoot 'testing\View2dExport'
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($name in @('View2dExport.cs', 'manifest.json', 'schema.json', 'skill.md')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $target $name) -Force
}
$testTarget = Join-Path $target 'tests'
New-Item -ItemType Directory -Path $testTarget -Force | Out-Null
Copy-Item -Path (Join-Path $source 'tests\*.json') -Destination $testTarget -Force
Write-Output "Installed View2dExport testing source to $target"
