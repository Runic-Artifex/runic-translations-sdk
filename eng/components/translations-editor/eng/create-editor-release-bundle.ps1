param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactsDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [ValidateSet("preview", "stable")]
    [string]$Channel
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$setVerifier = Join-Path $repositoryRoot "eng/verify-editor-release-set.ps1"
$stagingVerifier = Join-Path $repositoryRoot "eng/verify-editor-release-staging.ps1"
& $setVerifier -ArtifactsDirectory $ArtifactsDirectory -Version $Version -Channel $Channel
if ($LASTEXITCODE -ne 0) { throw "The platform release set must verify before bundling." }

$root = (Resolve-Path $ArtifactsDirectory).Path
$output = Join-Path $OutputDirectory "distribution"
$bundle = Join-Path $output "Runic.Translations.Editor-$Version.zip"
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("runic-editor-release-bundle-" + [Guid]::NewGuid().ToString("N"))
$reproducibilityProbe = Join-Path ([System.IO.Path]::GetTempPath()) ("runic-editor-release-bundle-probe-" + [Guid]::NewGuid().ToString("N") + ".zip")

function Write-DeterministicZip([string]$SourceDirectory, [string]$DestinationPath) {
    $stream = [System.IO.File]::Open($DestinationPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            Get-ChildItem -Path $SourceDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
                $name = [System.IO.Path]::GetRelativePath($SourceDirectory, $_.FullName).Replace('\', '/')
                $entry = $archive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = [System.IO.File]::OpenRead($_.FullName)
                try { $destination = $entry.Open(); try { $input.CopyTo($destination) } finally { $destination.Dispose() } } finally { $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

try {
    New-Item -ItemType Directory -Force -Path $output, $work | Out-Null
    $stagingDirectories = @(Get-ChildItem -Path $root -Directory -Recurse | Where-Object Name -eq "release-staging")
    foreach ($staging in $stagingDirectories) {
        $release = Get-Content -Raw (Join-Path $staging.FullName "release-manifest.json") | ConvertFrom-Json -AsHashtable
        $rid = $release.runtimeIdentifier
        $sourceRoot = Split-Path -Parent $staging.FullName
        $targetRoot = Join-Path $work "platforms/$rid"
        New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
        $sourceArchive = Join-Path $sourceRoot $release.artifacts[0].path
        $sourceChecksum = "$sourceArchive.sha256"
        $expectedChecksum = "$($release.artifacts[0].sha256)  $($release.artifacts[0].path)"
        if (-not (Test-Path $sourceArchive -PathType Leaf) -or -not (Test-Path $sourceChecksum -PathType Leaf) -or
            (Get-FileHash -Algorithm SHA256 $sourceArchive).Hash.ToLowerInvariant() -ne $release.artifacts[0].sha256 -or
            (Get-Content -Raw $sourceChecksum).Trim() -ne $expectedChecksum) { throw "Platform source '$rid' changed after release-set verification." }
        $targetArchive = Join-Path $targetRoot $release.artifacts[0].path
        Copy-Item $sourceArchive $targetArchive
        if ((Get-FileHash -Algorithm SHA256 $targetArchive).Hash.ToLowerInvariant() -ne $release.artifacts[0].sha256) { throw "Copied platform archive '$rid' did not preserve its verified digest." }
        Set-Content -Path "$targetArchive.sha256" -Value $expectedChecksum -Encoding ascii
        Copy-Item $staging.FullName (Join-Path $targetRoot "release-staging") -Recurse
        & $stagingVerifier -StagingDirectory (Join-Path $targetRoot "release-staging") -Channel $Channel -Version $Version -RuntimeIdentifier $rid
        if ($LASTEXITCODE -ne 0) { throw "The copied platform snapshot '$rid' failed closed release verification." }
    }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Write-DeterministicZip $work $bundle
    Write-DeterministicZip $work $reproducibilityProbe
    $digest = (Get-FileHash -Algorithm SHA256 -Path $bundle).Hash.ToLowerInvariant()
    if ($digest -ne (Get-FileHash -Algorithm SHA256 -Path $reproducibilityProbe).Hash.ToLowerInvariant()) {
        throw "Central release-evidence bundle is not reproducible from the verified platform snapshots."
    }
    Set-Content -Path "$bundle.sha256" -Value "$digest  $([System.IO.Path]::GetFileName($bundle))" -Encoding ascii
    $firstStaging = $stagingDirectories | Select-Object -First 1
    $source = Get-Content -Raw (Join-Path $firstStaging.FullName "release-manifest.json") | ConvertFrom-Json -AsHashtable
    $evidenceRoot = Join-Path $OutputDirectory "release-evidence-input"
    $distributionRoot = Join-Path $evidenceRoot "distribution"
    $receiptRoot = Join-Path $evidenceRoot "upstream-receipts"
    New-Item -ItemType Directory -Force -Path $distributionRoot, $receiptRoot | Out-Null
    Copy-Item $bundle $distributionRoot
    $receipt = [ordered]@{
        schemaVersion = 1
        artifact = [ordered]@{
            path = "distribution/Runic.Translations.Editor-$Version.zip"; sha256 = $digest; size = (Get-Item $bundle).Length; mediaType = "application/zip"
            identity = "Runic.Translations.Editor"; product = "editor"; version = $Version; type = "distribution"; id = "translations-editor-archive"; kind = "application-archive"
        }
        attestationBundle = [ordered]@{ path = "REPLACE_WITH_GITHUB_ATTESTATION_BUNDLE"; sha256 = "REPLACE_WITH_64_LOWERCASE_HEX" }
        source = [ordered]@{ repository = "https://github.com/Runic-Artifex/runic-translations-editor"; revision = $source.repositoryCommit; tree = $source.repositoryTree }
        builder = [ordered]@{ id = "REPLACE_WITH_GITHUB_BUILDER_ID" }
        invocation = [ordered]@{ id = "REPLACE_WITH_GITHUB_INVOCATION_ID" }
        materials = @([ordered]@{ uri = "REPLACE_WITH_MATERIAL_URI"; sha256 = "REPLACE_WITH_64_LOWERCASE_HEX" })
    }
    $receipt | ConvertTo-Json -Depth 16 | Set-Content -Path (Join-Path $receiptRoot "Runic.Translations.Editor-$Version.receipt.template.json") -Encoding utf8NoBOM
    Write-Host "Created central release-evidence distribution bundle: $bundle"
}
finally {
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    if (Test-Path $reproducibilityProbe) { Remove-Item -Force $reproducibilityProbe }
}
