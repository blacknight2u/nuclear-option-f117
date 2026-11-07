[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PluginAssembly,
    [Parameter(Mandatory)][string]$SourceDirectory,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option',
    [string]$BlueprinterAssembly
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach ($dir in @((Join-Path $GameDir 'NuclearOption_Data\Managed'), (Join-Path $GameDir 'BepInEx\core'), (Split-Path -Parent $PluginAssembly))) {
    $resolver.AddSearchDirectory($dir)
}
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$gamePath = Join-Path $GameDir 'NuclearOption_Data\Managed\Assembly-CSharp.dll'
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath, $reader)
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginAssembly).Path, $reader)
$types = @{}
function Add-Types($items) {
    foreach ($type in $items) {
        $types[$type.FullName] = $type
        Add-Types $type.NestedTypes
    }
}
Add-Types $game.MainModule.Types
$blueprinter = $null
if ($BlueprinterAssembly) {
    $blueprinter = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $BlueprinterAssembly).Path, $reader)
    Add-Types $blueprinter.MainModule.Types
}
$failures = [Collections.Generic.List[string]]::new()
$patches = [Collections.Generic.List[object]]::new()
$fields = [Collections.Generic.List[object]]::new()
$references = [Collections.Generic.List[object]]::new()
function Find-Field([string]$typeName, [string]$fieldName) {
    $type = $types[$typeName]
    while ($null -ne $type) {
        $field = $type.Fields | Where-Object Name -EQ $fieldName | Select-Object -First 1
        if ($null -ne $field) { return $field }
        $type = if ($type.BaseType) { $types[$type.BaseType.FullName] } else { $null }
    }
}
function Test-Assignable([string]$expected, [string]$actual) {
    $expected = $expected.TrimEnd('&')
    $actual = $actual.TrimEnd('&')
    if ($expected -eq 'System.Object' -or $expected -eq $actual) { return $true }
    $type = $types[$actual]
    while ($type -and $type.BaseType) {
        if ($type.BaseType.FullName -eq $expected) { return $true }
        $type = $types[$type.BaseType.FullName]
    }
    return $false
}
try {
    foreach ($patch in $mod.MainModule.Types) {
        $attrs = @($patch.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })
        if (!$attrs.Count) { continue }
        $targetType = $null
        $targetName = $null
        foreach ($attr in $attrs) {
            foreach ($arg in $attr.ConstructorArguments) {
                if ($arg.Type.FullName -eq 'System.Type') { $targetType = $arg.Value.FullName }
                if ($arg.Type.FullName -eq 'System.String') { $targetName = [string]$arg.Value }
            }
        }
        if (!$targetName) {
            $selector = $patch.Methods | Where-Object { $_.Name -in @('TargetMethod', 'TargetMethods') } | Select-Object -First 1
            if ($selector -and $selector.HasBody) {
                foreach ($instruction in $selector.Body.Instructions) {
                    if ($instruction.OpCode.Name -eq 'ldtoken' -and $instruction.Operand -is [Mono.Cecil.TypeReference] -and !$targetType) { $targetType = $instruction.Operand.FullName }
                    if ($instruction.OpCode.Name -eq 'ldstr' -and !$targetName) { $targetName = [string]$instruction.Operand }
                }
                if ($targetName -match '^([^:]+):(.+)$') { $targetType = $Matches[1]; $targetName = $Matches[2] }
                if ($selector.Name -eq 'TargetMethods' -and $targetType -eq 'WeaponSelector') { $targetName = 'Initialize' }
            }
        }
        $targets = @($types[$targetType].Methods | Where-Object Name -EQ $targetName)
        if ($patch.Name -eq 'F117FixedJammerSelectorPatch') {
            $targets = @($targets | Where-Object { 'HardpointSet' -in $_.Parameters.ParameterType.FullName })
        }
        if (!$targets.Count) { $failures.Add("Missing Harmony target: $($patch.Name) => ${targetType}.${targetName}") }
        elseif ($targets.Count -gt 1 -and $patch.Name -ne 'F117FixedJammerSelectorPatch') { $failures.Add("Ambiguous Harmony target: ${targetType}.${targetName}") }
        foreach ($target in $targets) {
            foreach ($hook in @($patch.Methods | Where-Object { $_.Name -in @('Prefix', 'Postfix') })) {
                foreach ($parameter in $hook.Parameters) {
                    $name = $parameter.Name
                    $actual = $null
                    if ($name -eq '__instance') { $actual = $target.DeclaringType.FullName }
                    elseif ($name -eq '__result') { $actual = $target.ReturnType.FullName }
                    elseif ($name -match '^__(\d+)$') {
                        $index = [int]$Matches[1]
                        if ($index -lt $target.Parameters.Count) { $actual = $target.Parameters[$index].ParameterType.FullName }
                    }
                    elseif ($name -match '^___(.+)$') { $actual = (Find-Field $targetType $Matches[1]).FieldType.FullName }
                    elseif ($name -in @('__state', '__args', '__originalMethod', '__runOriginal')) { continue }
                    else { $actual = ($target.Parameters | Where-Object Name -EQ $name | Select-Object -First 1).ParameterType.FullName }
                    if (!$actual -or !(Test-Assignable $parameter.ParameterType.FullName $actual)) {
                        $failures.Add("Harmony argument mismatch: $($patch.Name).$($hook.Name) $name expected $($parameter.ParameterType.FullName), game supplies '$actual'")
                    }
                }
            }
        }
        $patches.Add([ordered]@{ patch = $patch.Name; target = "${targetType}.${targetName}"; signatures = @($targets | ForEach-Object FullName) })
    }
    $source = (Get-ChildItem -LiteralPath $SourceDirectory -Filter '*.cs' -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
    $fieldNames = @{}
    foreach ($match in [regex]::Matches($source, 'AccessTools\.Field\(typeof\(([^)]+)\),\s*"([^"]+)"\)')) {
        $fieldNames[$match.Groups[1].Value + '|' + $match.Groups[2].Value] = $true
    }
    if ($source -match 'GetNestedType\("Motor"') {
        $fieldNames['Missile/Motor|thrust'] = $true
        $fieldNames['Missile/Motor|topSpeed'] = $true
    }
    foreach ($key in ($fieldNames.Keys | Sort-Object)) {
        $typeName, $name = $key.Split('|')
        $field = Find-Field $typeName $name
        if (!$field) { $failures.Add("Missing reflected field: ${typeName}.${name}") }
        $fields.Add([ordered]@{ type = $typeName; field = $name; signature = $field.FullName })
    }
    # Resolve compiled game/network member references without loading or running game code.
    foreach ($reference in $mod.MainModule.GetMemberReferences()) {
        if ($reference.DeclaringType.Scope.Name -notin @('Assembly-CSharp', 'Mirage')) { continue }
        $resolved = $null
        try { $resolved = $reference.Resolve() } catch { }
        if (!$resolved) { $failures.Add("Unresolved compiled member: $($reference.FullName)") }
        $references.Add([ordered]@{ member = $reference.FullName; resolved = ($null -ne $resolved) })
    }
    $report = [ordered]@{
        result = $(if ($failures.Count) { 'FAIL' } else { 'PASS' })
        method = 'Static metadata, Harmony argument, reflected-field and compiled-reference checks; no flight or multiplayer test.'
        checkedAtUtc = [DateTime]::UtcNow.ToString('o')
        gameAssemblySha256 = (Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash.ToLowerInvariant()
        plugin = $mod.Name.FullName
        pluginSha256 = (Get-FileHash -LiteralPath $PluginAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
        blueprinterSha256 = $(if ($BlueprinterAssembly) { (Get-FileHash -LiteralPath $BlueprinterAssembly -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null })
        patches = @($patches.ToArray())
        reflectedFields = @($fields.ToArray())
        compiledReferences = @($references.ToArray())
        failures = @($failures.ToArray())
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))) | Out-Null
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    Write-Output "$($report.result): $($mod.Name.Name): $($patches.Count) patches, $($fields.Count) reflected fields, $($references.Count) compiled references"
    if ($failures.Count) { $failures | Write-Output; throw 'Game compatibility checks failed.' }
} finally {
    $mod.Dispose()
    $game.Dispose()
    if ($blueprinter) { $blueprinter.Dispose() }
    $resolver.Dispose()
}
