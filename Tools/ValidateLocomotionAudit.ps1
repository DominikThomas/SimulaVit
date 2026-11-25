#requires -Version 7.0
param([switch]$CompileManaged, [switch]$UnityTests, [int]$AttemptsPerRow = 1000000,
    [string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$runtimeDirectory = Join-Path $repo 'Temp/HydrogenotrophyAudit'
if ($CompileManaged) {
    & (Join-Path $PSScriptRoot 'ValidateHydrogenotrophy.ps1') -CompileManaged -UnityData $UnityData
} else {
    $nunit = Get-ChildItem (Join-Path $repo 'Library/PackageCache/com.unity.ext.nunit*/net40/unity-custom/nunit.framework.dll') | Select-Object -First 1 -ExpandProperty FullName
    Add-Type -Path $nunit
    Add-Type -Path (Join-Path $UnityData 'Managed/UnityEngine/UnityEngine.CoreModule.dll')
    Add-Type -Path (Join-Path $repo 'Library/ScriptAssemblies/Unity.Profiling.Core.dll')
    [System.Reflection.Assembly]::LoadFrom((Join-Path $runtimeDirectory 'SimulaVit.dll')) | Out-Null
}
$assembly = [System.Reflection.Assembly]::LoadFrom((Join-Path $runtimeDirectory 'SimulaVit.EditModeTests.dll'))
$passed = 0; $failed = 0
foreach ($name in @('GeodesicLocomotionMutationTests', 'GeodesicLocomotionEvolutionDiagnosticsTests', 'GeodesicTaxisPolicyTests')) {
    $fixture = [Activator]::CreateInstance($assembly.GetType($name))
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
"Locomotion managed tests: $passed passed; $failed failed. Native ecological fixtures are separate."
if ($failed) { throw "$failed locomotion tests failed" }
$refs = @((Join-Path $runtimeDirectory 'SimulaVit.dll'), (Join-Path $UnityData 'Managed/UnityEngine/UnityEngine.CoreModule.dll')) +
    @(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -Path (Join-Path $PSScriptRoot 'LocomotionMutationExperiment.cs') -ReferencedAssemblies $refs
$outputDirectory = Join-Path $repo 'Temp/LocomotionAudit'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$rows = [LocomotionMutationExperiment]::Run($AttemptsPerRow)
$rows | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDirectory 'mutation-experiment.json') -Encoding utf8
$rows | Select-Object configuration,habitat,parent,@{n='observed(P/A/F/X)';e={$_.observed -join '/'}},@{n='expected(P/A/F/X)';e={($_.expected | ForEach-Object {$_.ToString('F2',[cultureinfo]::InvariantCulture)}) -join '/'}} | Format-Table -AutoSize
$geometry = [LocomotionMutationExperiment]::MeasureGeometry()
$geometry | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectory 'geometry.json') -Encoding utf8
$geometry | Format-List
if ($UnityTests) {
    $unityExe = Join-Path (Split-Path -Parent $UnityData) 'Unity.exe'
    $unityArgs = @('-batchmode','-nographics','-projectPath',('"' + $repo + '"'),'-runTests','-testPlatform','EditMode',
        '-testFilter','GeodesicLocomotionMutationTests;GeodesicLocomotionEvolutionDiagnosticsTests;GeodesicTaxisPolicyTests;GeodesicLocomotionEcologyExperiments',
        '-testResults',('"' + (Join-Path $outputDirectory 'unity-results.xml') + '"'),
        '-logFile',('"' + (Join-Path $outputDirectory 'unity.log') + '"'))
    $process = Start-Process -FilePath $unityExe -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
    $process.WaitForExit()
    if ($process.ExitCode) { throw "Unity test runner exited $($process.ExitCode). Inspect Temp/LocomotionAudit/unity.log; managed results do not establish ecological outcomes." }
    if (!(Test-Path -LiteralPath (Join-Path $outputDirectory 'unity-results.xml'))) { throw 'Unity produced no test result XML.' }
}
