[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option',
    [switch]$SkipRestore
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (!$SkipRestore) { & (Join-Path $root 'Tools\Build\Restore-Workspace.ps1') -GameDir $GameDir }
$workspace = Get-Content -LiteralPath (Join-Path $root 'artifacts\workspace.json') -Raw | ConvertFrom-Json
$lock = Get-Content -LiteralPath (Join-Path $root 'Build\dependencies.lock.json') -Raw | ConvertFrom-Json
$currentGameHash = (Get-FileHash -LiteralPath (Join-Path $GameDir 'NuclearOption_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($currentGameHash -ne $lock.gameAssemblySha256) {
    throw 'Installed game differs from the release target. Audit it and update Build/dependencies.lock.json and package gameVersion first.'
}
& $workspace.python (Join-Path $root 'Tools\Build\check_source_assets.py')
if ($LASTEXITCODE) { throw 'Source assets are incomplete or changed.' }
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'Plugin\F117Nighthawk.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$buildDir = Join-Path $root ('artifacts\compatibility-' + $version)
if (Test-Path -LiteralPath $buildDir) { throw "Build output already exists: $buildDir" }
[IO.Directory]::CreateDirectory($buildDir) | Out-Null
dotnet build (Join-Path $root 'Plugin\F117Nighthawk.csproj') -c Release -warnaserror -p:GameDir="$GameDir"
if ($LASTEXITCODE) { throw 'Plugin build failed.' }
$report = Join-Path $buildDir 'game-api.json'
$schema = Join-Path $buildDir 'game-schema.json'
& (Join-Path $root 'Tools\Audits\Test-GameCompatibility.ps1') `
    -PluginAssembly (Join-Path $root 'Plugin\bin\Release\F117Nighthawk.dll') `
    -SourceDirectory (Join-Path $root 'Plugin') -GameDir $GameDir `
    -BlueprinterAssembly $workspace.dependencies.blueprinter -OutputPath $report
& (Join-Path $root 'Tools\Audits\Export-GameSchema.ps1') -GameDir $GameDir -OutputPath $schema
$bundle = Join-Path $buildDir 'blacknight2u.f117a.nighthawk.nobp'
& $workspace.python (Join-Path $root 'Tools\Release\new_compatibility_bundle.py') `
    $workspace.dependencies.publishedBaseline $schema $report $bundle --version $version
if ($LASTEXITCODE) { throw 'Bundle compatibility verification failed.' }
& (Join-Path $root 'Tools\Release\New-F117Release.ps1') -BundlePath $bundle
