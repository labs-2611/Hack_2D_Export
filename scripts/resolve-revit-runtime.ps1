param(
    [ValidateSet('2026', '2027')][string]$RevitVersion,
    [string]$RevitDir,
    [ValidateSet('auto', '8', '10')][string]$RevitRuntime = 'auto'
)
$ErrorActionPreference = 'Stop'
if ($RevitRuntime -ne 'auto') { return $RevitRuntime }
if ($RevitVersion -eq '2027') { return '10' }
$api = Join-Path $RevitDir 'RevitAPI.dll'
if (-not (Test-Path -LiteralPath $api)) { throw "Missing Revit API reference: $api" }
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($api)
if ($version.FileMajorPart -ne 26) {
    throw "Expected Revit 2026 API in $RevitDir; found version $($version.FileVersion)."
}
if ($version.FileMinorPart -ge 5) { return '10' }
return '8'
