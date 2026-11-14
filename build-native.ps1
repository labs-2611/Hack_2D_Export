param(
    [ValidateSet('2026', '2027')][string]$RevitVersion = '2027',
    [string]$RevitDir,
    [ValidateSet('auto', '8', '10')][string]$RevitRuntime = 'auto'
)
$ErrorActionPreference = 'Stop'
if (-not $RevitDir) { $RevitDir = Join-Path $env:ProgramFiles "Autodesk\Revit $RevitVersion" }
foreach ($name in @('RevitAPI.dll', 'RevitAPIUI.dll')) {
    $reference = Join-Path $RevitDir $name
    if (-not (Test-Path -LiteralPath $reference)) { throw "Missing Revit reference: $reference" }
}
$resolvedRuntime = & (Join-Path $PSScriptRoot 'scripts\resolve-revit-runtime.ps1') -RevitVersion $RevitVersion -RevitDir $RevitDir -RevitRuntime $RevitRuntime
Write-Output "Building for Revit $RevitVersion / .NET $resolvedRuntime"
$outputPath = Join-Path $PSScriptRoot "artifacts\addin\$RevitVersion\net$resolvedRuntime.0-windows"
python (Join-Path $PSScriptRoot 'scripts\generate_native_core.py')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build (Join-Path $PSScriptRoot 'addin\Hack2DExport.csproj') -c Release "-p:RevitVersion=$RevitVersion" "-p:RevitRuntime=$resolvedRuntime" "-p:RevitDir=$RevitDir" "-p:OutputPath=$outputPath" '-p:AppendTargetFrameworkToOutputPath=false' '-p:NoWarn=MSB3277'
exit $LASTEXITCODE
