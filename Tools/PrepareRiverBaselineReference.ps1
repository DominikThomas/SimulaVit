#requires -Version 7.0
# Extract the actual reference implementation, not a reconstruction of the algorithm.
# Only a namespace wrapper is added. No statement inside the historical files is changed.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$gitArgs = @('-c', "safe.directory=$($repo.Replace('\','/'))", '-C', $repo)
$commit = (git @gitArgs rev-parse '6f48085^{commit}').Trim()
if ($LASTEXITCODE) { throw 'The required 6f48085 reference commit is unavailable.' }
$destination = Join-Path $repo 'Assets/HydrologyBaselineReference/Generated'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$files = @('GeodesicRiverSystem', 'GeodesicRiverPath', 'GeodesicRiverTerrain',
    'GeodesicDrainageGraph', 'GeodesicLakeBasins', 'GeodesicLakeGeometry',
    'GeodesicLakeRiverRouting', 'GeodesicRiverGradeAudit')
$manifest = foreach ($name in $files) {
    $path = "Assets/Scripts/Planet/Geodesic/Hydrology/$name.cs"
    $source = (git @gitArgs show "${commit}:$path") -join "`n"
    if ($LASTEXITCODE) { throw "Cannot extract $path" }
    $blob = (git @gitArgs rev-parse "${commit}:$path").Trim()
    $wrapped = "// Generated from $commit; git blob $blob. Do not edit.`nnamespace HydrologyReference6f48085`n{`n$source`n}`n"
    [IO.File]::WriteAllText((Join-Path $destination "$name.cs"), $wrapped, [Text.UTF8Encoding]::new($false))
    [pscustomobject]@{Path=$path;GitBlob=$blob}
}
[pscustomobject]@{Commit=$commit;Transformation='Namespace wrapper only';Files=$manifest} |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $destination 'source-manifest.json')
"Prepared literal $commit reference at $destination"
