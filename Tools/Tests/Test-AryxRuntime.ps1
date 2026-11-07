[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IsolatedGameDir,
    [string]$AryxDll,
    [string]$ReleaseZip,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$game = (Resolve-Path -LiteralPath $IsolatedGameDir).Path
if ($game -match '[\\/]steamapps[\\/]common[\\/]' -or !(Test-Path -LiteralPath (Join-Path $game 'NuclearOption.exe'))) {
    throw 'Use an isolated game copy outside the installed Steam library.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a fresh output directory for each run.' }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$plugins = Join-Path $game 'BepInEx\plugins'
$aircraftDll = Join-Path $plugins 'blacknight2u.f117a.nighthawk\F117Nighthawk.dll'
$probe = Join-Path $plugins 'F117AryxRuntimeProbe.dll'
$aryx = Join-Path $plugins 'AryxWeaponsPack_1.1.1.dll'
if ((Test-Path -LiteralPath $probe) -or (Test-Path -LiteralPath $aryx)) {
    throw 'Remove prior test probe or test Aryx DLL from the isolated copy first.'
}
$backup = Join-Path $OutputDirectory 'F117Nighthawk.original.dll'
Copy-Item -LiteralPath $aircraftDll -Destination $backup
$bundlePath = Join-Path (Split-Path -Parent $aircraftDll) 'blacknight2u.f117a.nighthawk.nobp'
$bundleBackup = Join-Path $OutputDirectory 'F117.original.nobp'
Copy-Item -LiteralPath $bundlePath -Destination $bundleBackup
$baselineProbe = Join-Path $plugins 'CompatibilityProbe.dll'
$baselineBackup = Join-Path $OutputDirectory 'CompatibilityProbe.original.dll'
if (Test-Path -LiteralPath $baselineProbe) { Move-Item -LiteralPath $baselineProbe -Destination $baselineBackup }
try {
    Copy-Item -LiteralPath (Join-Path $root 'Plugin\bin\Release\F117Nighthawk.dll') -Destination $aircraftDll -Force
    if ($ReleaseZip) {
        $package = Join-Path $OutputDirectory 'package'
        Expand-Archive -LiteralPath $ReleaseZip -DestinationPath $package
        Copy-Item -LiteralPath (Join-Path $package 'F117Nighthawk.dll') -Destination $aircraftDll -Force
        Copy-Item -LiteralPath (Join-Path $package 'blacknight2u.f117a.nighthawk.nobp') -Destination $bundlePath -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AryxRuntimeProbe\bin\Release\F117AryxRuntimeProbe.dll') -Destination $probe
    if ($AryxDll) { Copy-Item -LiteralPath $AryxDll -Destination $aryx }
    $started = Get-Date
    $process = Start-Process -FilePath (Join-Path $game 'NuclearOption.exe') -WorkingDirectory $game -WindowStyle Hidden `
        -ArgumentList '-batchmode','-nographics','-logFile','f117-aryx-runtime.log' -PassThru
    if (!$process.WaitForExit(26000)) { $process.Kill(); throw 'Runtime probe timed out.' }
    $report = Join-Path $game 'f117-aryx-probe.txt'
    if (!(Test-Path -LiteralPath $report) -or (Get-Item -LiteralPath $report).LastWriteTime -lt $started) {
        throw 'No fresh runtime report was produced.'
    }
    Copy-Item -LiteralPath $report -Destination $OutputDirectory
    Copy-Item -LiteralPath (Join-Path $game 'f117-aryx-runtime.log') -Destination $OutputDirectory
    Copy-Item -LiteralPath (Join-Path $game 'BepInEx\LogOutput.log') -Destination $OutputDirectory
    $result = Get-Content -Raw -LiteralPath $report
    Write-Output $result
    if ($result -notmatch 'RESULT PASS') { throw 'Runtime probe failed.' }
} finally {
    Copy-Item -LiteralPath $backup -Destination $aircraftDll -Force
    Copy-Item -LiteralPath $bundleBackup -Destination $bundlePath -Force
    if (Test-Path -LiteralPath $probe) { Move-Item -LiteralPath $probe -Destination $OutputDirectory }
    if (Test-Path -LiteralPath $aryx) { Move-Item -LiteralPath $aryx -Destination $OutputDirectory }
    if (Test-Path -LiteralPath $baselineBackup) { Move-Item -LiteralPath $baselineBackup -Destination $baselineProbe }
}
