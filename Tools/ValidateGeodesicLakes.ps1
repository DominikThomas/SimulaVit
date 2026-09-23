#requires -Version 7.0
param(
    [string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Data',
    [switch]$Benchmarks,
    [switch]$GradeAudit,
    [int[]]$AuditSubdivisions = @(6, 7),
    [int]$AuditSeed = 12345,
    [switch]$AuditEarthlike
)
# Pure algorithm tests only. Native GameObject/JSON/shader integration tests require Unity Test Runner.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$core = Join-Path $UnityData 'Managed/UnityEngine/UnityEngine.CoreModule.dll'
$nunit = Get-ChildItem (Join-Path $repo 'Library/PackageCache/com.unity.ext.nunit*/net40/unity-custom/nunit.framework.dll') | Select-Object -First 1 -ExpandProperty FullName
if (!$nunit) { throw 'Open the project once in Unity to restore its NUnit package.' }
Add-Type -Path $core,$nunit
$paths = @('Assets/Scripts/Planet/Geodesic/Grid/GeodesicGridTopology.cs','Assets/Scripts/Planet/Geodesic/Rendering/IcosphereRenderGeometry.cs','Assets/Scripts/Planet/Geodesic/Rendering/IcosphereRenderGeometryCache.cs','Assets/Scripts/Planet/Geodesic/Rendering/IcosphereRenderMeshBuilder.cs','Assets/Scripts/Planet/Geodesic/Terrain/PlanetTerrainSampler.cs','Assets/Scripts/Planet/Geodesic/Terrain/PlanetTerrainSettings.cs','Assets/Scripts/Utilities/SimpleNoise.cs','Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicDrainageGraph.cs','Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverTerrain.cs','Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverPath.cs','Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverGradeAudit.cs','Assets/Tests/EditMode/GeodesicLakeTests.cs','Assets/Tests/EditMode/GeodesicRiverGradeTests.cs','Assets/Tests/EditMode/GeodesicDrainageTests.cs','Assets/Tests/EditMode/GeodesicOceanConnectivityTests.cs','Assets/Scripts/Planet/Geodesic/Rendering/GeodesicMaskedOceanGeometry.cs','Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicLakeBasins.cs','Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicLakeGeometry.cs','Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicLakeRiverRouting.cs','Assets/Scripts/Planet/Geodesic/Rendering/IcosphereDirectionMapping.cs','Assets/Scripts/Planet/Geodesic/Rendering/IcosphereDirectionMappingBuilder.cs','Assets/Scripts/Planet/Environment/Ocean/GeodesicOceanConnectivity.cs','Assets/Scripts/Planet/Environment/Ocean/GeodesicOceanLayerGrid.cs','Assets/Scripts/Planet/Environment/Ocean/GeodesicPhysicalScale.cs','Assets/Scripts/Planet/Geodesic/Grid/GeodesicTransportGraph.cs') | ForEach-Object { Join-Path $repo $_ }
$refs = @($core, $nunit) + @(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
$paths += (Join-Path $PSScriptRoot 'GeodesicLakeBenchmark.cs'), (Join-Path $PSScriptRoot 'GeodesicRiverGradeBenchmark.cs')
Add-Type -Path $paths -ReferencedAssemblies $refs

$passed = 0; $failed = 0
foreach ($type in @([GeodesicRiverGradeTests], [GeodesicLakeTests], [GeodesicDrainageTests], [GeodesicOceanConnectivityTests])) {
    $fixture = [Activator]::CreateInstance($type)
    foreach ($method in $type.GetMethods()) {
        if (!$method.GetCustomAttributes([NUnit.Framework.TestAttribute], $false).Length) { continue }
        try { $method.Invoke($fixture, @()) | Out-Null; $passed++; "PASS $($method.Name)" }
        catch { $failed++; "FAIL $($method.Name): $($_.Exception.InnerException)" }
    }
}
"Pure hydrology tests: $passed passed; $failed failed. Native Unity tests are not run by this script."
if ($failed) { throw "$failed hydrology tests failed" }
if ($Benchmarks) {
    foreach ($level in @(6, 7)) {
        'Earthlike settings:'
        [GeodesicLakeBenchmark]::Run(12345, $level)
        'Enclosed-basin fixture (continent/mountain amplitude 0; fine-detail amplitude 0.08):'
        [GeodesicLakeBenchmark]::Run(12345, $level, 0.005, $true)
    }
}

if ($GradeAudit) {
    foreach ($level in $AuditSubdivisions) { [GeodesicRiverGradeBenchmark]::Run($AuditSeed, $level, !$AuditEarthlike.IsPresent) }
}
