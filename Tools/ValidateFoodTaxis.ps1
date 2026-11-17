#requires -Version 7.0
param([switch]$CompileManaged, [ValidateSet('baseline','food','alternatives')][string]$EditorTests,
    [string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $repo 'Temp/FoodTaxisAudit'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$runtime = Join-Path $repo 'Temp/HydrogenotrophyAudit'
if ($CompileManaged) {
    & (Join-Path $PSScriptRoot 'ValidateHydrogenotrophy.ps1') -CompileManaged -UnityData $UnityData
} else {
    $nunit = Get-ChildItem (Join-Path $repo 'Library/PackageCache/com.unity.ext.nunit*/net40/unity-custom/nunit.framework.dll') | Select-Object -First 1 -ExpandProperty FullName
    Add-Type -Path $nunit
    Add-Type -Path (Join-Path $UnityData 'Managed/UnityEngine/UnityEngine.CoreModule.dll')
    Add-Type -Path (Join-Path $repo 'Library/ScriptAssemblies/Unity.Profiling.Core.dll')
    [Reflection.Assembly]::LoadFrom((Join-Path $runtime 'SimulaVit.dll')) | Out-Null
}
$tests = [Reflection.Assembly]::LoadFrom((Join-Path $runtime 'SimulaVit.EditModeTests.dll'))
$passed = 0; $failed = 0
foreach ($name in @('GeodesicFoodSignalTests','GeodesicTaxisPolicyTests')) {
    $fixture = [Activator]::CreateInstance($tests.GetType($name))
    foreach ($method in $fixture.GetType().GetMethods()) {
        $cases = @($method.GetCustomAttributes([NUnit.Framework.TestCaseAttribute], $false))
        if (!$cases.Length -and !$method.GetCustomAttributes([NUnit.Framework.TestAttribute], $false).Length) { continue }
        if (!$cases.Length) { $cases = @($null) }
        foreach ($case in $cases) {
            $values = if ($null -eq $case) { @() } else { $case.Arguments }
            try { $method.Invoke($fixture, $values) | Out-Null; $passed++; "PASS $name.$($method.Name) ($values)" }
            catch { $failed++; "FAIL $name.$($method.Name): $($_.Exception.InnerException)" }
        }
    }
}
"Food-signal managed tests: $passed passed; $failed failed. Native movement and ecological tests are separate."
if ($failed) { throw "$failed food-signal tests failed" }
if ($EditorTests) {
    Set-Content -LiteralPath (Join-Path $directory 'editor-enabled') -Value 'Local opt-in diagnostic test runner' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $directory 'editor-command.txt') -Value $EditorTests -Encoding utf8
    Set-Content -LiteralPath (Join-Path $directory 'editor-refresh.txt') -Value 'Import current audit sources before running' -Encoding utf8
    "Queued $EditorTests for the existing Editor. Assets > Refresh may be needed. This is not a completed test result."
}
