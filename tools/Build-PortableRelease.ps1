[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Releases\Azur-portable.zip')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$azurRoot = Split-Path -Parent $PSScriptRoot
$azurRelease = Join-Path $azurRoot 'Outil_Azur_complet\bin\Release'
$azurResources = Join-Path $azurRelease 'ressources'
$azurReadme = Join-Path $azurRoot 'docs\PORTABLE.md'
$azurExe = Join-Path $azurRelease 'Outil_Azur_complet.exe'
if (!(Test-Path -LiteralPath $azurExe) -or !(Test-Path -LiteralPath $azurResources) -or !(Test-Path -LiteralPath $azurReadme)) {
    throw 'Compilez la solution en Release et vérifiez les ressources et docs/PORTABLE.md avant de créer le paquet.'
}

$azurOutput = [IO.Path]::GetFullPath($OutputPath)
$azurReleasePath = [IO.Path]::GetFullPath($azurRelease).TrimEnd('\') + '\'
if ($azurOutput.StartsWith($azurReleasePath, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Le paquet de distribution doit être créé hors du dossier Release.'
}
$azurOutputDirectory = Split-Path -Parent $azurOutput
[IO.Directory]::CreateDirectory($azurOutputDirectory) | Out-Null
if (Test-Path -LiteralPath $azurOutput) { throw "Le paquet existe déjà : $azurOutput" }

# Only compiled runtime files and bundled assets are selected. Local config.json,
# auth/world overrides, generated creations, logs and debug symbols stay private.
$azurRootFiles = Get-ChildItem -LiteralPath $azurRelease -File | Where-Object {
    $_.Extension -in @('.exe', '.dll') -or $_.Name -eq 'Outil_Azur_complet.exe.config'
}
$azurAssetFiles = Get-ChildItem -LiteralPath $azurResources -Recurse -File | Where-Object {
    -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -and
    $_.FullName -notmatch '[\\/]Bot[\\/](AccountSingle[\\/]|BotConfig\.json$)' -and
    $_.Extension -ne '.tmp'
}
$azurFiles = @($azurRootFiles) + @($azurAssetFiles)
$azurDocuments = @('ETAT_PROJET.md', 'GUIDE_EDITEURS.md', 'FORMAT_SWF_CARTES.md',
    'FORMAT_SWF_OBJETS.md', 'KAUTH_COMPATIBILITE.md', 'CAPTURE_RESEAU.md', 'BOT_STARLOCO.md', 'BRIEF_REDESIGN_UI_BOT.md', 'STARLOCO_SOURCES_ANALYSE.md', 'PORTABLE.md') | ForEach-Object {
    @{ Source = (Join-Path $azurRoot ('docs\' + $_)); Entry = ('Azur/docs/' + $_) }
}
$azurDocuments += @{ Source = (Join-Path $azurRoot 'tests\README.md'); Entry = 'Azur/tests/README.md' }
foreach ($azurDocument in $azurDocuments) {
    if (!(Test-Path -LiteralPath $azurDocument.Source)) { throw "Documentation manquante : $($azurDocument.Source)" }
}
if ($azurRootFiles.Count -lt 5 -or $azurAssetFiles.Count -lt 100) {
    throw 'La compilation Release ou sa bibliothèque de ressources semble incomplète.'
}
$azurHash = [Security.Cryptography.SHA256]::Create()
$azurManifest = New-Object Text.StringBuilder
$azurArchive = $null
try {
    $azurArchive = [IO.Compression.ZipFile]::Open($azurOutput, [IO.Compression.ZipArchiveMode]::Create)
    foreach ($azurFile in $azurFiles) {
        $azurFilePath = [IO.Path]::GetFullPath($azurFile.FullName)
        if (!$azurFilePath.StartsWith($azurReleasePath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Fichier hors de la compilation Release : $azurFilePath"
        }
        $azurRelative = $azurFilePath.Substring($azurReleasePath.Length).Replace('\', '/')
        $azurEntry = 'Azur/' + $azurRelative
        # Store already compressed images instead of recompressing the tile library.
        $azurCompression = [IO.Compression.CompressionLevel]::Optimal
        if ($azurFile.Extension.ToLowerInvariant() -in @('.png', '.jpg', '.jpeg')) {
            $azurCompression = [IO.Compression.CompressionLevel]::NoCompression
        }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($azurArchive, $azurFilePath, $azurEntry,
            $azurCompression) | Out-Null
        $azurStream = [IO.File]::OpenRead($azurFilePath)
        try { $azurDigest = [BitConverter]::ToString($azurHash.ComputeHash($azurStream)).Replace('-', '').ToLowerInvariant() }
        finally { $azurStream.Dispose() }
        [void]$azurManifest.Append($azurDigest).Append('  ').Append($azurEntry).Append("`n")
    }
    foreach ($azurDocument in $azurDocuments) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($azurArchive, $azurDocument.Source,
            $azurDocument.Entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $azurStream = [IO.File]::OpenRead($azurDocument.Source)
        try { $azurDigest = [BitConverter]::ToString($azurHash.ComputeHash($azurStream)).Replace('-', '').ToLowerInvariant() }
        finally { $azurStream.Dispose() }
        [void]$azurManifest.Append($azurDigest).Append('  ').Append($azurDocument.Entry).Append("`n")
    }
    $azurGuide = $azurArchive.CreateEntry('Azur/README-PORTABLE.md', [IO.Compression.CompressionLevel]::Optimal)
    $azurWriter = New-Object IO.StreamWriter($azurGuide.Open(), (New-Object Text.UTF8Encoding($false)))
    try { $azurWriter.Write([IO.File]::ReadAllText($azurReadme).Replace('(KAUTH_COMPATIBILITE.md)', '(docs/KAUTH_COMPATIBILITE.md)').Replace('(CAPTURE_RESEAU.md)', '(docs/CAPTURE_RESEAU.md)').Replace('(BOT_STARLOCO.md)', '(docs/BOT_STARLOCO.md)')) }
    finally { $azurWriter.Dispose() }
    $azurManifestEntry = $azurArchive.CreateEntry('Azur/SHA256SUMS.txt', [IO.Compression.CompressionLevel]::Optimal)
    $azurWriter = New-Object IO.StreamWriter($azurManifestEntry.Open(), (New-Object Text.UTF8Encoding($false)))
    try { $azurWriter.Write($azurManifest.ToString()) }
    finally { $azurWriter.Dispose() }
}
catch {
    if ($azurArchive) { $azurArchive.Dispose(); $azurArchive = $null }
    if (Test-Path -LiteralPath $azurOutput) { Remove-Item -LiteralPath $azurOutput -Force }
    throw
}
finally {
    if ($azurArchive) { $azurArchive.Dispose() }
    $azurHash.Dispose()
}

$azurZip = [IO.Compression.ZipFile]::OpenRead($azurOutput)
try {
    if ($azurZip.Entries.Count -ne $azurFiles.Count + $azurDocuments.Count + 2 -or
        !$azurZip.GetEntry('Azur/Outil_Azur_complet.exe') -or
        !$azurZip.GetEntry('Azur/SHA256SUMS.txt') -or
        !$azurZip.GetEntry('Azur/ressources/maps/sols/Herbe/4.png')) {
        throw 'Le paquet créé ne contient pas toutes ses entrées requises.'
    }
}
finally { $azurZip.Dispose() }
Write-Host "Paquet créé : $azurOutput ($($azurFiles.Count) fichiers compilés et ressources, $($azurDocuments.Count) documents ; $([IO.FileInfo]::new($azurOutput).Length) octets)."
