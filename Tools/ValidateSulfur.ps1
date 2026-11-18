#requires -Version 7.0
param(
    [switch]$CompileManaged,
    [string]$NativeProject,
    [switch]$World,
    [switch]$Regressions,
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ($CompileManaged) { & (Join-Path $PSScriptRoot 'ValidateFoodTaxis.ps1') -CompileManaged }
if (!$NativeProject) { return }
# Reuse an isolated project restored with this project's packages. Never use the live checkout.
$resolved = [IO.Path]::GetFullPath($NativeProject).TrimEnd('\','/')
$validationRoot = [IO.Path]::GetFullPath((Join-Path $repo 'Temp/FoodTaxisAudit')) + [IO.Path]::DirectorySeparatorChar
if (!$resolved.StartsWith($validationRoot, [StringComparison]::OrdinalIgnoreCase) -or
    !(Split-Path -Leaf $resolved).StartsWith('UnityValidation-') -or
    !(Test-Path -LiteralPath (Join-Path $resolved 'ProjectSettings/ProjectVersion.txt'))) {
    throw 'NativeProject must be a disposable Temp/FoodTaxisAudit/UnityValidation-* Unity project.'
}
$output = Join-Path $repo 'Temp/SulfurAudit'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& robocopy (Join-Path $repo 'Assets') (Join-Path $resolved 'Assets') /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw 'Could not synchronize validation Assets.' }
# The ordinary setup UI saves its draft on application quit, including batch mode.
# Disable that writer in the disposable scene before the Editor can instantiate it.
$scene = Join-Path $resolved 'Assets/PlanetScene.unity'
$sceneText = [IO.File]::ReadAllText($scene)
[IO.File]::WriteAllText($scene, $sceneText.Replace('saveStartupConfigOnStart: 1', 'saveStartupConfigOnStart: 0'))
# Some failing regression fixtures leave freshly constructed startup controllers alive.
# Guard the file writer as well, only in this disposable copy, so those cannot save drafts.
$startupSource = Join-Path $resolved 'Assets/Scripts/Core/Startup/SimulationStartupController.cs'
$startupText = [IO.File]::ReadAllText($startupSource)
$writerPattern = 'private void SaveStartupConfig\(SimulationStartupConfig config\)\s*\{'
if ([regex]::Matches($startupText, $writerPattern).Count -ne 1) { throw 'Could not isolate the validation startup writer.' }
$startupText = [regex]::Replace($startupText, $writerPattern, '$0' + [Environment]::NewLine + '        if (Application.isBatchMode) return; // Disposable validation copy only.')
[IO.File]::WriteAllText($startupSource, $startupText)
$label = if ($World) { 'world' } elseif ($Regressions) { 'regressions' } else { 'sulfur' }
$filter = if ($World) { '^GeodesicSulfur' }
    elseif ($Regressions) {
        $fixtures = Get-ChildItem (Join-Path $repo 'Assets/Tests/EditMode') -Filter '*.cs' |
            Where-Object { $_.BaseName -notmatch 'WorldExperiments|FoodAlternatives' } |
            ForEach-Object { [regex]::Escape($_.BaseName) }
        '^(' + ($fixtures -join '|') + ')\.'
    }
    else { '^GeodesicSulfur(Runtime|Ecology)Tests' }
$result = Join-Path $output "$label-results.xml"
$log = Join-Path $output "$label.log"
$arguments = @('-batchmode','-nographics','-projectPath',('"'+$resolved+'"'),
    '-runTests','-testPlatform','EditMode','-assemblyNames','SimulaVit.EditModeTests',
    '-testFilter',('"'+$filter+'"'),'-testResults',('"'+$result+'"'),'-logFile',('"'+$log+'"'))
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $output "$label-process-id.txt")
Write-Output "Started native $label run, process $($process.Id). Results: $result. This is not yet a completed test result."
