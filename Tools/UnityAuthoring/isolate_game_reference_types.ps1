param(
    [string]$GameReference = "UnityAuthoring\Assets\Plugins\GameReferences\BroomGameCoreXX.dll",
    [string]$CecilAssembly = "C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\BepInEx\core\Mono.Cecil.dll"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$referencePath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $GameReference))
$allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "UnityAuthoring\Assets\Plugins\GameReferences"))
if (-not $referencePath.StartsWith($allowedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to rewrite a game reference outside $allowedRoot"
}
if (-not (Test-Path -LiteralPath $referencePath)) {
    throw "Game reference is missing: $referencePath"
}
if (-not (Test-Path -LiteralPath $CecilAssembly)) {
    throw "Mono.Cecil is missing: $CecilAssembly"
}

Add-Type -Path $CecilAssembly
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
@(
    [System.IO.Path]::GetDirectoryName($referencePath),
    "C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\NuclearOption_Data\Managed",
    "C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\BepInEx\core",
    "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Data\Managed",
    "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Data\Managed\UnityEngine"
) | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
    $resolver.AddSearchDirectory($_)
}
$readerParameters = [Mono.Cecil.ReaderParameters]::new()
$readerParameters.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($referencePath, $readerParameters)
$renames = [ordered]@{
    "PlayerSettings" = "BroomGamePlayerSettings"
    "DebugUI" = "BroomGameDebugUI"
    "MessageManager" = "BroomGameMessageManager"
}
$changed = 0
$temporaryPath = "$referencePath.isolated.tmp"
try {
    foreach ($entry in $renames.GetEnumerator()) {
        $original = @($assembly.MainModule.Types | Where-Object {
            $_.Namespace -eq "" -and $_.Name -eq $entry.Key
        })
        $isolated = @($assembly.MainModule.Types | Where-Object {
            $_.Namespace -eq "" -and $_.Name -eq $entry.Value
        })
        if ($original.Count -eq 1 -and $isolated.Count -eq 0) {
            $original[0].Name = $entry.Value
            $changed++
        }
        elseif ($original.Count -eq 0 -and $isolated.Count -eq 1) {
            continue
        }
        else {
            throw "Unexpected $($entry.Key) isolation state: original=$($original.Count), isolated=$($isolated.Count)"
        }
    }
    if ($changed -gt 0) {
        $assembly.Write($temporaryPath)
    }
}
finally {
    $assembly.Dispose()
}
if ($changed -gt 0) {
    try {
        Copy-Item -LiteralPath $temporaryPath -Destination $referencePath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

Write-Output "GAME_REFERENCE_TYPE_ISOLATION=PASS,changed:$changed"
