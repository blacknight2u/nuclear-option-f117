[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BundlePath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$packageRoot = Join-Path $repoRoot 'Package\blacknight2u.f117a.nighthawk'
$dllPath = Join-Path $repoRoot 'Plugin\bin\Release\F117Nighthawk.dll'
$bundleFile = (Resolve-Path -LiteralPath $BundlePath).Path
$receipt = Get-Content -LiteralPath ($bundleFile + '.validation.json') -Raw | ConvertFrom-Json
$metadata = Get-Content -LiteralPath (Join-Path $packageRoot 'meta.json') -Raw | ConvertFrom-Json
$version = $metadata.artifact.version
$dllVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath).ProductVersion.Split('+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $dllVersion -ne $version -or $receipt.version -ne $version) {
    throw 'Package, DLL and validated bundle versions must match.'
}
$bundleHash = (Get-FileHash -LiteralPath $bundleFile -Algorithm SHA256).Hash.ToLowerInvariant()
if ($receipt.result -ne 'PASS' -or $receipt.sha256 -ne $bundleHash) {
    throw 'Bundle does not match its successful build-validation receipt. Rebuild it.'
}
$dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToLowerInvariant()
$metadata.artifact.hash = 'sha256:' + $dllHash
$readme = Get-Content -LiteralPath (Join-Path $packageRoot 'README.md') -Raw
if ($readme -notmatch 'Ricardo3D' -or $readme -notmatch 'creativecommons.org/licenses/by/4.0') {
    throw 'Package README is missing the model attribution.'
}
$outputDir = Join-Path $repoRoot 'artifacts\releases'
$output = Join-Path $outputDir ('F-117A-Nighthawk-v' + $version + '.zip')
if (Test-Path -LiteralPath $output) {
    throw "Refusing to replace an existing release: $output"
}
[IO.Directory]::CreateDirectory($outputDir) | Out-Null
Add-Type -AssemblyName System.IO.Compression
$pending = $output + '.partial'
$stream = [IO.File]::Open($pending, [IO.FileMode]::CreateNew)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($source in @(
            @{ Name = 'F117Nighthawk.dll'; Path = $dllPath },
            @{ Name = 'blacknight2u.f117a.nighthawk.nobp'; Path = $bundleFile }
        )) {
            $entry = $zip.CreateEntry($source.Name, [IO.Compression.CompressionLevel]::Optimal)
            $entryStream = $entry.Open()
            $sourceStream = [IO.File]::OpenRead($source.Path)
            try { $sourceStream.CopyTo($entryStream) }
            finally { $sourceStream.Dispose(); $entryStream.Dispose() }
        }
        foreach ($document in @(
            @{ Name = 'meta.json'; Text = ($metadata | ConvertTo-Json -Depth 20) },
            @{ Name = 'README.md'; Text = $readme }
        )) {
            $writer = [IO.StreamWriter]::new($zip.CreateEntry($document.Name).Open(), [Text.UTF8Encoding]::new($false))
            try { $writer.Write($document.Text) } finally { $writer.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }

$archive = [IO.Compression.ZipArchive]::new([IO.File]::OpenRead($pending))
try {
    if ($archive.Entries.Count -ne 4) { throw 'Unexpected release archive contents.' }
    foreach ($expected in @(
        @{ Name = 'F117Nighthawk.dll'; Hash = $dllHash },
        @{ Name = 'blacknight2u.f117a.nighthawk.nobp'; Hash = $bundleHash }
    )) {
        $entryStream = $archive.GetEntry($expected.Name).Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($entryStream)).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose(); $entryStream.Dispose() }
        if ($actual -ne $expected.Hash) { throw ('Packaged bytes changed: ' + $expected.Name) }
    }
} finally { $archive.Dispose() }
[IO.File]::Move($pending, $output)

Write-Output ('Verified release: ' + $output)
Write-Output ('DLL SHA256: ' + $dllHash)
Write-Output ('Bundle SHA256: ' + $bundleHash)
Write-Output ('ZIP SHA256: ' + (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant())
