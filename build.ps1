param(
    [ValidateSet('2026', '2027')][string]$RevitVersion = '2027',
    [string]$RevitDir,
    [string]$AgentulatorAddinDir,
    [ValidateSet('auto', '8', '10')][string]$RevitRuntime = 'auto'
)
$ErrorActionPreference = 'Stop'
if (-not $RevitDir) { $RevitDir = Join-Path $env:ProgramFiles "Autodesk\Revit $RevitVersion" }
if (-not $AgentulatorAddinDir) { $AgentulatorAddinDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion\Agentulator" }
foreach ($required in @(
    (Join-Path $RevitDir 'RevitAPI.dll'),
    (Join-Path $RevitDir 'RevitAPIUI.dll'),
    (Join-Path $AgentulatorAddinDir 'Agentulator.Contracts.dll'),
    (Join-Path $AgentulatorAddinDir 'Agentulator.RevitSdk.dll')
)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing build reference: $required" }
}
$resolvedRuntime = & (Join-Path $PSScriptRoot 'scripts\resolve-revit-runtime.ps1') -RevitVersion $RevitVersion -RevitDir $RevitDir -RevitRuntime $RevitRuntime
Write-Output "Building Agentulator adapter for Revit $RevitVersion / .NET $resolvedRuntime"
# Revit ships framework assemblies with versions that differ from the .NET SDK;
# the host-side Agentulator compiler is also run as a separate compatibility check.
dotnet build (Join-Path $PSScriptRoot 'plugin\View2dExport\View2dExport.csproj') -c Release "-p:RevitVersion=$RevitVersion" "-p:RevitRuntime=$resolvedRuntime" "-p:RevitDir=$RevitDir" "-p:AgentulatorAddinDir=$AgentulatorAddinDir" '-p:NoWarn=MSB3277'
exit $LASTEXITCODE
