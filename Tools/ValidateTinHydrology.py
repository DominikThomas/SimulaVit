#!/usr/bin/env python3
"""Run actual managed hydrology sources against Unity's assemblies, without Editor licensing.

This does not replace native Unity integration tests or licensed screenshot acceptance.
All generated compilation files live in work/tin-validation.
"""
import argparse
import os
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--unity-data', required=True, type=Path)
parser.add_argument('--benchmark', type=int, choices=range(1, 8))
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
work = root / 'work' / 'tin-validation'
work.mkdir(parents=True, exist_ok=True)
data = args.unity_data.resolve()
dotnet = data / 'NetCoreRuntime' / 'dotnet'
mono = data / 'MonoBleedingEdge' / 'bin' / 'mono'
core = data / 'Managed' / 'UnityEngine'
nunit = data / 'Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/net40/unity-custom'
sources = list((root / 'Assets/Scripts/Planet/Geodesic/Hydrology').glob('*.cs'))
sources = [p for p in sources if p.name != 'GeodesicRiverSystem.cs']
sources += [root / p for p in [
    'Assets/Scripts/Planet/Geodesic/Grid/GeodesicGridTopology.cs',
    'Assets/Scripts/Planet/Geodesic/Grid/GeodesicTransportGraph.cs',
    'Assets/Scripts/Planet/Geodesic/Rendering/IcosphereRenderGeometry.cs',
    'Assets/Scripts/Planet/Geodesic/Rendering/IcosphereRenderGeometryCache.cs',
    'Assets/Scripts/Planet/Geodesic/Rendering/IcosphereRenderMeshBuilder.cs',
    'Assets/Scripts/Planet/Geodesic/Rendering/IcosphereDirectionMapping.cs',
    'Assets/Scripts/Planet/Geodesic/Rendering/IcosphereDirectionMappingBuilder.cs',
    'Assets/Scripts/Planet/Geodesic/Rendering/GeodesicMaskedOceanGeometry.cs',
    'Assets/Scripts/Planet/Environment/Ocean/GeodesicOceanConnectivity.cs',
    'Assets/Scripts/Planet/Environment/Ocean/GeodesicOceanLayerGrid.cs',
    'Assets/Scripts/Planet/Environment/Ocean/GeodesicPhysicalScale.cs',
    'Assets/Scripts/Planet/Geodesic/Terrain/PlanetTerrainSampler.cs',
    'Assets/Scripts/Planet/Geodesic/Terrain/PlanetTerrainSettings.cs',
    'Assets/Scripts/Utilities/SimpleNoise.cs',
    'Assets/Tests/EditMode/GeodesicTinHydrologyTests.cs',
    'Assets/Tests/EditMode/GeodesicTinSurfaceTests.cs',
    'Assets/Tests/EditMode/GeodesicHydrologyTopologyTests.cs',
    'Assets/Tests/EditMode/GeodesicRiverGradeTests.cs',
    'Assets/Tests/EditMode/GeodesicDrainageTests.cs',
    'Assets/Tests/EditMode/GeodesicOceanConnectivityTests.cs',
    'Assets/Tests/EditMode/GeodesicLakeTests.cs',
    'Tools/TinHydrologyValidation.cs',
]]
refs = list((data / 'MonoBleedingEdge/lib/mono/unityjit-linux').glob('*.dll'))
refs += [data / 'MonoBleedingEdge/lib/mono/unityjit-linux/Facades/netstandard.dll']
refs += list(core.glob('*.dll')) + [nunit / 'nunit.framework.dll']
response = work / 'compile.rsp'
response.write_text('\n'.join(['/nologo', '/nostdlib+', '/langversion:latest', '/target:exe',
    '/out:"' + str(work / 'TinValidation.exe') + '"'] +
    ['/reference:"' + str(p) + '"' for p in refs] + ['"' + str(p) + '"' for p in sources]))
subprocess.run([str(dotnet), str(data / 'DotNetSdkRoslyn/csc.dll'), '@' + str(response)], check=True, cwd=root)
env = dict(os.environ)
env['MONO_PATH'] = os.pathsep.join([str(core), str(nunit), str(data / 'MonoBleedingEdge/lib/mono/unityjit-linux')])
command = [str(mono), str(work / 'TinValidation.exe')]
if args.benchmark is not None:
    command += ['--benchmark', str(args.benchmark)]
subprocess.run(command, check=True, cwd=root, env=env)
