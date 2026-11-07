[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')
$schema = [ordered]@{}
function Add-Schema($types, [string]$assemblyName) {
    foreach ($type in $types) {
        $fields = [ordered]@{}
        foreach ($field in $type.Fields) { $fields[$field.Name] = $field.FieldType.FullName }
        $properties = [ordered]@{}
        foreach ($property in $type.Properties) { $properties[$property.Name] = $property.PropertyType.FullName }
        $schema[$type.FullName] = [ordered]@{
            assembly = $assemblyName
            baseType = $type.BaseType.FullName
            fields = $fields
            properties = $properties
        }
        Add-Schema $type.NestedTypes $assemblyName
    }
}
$managed = Join-Path $GameDir 'NuclearOption_Data\Managed'
foreach ($path in @(Get-ChildItem -LiteralPath $managed -File | Where-Object { $_.Name -eq 'Assembly-CSharp.dll' -or $_.Name -eq 'Mirage.dll' -or $_.Name -like 'UnityEngine*.dll' })) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path.FullName)
    try { Add-Schema $assembly.MainModule.Types $assembly.Name.Name }
    finally { $assembly.Dispose() }
}
$report = [ordered]@{
    gameAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    types = $schema
}
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "Exported $($schema.Count) game/network/Unity type definitions."
