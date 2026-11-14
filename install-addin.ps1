param(
    [ValidateSet('2026', '2027')][string]$RevitVersion = '2027',
    [string]$RevitDir,
    [ValidateSet('auto', '8', '10')][string]$RevitRuntime = 'auto'
)
$ErrorActionPreference = 'Stop'
if (-not $RevitDir) { $RevitDir = Join-Path $env:ProgramFiles "Autodesk\Revit $RevitVersion" }
$resolvedRuntime = & (Join-Path $PSScriptRoot 'scripts\resolve-revit-runtime.ps1') -RevitVersion $RevitVersion -RevitDir $RevitDir -RevitRuntime $RevitRuntime
& (Join-Path $PSScriptRoot 'build-native.ps1') -RevitVersion $RevitVersion -RevitDir $RevitDir -RevitRuntime $resolvedRuntime
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$framework = "net$resolvedRuntime.0-windows"
$builtAssembly = Join-Path $PSScriptRoot "artifacts\addin\$RevitVersion\$framework\Hack2DExport.dll"
if (-not (Test-Path -LiteralPath $builtAssembly)) { throw "Built add-in DLL missing: $builtAssembly" }
$addinFolder = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
New-Item -ItemType Directory -Path $addinFolder -Force | Out-Null
$deployment = Join-Path $addinFolder 'Hack2DExport'
New-Item -ItemType Directory -Path $deployment -Force | Out-Null
$assembly = Join-Path $deployment 'Hack2DExport.dll'
Copy-Item -LiteralPath $builtAssembly -Destination $assembly -Force
$manifest = Join-Path $addinFolder 'Hack2DExport.addin'
$content = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Hack 2D Export</Name>
    <Assembly>$assembly</Assembly>
    <AddInId>47e64a62-0b5f-4c69-a087-7dccdf731180</AddInId>
    <FullClassName>Hack2DExport.AddinApplication</FullClassName>
    <VendorId>2611</VendorId>
    <VendorDescription>2611 Labs</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
Set-Content -LiteralPath $manifest -Value $content -Encoding utf8
Write-Output "Installed native add-in manifest: $manifest"
