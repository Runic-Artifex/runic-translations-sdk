param(
    [Parameter(Mandatory = $true)]
    [string]$StagingDirectory,
    [Parameter(Mandatory = $true)]
    [ValidateSet("preview", "stable")]
    [string]$Channel,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [ValidateSet("linux-x64", "win-x64", "osx-arm64")]
    [string]$RuntimeIdentifier
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-Digest([string]$Path) { (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant() }

$root = (Resolve-Path $StagingDirectory).Path
$required = @("package-manifest.json", "release-manifest.json", "dependencies.json", "sbom.spdx.json", "provenance.json", "upstream-receipt.template.json", "SHA256SUMS")
foreach ($name in $required) { if (-not (Test-Path (Join-Path $root $name) -PathType Leaf)) { throw "Release staging omitted '$name'." } }
$actualFiles = @(Get-ChildItem -Path $root -File -Recurse | ForEach-Object { [System.IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') } | Sort-Object)
if (@(Compare-Object -ReferenceObject ($required | Sort-Object) -DifferenceObject $actualFiles).Count -ne 0) { throw "Release staging is not a closed regular-file set." }
if (@(Get-ChildItem -Path $root -Recurse -Force | Where-Object { $_.LinkType }).Count -ne 0) { throw "Release staging must not contain links." }

$release = Get-Content -Raw (Join-Path $root "release-manifest.json") | ConvertFrom-Json -AsHashtable
if ($release.schema -ne "runic.translations.editor-release/1" -or $release.channel -ne $Channel -or $release.version -ne $Version -or $release.runtimeIdentifier -ne $RuntimeIdentifier) {
    throw "The closed release manifest does not bind the requested release identity."
}
if (@($release.artifacts).Count -ne 1) { throw "Release staging must contain exactly one declared distribution artifact per platform." }
$artifact = $release.artifacts[0]
if ($artifact.identity -ne "Runic.Translations.Editor" -or $artifact.product -ne "editor" -or $artifact.type -ne "distribution" -or $artifact.version -ne $Version -or
    $artifact.id -ne "runic-translations-editor-$RuntimeIdentifier" -or $artifact.kind -ne "self-contained-desktop-archive" -or
    $artifact.path -notmatch '^[A-Za-z0-9][A-Za-z0-9._/@+\-]*$' -or $artifact.path.Contains('/') -or
    $artifact.path.Length -gt 240) { throw "The declared distribution artifact is invalid." }
$expectedMediaType = if ($artifact.path.EndsWith(".zip", [StringComparison]::Ordinal)) { "application/zip" } elseif ($artifact.path.EndsWith(".tar.gz", [StringComparison]::Ordinal)) { "application/gzip" } else { "" }
if ($artifact.mediaType -ne $expectedMediaType) { throw "The declared distribution artifact media type is invalid." }
$archive = Join-Path (Split-Path -Parent $root) $artifact.path
if (-not (Test-Path $archive -PathType Leaf) -or $artifact.sha256 -ne (Get-Digest $archive) -or $artifact.size -ne (Get-Item $archive).Length) {
    throw "The release artifact is not bound to the closed manifest."
}
$siblingChecksum = "$archive.sha256"
if (-not (Test-Path $siblingChecksum -PathType Leaf) -or (Get-Content -Raw $siblingChecksum).Trim() -ne "$($artifact.sha256)  $($artifact.path)") { throw "The release artifact sibling checksum is missing or does not bind its exact content." }

$sumEntries = @(Get-Content (Join-Path $root "SHA256SUMS") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$checksumNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
if ($sumEntries.Count -ne 7) { throw "Release staging checksums must bind exactly the six metadata files and archive." }
foreach ($entry in $sumEntries) {
    if ($entry -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9][A-Za-z0-9._/@+\-]*)$') { throw "Malformed release staging checksum entry '$entry'." }
    $expected = $Matches[1]; $name = $Matches[2]
    if (-not $checksumNames.Add($name)) { throw "Release staging checksum names must be unique." }
    $path = if ($name -eq $artifact.path) { $archive } else { Join-Path $root $name }
    if (-not (Test-Path $path -PathType Leaf) -or (Get-Digest $path) -ne $expected) { throw "Checksum verification failed for '$name'." }
}
if (@(Compare-Object -ReferenceObject (@($required | Where-Object { $_ -ne "SHA256SUMS" }) + @($artifact.path) | Sort-Object) -DifferenceObject @($checksumNames | Sort-Object)).Count -ne 0) { throw "Release staging checksum names do not close the artifact set." }

$package = Get-Content -Raw (Join-Path $root "package-manifest.json") | ConvertFrom-Json -AsHashtable
if ($package.schema -ne "runic.translations.editor-package/1" -or $package.updateChannel -ne $Channel -or $package.version -ne $Version -or $package.runtimeIdentifier -ne $RuntimeIdentifier) {
    throw "The copied package manifest does not bind the release identity."
}
if ($release.packageManifest.path -ne "package-manifest.json" -or $release.packageManifest.sha256 -ne (Get-Digest (Join-Path $root "package-manifest.json")) -or
    $release.packageManifest.fileCount -ne @($package.files).Count -or $release.packageManifest.totalBytes -ne [int64](@($package.files | Measure-Object -Property bytes -Sum).Sum)) { throw "Release package-manifest binding is incomplete." }
$provenance = Get-Content -Raw (Join-Path $root "provenance.json") | ConvertFrom-Json -AsHashtable
$receipt = Get-Content -Raw (Join-Path $root "upstream-receipt.template.json") | ConvertFrom-Json -AsHashtable
$releaseRevision = [string]$release.repositoryCommit
$releaseTree = [string]$release.repositoryTree
$packageRevision = [string]$package.repositoryCommit
$packageTree = [string]$package.repositoryTree
$provenanceRevision = [string]$provenance.source.revision
$provenanceTree = [string]$provenance.source.tree
$receiptRevision = [string]$receipt.source.revision
$receiptTree = [string]$receipt.source.tree
if ($receipt.schemaVersion -ne 1 -or $receipt.source.repository -ne "https://github.com/Runic-Artifex/runic-translations-editor" -or
    $receipt.artifact.path -ne $artifact.path -or $receipt.artifact.sha256 -ne $artifact.sha256 -or $receipt.artifact.size -ne $artifact.size -or
    $receipt.artifact.mediaType -ne $artifact.mediaType -or $receipt.artifact.version -ne $artifact.version -or $receipt.artifact.identity -ne $artifact.identity -or
    $receipt.artifact.product -ne $artifact.product -or $receipt.artifact.type -ne $artifact.type -or $receipt.artifact.id -ne $artifact.id -or $receipt.artifact.kind -ne $artifact.kind) {
    throw "The receipt template does not bind this release artifact and source repository."
}
if ($releaseRevision -ne $packageRevision -or $releaseTree -ne $packageTree -or $releaseRevision -ne $provenanceRevision -or $releaseTree -ne $provenanceTree) {
    throw "Release, package, and provenance source identities must be exactly cross-bound."
}
if ($releaseRevision -eq "local") {
    if ($releaseTree -ne "unavailable" -or $receiptRevision -ne "REPLACE_WITH_GIT_REVISION" -or $receiptTree -ne "REPLACE_WITH_GIT_TREE") { throw "Local staging must retain only the explicit receipt placeholders." }
} elseif ($receiptRevision -ne $releaseRevision -or $receiptTree -ne $releaseTree) {
    throw "The receipt template source identity must exactly bind the release revision and tree."
}
if ($Channel -eq "stable" -and ($releaseRevision -notmatch '^[a-f0-9]{40}$' -or $releaseTree -notmatch '^[a-f0-9]{40}$')) { throw "Stable staging provenance requires an exact revision and tree." }
$dependencies = Get-Content -Raw (Join-Path $root "dependencies.json") | ConvertFrom-Json -AsHashtable
if ($dependencies.schema -ne "runic.translations.editor-dependencies/1" -or @($dependencies.notices).Count -ne 2 -or @($dependencies.packages).Count -lt 2) {
    throw "Dependency and license inventory is incomplete."
}
$manifestFilesByPath = @{}
foreach ($file in $package.files) { $manifestFilesByPath[[string]$file.path] = [string]$file.sha256 }
foreach ($noticePath in @("LICENSE.txt", "THIRD-PARTY-NOTICES.md")) {
    if (@($dependencies.notices | Where-Object { $_.path -eq $noticePath -and $_.sha256 -eq $manifestFilesByPath[$noticePath] }).Count -ne 1) { throw "Dependency notice '$noticePath' is not exactly bound to the packaged file manifest." }
}
$dependencyEcosystems = @($dependencies.packages | ForEach-Object { [string]$_.ecosystem } | Sort-Object -Unique)
if (@(Compare-Object -ReferenceObject @("npm", "nuget") -DifferenceObject $dependencyEcosystems).Count -ne 0) { throw "Dependency inventory must contain nonempty npm and NuGet closures." }
foreach ($dependency in $dependencies.packages) {
    if ([string]::IsNullOrWhiteSpace([string]$dependency.name) -or [string]::IsNullOrWhiteSpace([string]$dependency.version) -or
        [string]::IsNullOrWhiteSpace([string]$dependency.integrity) -or [string]::IsNullOrWhiteSpace([string]$dependency.source) -or
        [string]::IsNullOrWhiteSpace([string]$dependency.license) -or [string]$dependency.metadataSha256 -notmatch '^[a-f0-9]{64}$') {
        throw "Dependency inventory contains an unbound component."
    }
}
$sbom = Get-Content -Raw (Join-Path $root "sbom.spdx.json") | ConvertFrom-Json -AsHashtable
if ($sbom.spdxVersion -ne "SPDX-2.3" -or $sbom.dataLicense -ne "CC0-1.0" -or @($sbom.packages).Count -ne (@($dependencies.packages).Count + 1) -or @($sbom.packages).Count -ne @($sbom.relationships).Count) { throw "SBOM is invalid." }
foreach ($dependency in $dependencies.packages) {
    if (@($sbom.packages | Where-Object { $_.name -eq "$($dependency.ecosystem):$($dependency.name)" -and $_.versionInfo -eq $dependency.version -and $_.downloadLocation -eq $dependency.source -and $_.licenseDeclared -eq $dependency.license -and $_.licenseConcluded -eq $dependency.license -and $_.checksums[0].checksumValue -eq $dependency.metadataSha256 }).Count -ne 1) { throw "SBOM does not close dependency '$($dependency.ecosystem):$($dependency.name)@$($dependency.version)'." }
}
if ($provenance.schema -ne "runic.translations.editor-provenance/1" -or $provenance.artifact.path -ne $artifact.path -or $provenance.artifact.sha256 -ne $artifact.sha256 -or
    $provenance.artifact.size -ne $artifact.size -or $provenance.artifact.mediaType -ne $artifact.mediaType -or $provenance.artifact.version -ne $artifact.version -or
    $provenance.artifact.identity -ne $artifact.identity -or $provenance.artifact.product -ne $artifact.product -or $provenance.artifact.type -ne $artifact.type -or $provenance.artifact.id -ne $artifact.id -or $provenance.artifact.kind -ne $artifact.kind) { throw "Provenance does not exactly bind the staged artifact." }
Write-Host "Verified closed $Channel release staging '$root'."
$global:LASTEXITCODE = 0
