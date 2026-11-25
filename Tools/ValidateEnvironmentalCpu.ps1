#requires -Version 7.0
param([ValidateSet('reference','optimized','regressions')][string]$Phase = 'optimized', [string]$TestFilter)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$audit = Join-Path $repo 'work/EnvironmentalCpuPass'
$project = Join-Path $audit 'UnityProject'
$version = ((Get-Content -LiteralPath (Join-Path $repo 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$unity = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
$owner = Join-Path $audit 'OWNER.txt'
if ((Test-Path -LiteralPath $project) -and !(Test-Path -LiteralPath $owner)) { throw 'Existing validation project has no ownership record; refusing to modify it.' }
if ((Test-Path -LiteralPath $owner) -and !(Get-Content -LiteralPath $owner -Raw).StartsWith('Owned by the geodesic environmental CPU optimization chat, 2026-11-22.')) { throw 'Validation workspace belongs to another task; refusing to modify it.' }
$lockFile = Join-Path $project 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $lockFile) {
    try { $lockCheck = [IO.File]::Open($lockFile,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $lockCheck.Dispose() }
    catch { throw 'The validation project is active in another Unity process; refusing to synchronize its sources.' }
}
New-Item -ItemType Directory -Path "$project/Assets", "$project/Packages", "$project/ProjectSettings", "$audit/Results" -Force | Out-Null
if (!(Test-Path -LiteralPath $owner)) { 'Owned by the geodesic environmental CPU optimization chat, 2026-11-22. One reusable disposable project; reference and optimized sources share it. Do not use while validation is running.' | Set-Content -LiteralPath $owner }
foreach ($entry in @(@('Assets/Scripts','Assets/Scripts'),@('Assets/Tests/EditMode','Assets/Tests/EditMode'))) {
    & robocopy (Join-Path $repo $entry[0]) (Join-Path $project $entry[1]) /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "Copy failed: $($entry[0])" }
}
if ($Phase -eq 'reference') {
    foreach ($spec in @(@('GeodesicOceanResourceField.cs','Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicOceanResourceField.cs'),@('GeodesicSurfaceTemperatureField.cs','Assets/Scripts/Planet/Environment/Temperature/GeodesicSurfaceTemperatureField.cs'))) {
        Copy-Item -LiteralPath (Join-Path $audit "Before/Instrumented/$($spec[0])") -Destination (Join-Path $project $spec[1])
    }
}
# Local read-only package references reuse the live checkout's restored package sources;
# no second downloaded package cache and no shared writable Unity Library.
$manifest = Get-Content -LiteralPath (Join-Path $repo 'Packages/manifest.json') -Raw | ConvertFrom-Json -AsHashtable
foreach ($dir in (Get-ChildItem -LiteralPath (Join-Path $repo 'Library/PackageCache') -Directory)) {
    $packageJson = Join-Path $dir.FullName 'package.json'
    if (Test-Path -LiteralPath $packageJson) {
        $package = Get-Content -LiteralPath $packageJson -Raw | ConvertFrom-Json
        $manifest.dependencies[$package.name] = 'file:' + $dir.FullName.Replace('\','/')
    }
}
$manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath "$project/Packages/manifest.json" -Encoding utf8
Copy-Item -LiteralPath (Join-Path $repo 'ProjectSettings/ProjectVersion.txt') -Destination "$project/ProjectSettings"
$settings = [IO.File]::ReadAllText((Join-Path $repo 'ProjectSettings/ProjectSettings.asset'))
$settings = [regex]::Replace($settings,'(?m)^  companyName:.*$','  companyName: SimulaVitEnvironmentalValidation')
[IO.File]::WriteAllText("$project/ProjectSettings/ProjectSettings.asset",$settings)
Copy-Item -LiteralPath (Join-Path $repo 'Assets/PlanetScene.unity'),(Join-Path $repo 'Assets/PlanetScene.unity.meta') -Destination "$project/Assets"
# This guard exists only in the disposable source. Never write the user's startup config.
$startup = "$project/Assets/Scripts/Core/Startup/SimulationStartupController.cs"
$text = [IO.File]::ReadAllText($startup)
$text = [regex]::Replace($text,'private void SaveStartupConfig\(SimulationStartupConfig config\)\s*\{','$0' + [Environment]::NewLine + '        if (Application.isBatchMode) return; // Disposable validation only.')
[IO.File]::WriteAllText($startup,$text)
$filter = if ($Phase -eq 'regressions') {
    '^(GeodesicAbioticChemistryTests|GeodesicHorizontalMixingActivityTests|GeodesicAtmosphereTests|GeodesicAtmosphereClimateTests|GeodesicVentThermalModelTests|GeodesicPhysicalScaleTests|GeodesicHydrogenotrophyIntegrationTests|GeodesicHydrogenotrophyBalanceTests|GeodesicSulfurRuntimeTests|GeodesicFermentationRuntimeTests|GeodesicFermentationEcologyTests|SimulationSpeedSemanticsTests|SimulationPerformanceAnalyzerTests)\.'
} else { '^GeodesicEnvironmentalCpuParityTests\.' }
if ($TestFilter) { $filter = $TestFilter }
$label = if ($TestFilter) { "$Phase-filtered" } else { $Phase }
$oldPhase = $env:SIMULAVIT_ENVIRONMENTAL_CPU_PHASE
$env:SIMULAVIT_ENVIRONMENTAL_CPU_PHASE = $Phase
try {
    $arguments = @('-batchmode','-nographics','-projectPath',('"'+$project+'"'),'-runTests','-testPlatform','EditMode','-assemblyNames','SimulaVit.EditModeTests','-testFilter',('"'+$filter+'"'),'-testResults',('"'+(Join-Path $audit "Results/$label-results.xml")+'"'),'-logFile',('"'+(Join-Path $audit "Results/$label-unity.log")+'"'))
    $process = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    Write-Output "Unity $Phase exit: $($process.ExitCode); results: $audit/Results/$label-results.xml"
    exit $process.ExitCode
} finally { $env:SIMULAVIT_ENVIRONMENTAL_CPU_PHASE = $oldPhase }
