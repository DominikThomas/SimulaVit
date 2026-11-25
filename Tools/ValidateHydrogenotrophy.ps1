#requires -Version 7.0
# Runs the production pure transaction planner and NUnit invariants without Unity.
param(
    [switch]$CompileManaged,
    [string]$ManagedRuntimeDirectory,
    [switch]$Replay,
    [string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Data'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ($CompileManaged) {
    $ManagedRuntimeDirectory = Join-Path $repo 'Temp/HydrogenotrophyAudit'
    New-Item -ItemType Directory -Path $ManagedRuntimeDirectory -Force | Out-Null
    function Write-AuditResponse([string]$assemblyName, [string]$sourceDirectory) {
        $cached = Get-ChildItem (Join-Path $repo 'Library/Bee/artifacts') -Recurse -Filter "$assemblyName.rsp" |
            Sort-Object LastWriteTime -Descending |
            Where-Object { (Get-Content -LiteralPath $_.FullName -Raw -Encoding utf8).Contains('-define:UNITY_EDITOR') } |
            Select-Object -First 1
        if (!$cached) { throw "No restored Editor compiler response for $assemblyName. Open the project in Unity to restore it." }
        $lines = [System.Collections.Generic.List[string]]::new()
        $sources = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($line in (Get-Content -LiteralPath $cached.FullName -Encoding utf8)) {
            if ($line.StartsWith('-out:')) { $line = '-out:"' + (Join-Path $ManagedRuntimeDirectory "$assemblyName.dll") + '"' }
            elseif ($line.StartsWith('-refout:')) { $line = '-refout:"' + (Join-Path $ManagedRuntimeDirectory "$assemblyName.ref.dll") + '"' }
            elseif ($assemblyName -ne 'SimulaVit' -and $line.StartsWith('-r:') -and
                ($line.Replace('\','/').EndsWith('/SimulaVit.ref.dll"') -or $line.Replace('\','/').EndsWith('/SimulaVit.dll"'))) {
                $line = '-r:"' + (Join-Path $ManagedRuntimeDirectory 'SimulaVit.ref.dll') + '"'
            }
            if ($line.Trim('"').EndsWith('.cs')) { $sources.Add($line.Trim('"').Replace('\','/')) | Out-Null }
            $lines.Add($line)
        }
        foreach ($source in (Get-ChildItem (Join-Path $repo $sourceDirectory) -Recurse -Filter '*.cs')) {
            $relative = [System.IO.Path]::GetRelativePath($repo, $source.FullName).Replace('\','/')
            if ($sources.Add($relative)) { $lines.Add('"' + $relative + '"') }
        }
        $response = Join-Path $ManagedRuntimeDirectory "$assemblyName.audit.rsp"
        $lines | Set-Content -LiteralPath $response -Encoding utf8
        return $response
    }
    Push-Location $repo
    try {
        foreach ($spec in @(@('SimulaVit','Assets/Scripts'), @('SimulaVit.EditModeTests','Assets/Tests/EditMode'))) {
            $response = Write-AuditResponse $spec[0] $spec[1]
            & dotnet exec --roll-forward Major (Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll') "@$response"
            if ($LASTEXITCODE) { throw "$($spec[0]) managed compilation failed." }
        }
    } finally { Pop-Location }
}
$nunit = Get-ChildItem (Join-Path $repo 'Library/PackageCache/com.unity.ext.nunit*/net40/unity-custom/nunit.framework.dll') | Select-Object -First 1 -ExpandProperty FullName
if (!$nunit) { throw 'The restored Unity NUnit package is required.' }
Add-Type -Path $nunit
$refs = @($nunit) + @(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
$sources = @(
    'Assets/Scripts/Biology/Replicators/GeodesicHydrogenotrophyBalance.cs',
    'Assets/Tests/EditMode/GeodesicHydrogenotrophyBalanceTests.cs',
    'Tools/HydrogenotrophyConservationReplay.cs'
) | ForEach-Object { Join-Path $repo $_ }
Add-Type -Path $sources -ReferencedAssemblies $refs
$fixture = [GeodesicHydrogenotrophyBalanceTests]::new()
$passed = 0; $failed = 0
foreach ($method in $fixture.GetType().GetMethods()) {
    if (!$method.GetCustomAttributes([NUnit.Framework.TestAttribute], $false).Length) { continue }
    try { $method.Invoke($fixture, @()) | Out-Null; $passed++; "PASS $($method.Name)" }
    catch { $failed++; "FAIL $($method.Name): $($_.Exception.InnerException)" }
}
"Hydrogenotrophy pure transaction tests: $passed passed; $failed failed. Unity integration tests are separate."
if ($ManagedRuntimeDirectory) {
    # Run only engine-independent methods from the compiled production/test assemblies.
    # This does not stand in for GameObject/lifecycle tests in Unity Test Runner.
    Add-Type -Path (Join-Path $UnityData 'Managed/UnityEngine/UnityEngine.CoreModule.dll')
    Add-Type -Path (Join-Path $repo 'Library/ScriptAssemblies/Unity.Profiling.Core.dll')
    [System.Reflection.Assembly]::LoadFrom((Join-Path $ManagedRuntimeDirectory 'SimulaVit.dll')) | Out-Null
    $assembly = [System.Reflection.Assembly]::LoadFrom((Join-Path $ManagedRuntimeDirectory 'SimulaVit.EditModeTests.dll'))
    $fixtures = @{
        GeodesicBiologyRuntimeTests = '^(HydrogenotrophyActivity|HydrogenotrophyModifiers|SharedCompetition|MultipleMetabolismResources|SharedHabitatTemperature)'
        GeodesicHydrogenotrophyDiagnosticsTests = '.*'
        GeodesicAbioticChemistryTests = '.*'
        GeodesicHorizontalMixingActivityTests = '.*'
    }
    foreach ($name in $fixtures.Keys) {
        $type = $assembly.GetType($name)
        $instance = [Activator]::CreateInstance($type)
        foreach ($method in $type.GetMethods()) {
            if ($method.Name -notmatch $fixtures[$name]) { continue }
            $cases = @($method.GetCustomAttributes([NUnit.Framework.TestCaseAttribute], $false))
            $isTest = $method.GetCustomAttributes([NUnit.Framework.TestAttribute], $false).Length -gt 0
            if (!$isTest -and !$cases.Length) { continue }
            if (!$cases.Length) { $cases = @($null) }
            foreach ($case in $cases) {
                $values = if ($null -eq $case) { @() } else { $case.Arguments }
                try { $method.Invoke($instance, $values) | Out-Null; $passed++; "PASS $name.$($method.Name) ($values)" }
                catch { $failed++; "FAIL $name.$($method.Name): $($_.Exception.InnerException)" }
            }
        }
    }
    "All selected managed tests: $passed passed; $failed failed. Native Unity integration remains separate."
}
if ($failed) { throw "$failed hydrogenotrophy tests failed" }
if ($Replay) {
    $reports = [HydrogenotrophyConservationReplay]::Run(123456)
    $corrected = $reports | Where-Object mode -eq 'corrected-double-atomic'
    $balanceTolerance = [GeodesicHydrogenotrophyBalance]::AbsoluteTolerance + [GeodesicHydrogenotrophyBalance]::RelativeTolerance * $corrected.co2Measured
    if ([Math]::Abs($corrected.wholeSystemResidual) -gt $balanceTolerance -or
        $corrected.modelResidual -ne 0 -or $corrected.inputTransferViolations -ne 0 -or
        $corrected.zeroMeasuredInputWithEnergy -ne 0) { throw 'Corrected replay failed its conservation/transfer invariants.' }
    $outputDirectory = Join-Path $repo 'Temp/HydrogenotrophyAudit'
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $reports | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputDirectory 'reaction-replay.json') -Encoding utf8
    $reports | Format-List mode,seed,seconds,co2Planned,co2Measured,h2Measured,storeGains,carbonProducts,energy,modelResidual,reactionMeasuredResidual,wholeSystemResidual,inputTransferViolations,zeroMeasuredInputWithEnergy,precisionRejected
}
