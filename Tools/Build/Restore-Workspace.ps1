[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option',
    [string]$Python = 'python',
    [switch]$SkipLfs
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$lock = Get-Content -LiteralPath (Join-Path $root 'Build\dependencies.lock.json') -Raw | ConvertFrom-Json
$artifacts = Join-Path $root 'artifacts'
$dependencies = Join-Path $artifacts 'dependencies'
[IO.Directory]::CreateDirectory($dependencies) | Out-Null
if (!$SkipLfs) {
    git -C $root lfs pull
    if ($LASTEXITCODE) { throw 'git lfs pull failed; install Git LFS and authenticate to GitHub.' }
}
& $Python (Join-Path $root 'Tools\Build\check_source_assets.py')
if ($LASTEXITCODE) { throw 'Source-asset verification failed.' }
$gameAssembly = Join-Path $GameDir 'NuclearOption_Data\Managed\Assembly-CSharp.dll'
$cecil = Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll'
foreach ($required in @($gameAssembly, $cecil, (Join-Path $GameDir 'BepInEx\core\BepInEx.dll'))) {
    if (!(Test-Path -LiteralPath $required)) { throw "Local dependency missing: $required" }
}
$resolved = [ordered]@{}
foreach ($download in $lock.downloads) {
    if ([IO.Path]::GetFileName($download.fileName) -ne $download.fileName) { throw 'Invalid dependency filename.' }
    $path = Join-Path $dependencies $download.fileName
    if (!(Test-Path -LiteralPath $path)) {
        $pending = $path + '.download'
        Invoke-WebRequest -Uri $download.url -OutFile $pending
        if ((Get-FileHash -LiteralPath $pending -Algorithm SHA256).Hash.ToLowerInvariant() -ne $download.sha256) {
            throw "Dependency checksum failed: $($download.fileName)"
        }
        [IO.File]::Move($pending, $path)
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $download.sha256) {
        throw "Cached dependency differs from the lock file: $path"
    }
    $resolved[$download.id] = $path
}
Add-Type -Path $cecil
$donor = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($resolved.donor)
try {
    $resource = @($donor.MainModule.Resources | Where-Object Name -EQ $lock.donorBundle.resourceName)
    if ($resource.Count -ne 1) { throw 'The locked donor resource is missing or ambiguous.' }
    $bundlePath = Join-Path $dependencies $lock.donorBundle.fileName
    $bytes = $resource[0].GetResourceData()
    if (Test-Path -LiteralPath $bundlePath) {
        $sha = [Security.Cryptography.SHA256]::HashData($bytes)
        $expected = [Convert]::ToHexString($sha).ToLowerInvariant()
        if ((Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
            throw 'Existing donor bundle differs from its locked source resource.'
        }
    } else { [IO.File]::WriteAllBytes($bundlePath, $bytes) }
} finally { $donor.Dispose() }
$venv = Join-Path $artifacts 'python'
$venvPython = Join-Path $venv 'Scripts\python.exe'
if (!(Test-Path -LiteralPath $venvPython)) {
    & $Python -m venv $venv
    if ($LASTEXITCODE) { throw 'Python environment creation failed.' }
}
& $venvPython -m pip install --quiet --disable-pip-version-check -r (Join-Path $root 'Build\requirements.txt')
if ($LASTEXITCODE) { throw 'Python dependency installation failed.' }
& $venvPython (Join-Path $root 'Tools\Build\check_donor_assets.py') $bundlePath (Join-Path $root 'Build\dependencies.lock.json')
if ($LASTEXITCODE) { throw 'Donor asset verification failed.' }
$workspace = [ordered]@{
    root = $root
    blenderMaster = Join-Path $root 'F117_Production_Master.blend'
    unityAssets = Join-Path $root 'UnityAuthoring\Assets\F117'
    gameDir = [IO.Path]::GetFullPath($GameDir)
    gameAssemblySha256 = (Get-FileHash -LiteralPath $gameAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
    dependencies = $resolved
    donorBundle = $bundlePath
    python = $venvPython
    fullAuthoring = 'Source assets restored. For geometry changes, export the donor into editable Unity assets as described in DEVELOPMENT.md.'
}
$workspace | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifacts 'workspace.json') -Encoding utf8
Write-Output "Workspace restored: $(Join-Path $artifacts 'workspace.json')"
