param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath,
    [Parameter(Mandatory = $true)]
    [string]$PackageManifestPath,
    [Parameter(Mandatory = $true)]
    [ValidateSet("linux-x64", "win-x64", "osx-arm64")]
    [string]$RuntimeIdentifier,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [ValidateSet("preview", "stable")]
    [string]$Channel,
    [Parameter(Mandatory = $true)]
    [string]$RepositoryCommit,
    [string]$RepositoryTree = "",
    [string]$CreatedAt = "",
    [string]$LockFile = "",
    [string]$AssetsPath = "",
    [string]$FrontendRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$stagingRoot = Join-Path $ArtifactDirectory "release-staging"
$maxPathLength = 240
$maxFileCount = 10000
$maxFileBytes = 536870912
$maxTotalBytes = 2147483648

function Write-CanonicalJson([object]$Value, [string]$Path) {
    $Value | ConvertTo-Json -Depth 32 | Set-Content -Path $Path -Encoding utf8NoBOM
}

function Get-Digest([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

function Assert-SafeRelativePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Length -gt $maxPathLength -or
        [System.IO.Path]::IsPathRooted($Path) -or $Path.Contains("\") -or
        ($Path.Split('/') -contains '..') -or $Path.StartsWith('./', [StringComparison]::Ordinal) -or
        $Path -notmatch '^[A-Za-z0-9][A-Za-z0-9._/@+\-]*$') {
        throw "Release staging path '$Path' is not a bounded relative artifact path."
    }
}

function Get-SourceDate() {
    if (-not [string]::IsNullOrWhiteSpace($CreatedAt)) {
        return [DateTimeOffset]::Parse($CreatedAt, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime().ToString("O")
    }
    if ($RepositoryCommit -match '^[a-f0-9]{40}$') {
        $gitDate = (& git -C $repositoryRoot show -s --format=%cI $RepositoryCommit 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitDate)) {
            return [DateTimeOffset]::Parse($gitDate, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime().ToString("O")
        }
    }
    return "1970-01-01T00:00:00.0000000+00:00"
}

function Get-NodeDependencies() {
    $lockPath = if ($LockFile) { $LockFile } else { Join-Path $repositoryRoot "Frontend/package-lock.json" }
    $frontendRoot = if ($FrontendRoot) { $FrontendRoot } else { Join-Path $repositoryRoot "Frontend" }
    if (-not (Test-Path $lockPath -PathType Leaf)) { throw "The npm lockfile '$lockPath' is missing." }
    if (-not (Test-Path $frontendRoot -PathType Container)) { throw "The frontend artifact root '$frontendRoot' is missing." }
    $lock = Get-Content -Raw -Path $lockPath | ConvertFrom-Json -AsHashtable
    $result = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $lock.packages.GetEnumerator() | Sort-Object Key) {
        if ($entry.Key -notlike "node_modules/*") { continue }
        $name = $entry.Key.Substring("node_modules/".Length)
        $resolved = if ($entry.Value.ContainsKey("resolved")) { [string]$entry.Value.resolved } else { "" }
        $integrity = if ($entry.Value.ContainsKey("integrity")) { [string]$entry.Value.integrity } else { "" }
        if (($resolved.Length -eq 0 -or $integrity.Length -eq 0) -and $entry.Value.ContainsKey("inBundle") -and $entry.Value.inBundle) {
            $parentPath = $entry.Key.Substring(0, $entry.Key.LastIndexOf("/node_modules/"))
            $parent = $lock.packages[$parentPath]
            if ($null -eq $parent -or -not $parent.ContainsKey("integrity")) { throw "Bundled npm dependency '$name' lacks an integrity-bound parent." }
            $parentResolved = if ($parent.ContainsKey("resolved")) { [string]$parent.resolved } else {
                $parentName = $parentPath.Substring($parentPath.LastIndexOf("node_modules/") + "node_modules/".Length)
                $parentLeaf = $parentName.Split('/')[-1]
                "https://registry.npmjs.org/$parentName/-/$parentLeaf-$($parent.version).tgz"
            }
            $resolved = "bundled:" + $parentResolved
            $integrity = "bundled:" + [string]$parent.integrity
        }
        if ($integrity.Length -eq 0) { throw "Npm dependency '$name' lacks locked integrity metadata." }
        $license = if ($entry.Value.ContainsKey("license")) { [string]$entry.Value.license } else { "" }
        if ($resolved.Length -eq 0) {
            $packageLeaf = $name.Split('/')[-1]
            $resolved = "https://registry.npmjs.org/$name/-/$packageLeaf-$($entry.Value.version).tgz"
        }
        $packageJson = Join-Path $frontendRoot ($entry.Key + "/package.json")
        $installedMetadata = $null
        if ($license.Length -eq 0) {
            if (-not (Test-Path $packageJson -PathType Leaf)) { throw "Npm dependency '$name' lacks a lockfile license or installed package metadata." }
            $installedMetadata = Get-Content -Raw -Path $packageJson | ConvertFrom-Json -AsHashtable
            if ([string]$installedMetadata.name -ne $name -or [string]$installedMetadata.version -ne [string]$entry.Value.version) { throw "Installed npm metadata does not bind '$name@$($entry.Value.version)'." }
            if ($installedMetadata.ContainsKey("license")) { $license = [string]$installedMetadata.license }
        }
        if ($license.Length -eq 0) { $license = "NOASSERTION" }
        $installedMetadataSha256 = if ($null -eq $installedMetadata) { "" } else { Get-Digest $packageJson }
        $metadataSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("$name`n$($entry.Value.version)`n$resolved`n$integrity`n$license"))).ToLowerInvariant()
        $result.Add([ordered]@{
            name = $name
            version = [string]$entry.Value.version
            integrity = $integrity
            source = $resolved
            license = $license
            metadataSha256 = $metadataSha256
            installedMetadataSha256 = $installedMetadataSha256
            ecosystem = "npm"
        })
    }
    return @($result)
}

function Get-DotnetDependencies() {
    $assetsPath = if ($AssetsPath) { $AssetsPath } else { Join-Path $repositoryRoot "obj/project.assets.json" }
    if (-not (Test-Path $assetsPath -PathType Leaf)) { throw "The restored .NET dependency graph is missing '$assetsPath'." }
    $assets = Get-Content -Raw -Path $assetsPath | ConvertFrom-Json -AsHashtable
    $packageFolders = @($assets.packageFolders.Keys)
    if ($packageFolders.Count -eq 0) { throw "The restored .NET dependency graph declares no package folders." }
    $result = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $assets.libraries.GetEnumerator() | Sort-Object Key) {
        if ($entry.Value.type -ne "package") { continue }
        $parts = $entry.Key.Split('/', 2)
        if ($parts.Count -ne 2) { throw "The restored package identity '$($entry.Key)' is malformed." }
        $packageDirectory = $packageFolders | ForEach-Object { Join-Path $_ ($parts[0].ToLowerInvariant() + "/" + $parts[1].ToLowerInvariant()) } | Where-Object { Test-Path $_ -PathType Container } | Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($packageDirectory)) { throw "Resolved NuGet dependency '$($entry.Key)' is absent from the assets-declared package folders." }
        $nuspec = Get-ChildItem -Path $packageDirectory -Filter "*.nuspec" -File -ErrorAction SilentlyContinue | Select-Object -First 1
        $restoredMetadataPath = Join-Path $packageDirectory ".nupkg.metadata"
        if ($null -eq $nuspec -or -not (Test-Path $restoredMetadataPath -PathType Leaf) -or [string]::IsNullOrWhiteSpace([string]$entry.Value.sha512)) { throw "NuGet dependency '$($entry.Key)' lacks restored metadata or integrity." }
        $restoredMetadata = Get-Content -Raw -Path $restoredMetadataPath | ConvertFrom-Json -AsHashtable
        if ([string]::IsNullOrWhiteSpace([string]$restoredMetadata.source) -or [string]::IsNullOrWhiteSpace([string]$restoredMetadata.contentHash) -or [string]$restoredMetadata.contentHash -ne [string]$entry.Value.sha512) { throw "NuGet dependency '$($entry.Key)' does not bind its restored source and content hash." }
        [xml]$metadata = Get-Content -Raw -Path $nuspec.FullName
        $license = [string]$metadata.package.metadata.license
        if ([string]::IsNullOrWhiteSpace($license)) { $license = [string]$metadata.package.metadata.licenseUrl }
        if ([string]::IsNullOrWhiteSpace($license)) { throw "NuGet dependency '$($entry.Key)' lacks a license declaration." }
        $metadataSha256 = Get-Digest $nuspec.FullName
        $result.Add([ordered]@{
            name = $parts[0]
            version = $parts[1]
            integrity = [string]$entry.Value.sha512
            source = [string]$restoredMetadata.source
            license = $license
            metadataSha256 = $metadataSha256
            ecosystem = "nuget"
        })
    }
    return @($result)
}

if (-not (Test-Path $ArchivePath -PathType Leaf) -or -not (Test-Path $PackageManifestPath -PathType Leaf)) {
    throw "Release staging requires the finished archive and its verified package manifest."
}
if ($RepositoryCommit -notmatch '^(local|[a-f0-9]{40})$') {
    throw "RepositoryCommit must be 'local' or a lowercase 40-character Git revision."
}
if (-not [string]::IsNullOrWhiteSpace($RepositoryTree) -and $RepositoryTree -notmatch '^[a-f0-9]{40}$') {
    throw "RepositoryTree must be a lowercase 40-character Git tree when supplied."
}
if ($Channel -eq "stable" -and ($RepositoryCommit -notmatch '^[a-f0-9]{40}$' -or $RepositoryTree -notmatch '^[a-f0-9]{40}$')) { throw "Stable staging requires an exact source revision and tree." }

if (Test-Path $stagingRoot) { Remove-Item -Recurse -Force $stagingRoot }
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$archive = Get-Item $ArchivePath
$archiveName = $archive.Name
Assert-SafeRelativePath $archiveName
$mediaType = if ($archiveName.EndsWith(".zip", [StringComparison]::Ordinal)) { "application/zip" } else { "application/gzip" }
$artifact = [ordered]@{
    path = $archiveName
    sha256 = Get-Digest $archive.FullName
    size = $archive.Length
    mediaType = $mediaType
    identity = "Runic.Translations.Editor"
    product = "editor"
    version = $Version
    type = "distribution"
    id = "runic-translations-editor-$RuntimeIdentifier"
    kind = "self-contained-desktop-archive"
}

$expectedRepositoryTree = if ($RepositoryTree) { $RepositoryTree } else { "unavailable" }
$packageManifest = Get-Content -Raw -Path $PackageManifestPath | ConvertFrom-Json -AsHashtable
if ($packageManifest.schema -ne "runic.translations.editor-package/1" -or $packageManifest.version -ne $Version -or
    $packageManifest.updateChannel -ne $Channel -or $packageManifest.runtimeIdentifier -ne $RuntimeIdentifier -or
    $packageManifest.repositoryCommit -ne $RepositoryCommit -or $packageManifest.repositoryTree -ne $expectedRepositoryTree) {
    throw "The package manifest does not bind this version, channel, and runtime."
}
$packageFiles = @($packageManifest.files)
if ($packageFiles.Count -eq 0 -or $packageFiles.Count -gt $maxFileCount) { throw "The package manifest file count is outside the release bound." }
$totalBytes = [int64]0
foreach ($file in $packageFiles) {
    Assert-SafeRelativePath ([string]$file.path)
    if ($file.bytes -lt 0 -or $file.bytes -gt $maxFileBytes) { throw "Package file '$($file.path)' exceeds the per-file release bound." }
    $totalBytes += [int64]$file.bytes
}
if ($totalBytes -gt $maxTotalBytes) { throw "The package content exceeds the aggregate release bound." }

Copy-Item $PackageManifestPath (Join-Path $stagingRoot "package-manifest.json")
$releaseManifest = [ordered]@{
    schema = "runic.translations.editor-release/1"
    channel = $Channel
    version = $Version
    repositoryCommit = $RepositoryCommit
    repositoryTree = $expectedRepositoryTree
    runtimeIdentifier = $RuntimeIdentifier
    artifacts = @($artifact)
    packageManifest = [ordered]@{
        path = "package-manifest.json"
        sha256 = Get-Digest (Join-Path $stagingRoot "package-manifest.json")
        fileCount = $packageFiles.Count
        totalBytes = $totalBytes
    }
}
Write-CanonicalJson $releaseManifest (Join-Path $stagingRoot "release-manifest.json")

$dotnetDependencies = @(Get-DotnetDependencies)
$nodeDependencies = @(Get-NodeDependencies)
if ($dotnetDependencies.Count -eq 0 -or $nodeDependencies.Count -eq 0) { throw "Release staging requires nonempty restored NuGet and npm dependency closures." }
$dependencies = [ordered]@{
    schema = "runic.translations.editor-dependencies/1"
    notices = @(
        [ordered]@{ path = "LICENSE.txt"; sha256 = Get-Digest (Join-Path $repositoryRoot "LICENSE") },
        [ordered]@{ path = "THIRD-PARTY-NOTICES.md"; sha256 = Get-Digest (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md") }
    )
    packages = @($dotnetDependencies + $nodeDependencies | Sort-Object ecosystem, name, version)
}
Write-CanonicalJson $dependencies (Join-Path $stagingRoot "dependencies.json")

$spdxPackages = [System.Collections.Generic.List[object]]::new()
$spdxPackages.Add([ordered]@{
    SPDXID = "SPDXRef-RunicTranslationsEditor"
    checksums = @([ordered]@{ algorithm = "SHA256"; checksumValue = $artifact.sha256 })
    copyrightText = "Copyright (c) 2026 Viktor Jannicke"
    downloadLocation = "NOASSERTION"
    filesAnalyzed = $false
    licenseConcluded = "MIT"
    licenseDeclared = "MIT"
    name = "Runic.Translations.Editor"
    versionInfo = $Version
})
$componentIndex = 0
foreach ($dependency in $dependencies.packages) {
    if ($dependency.metadataSha256 -notmatch '^[a-f0-9]{64}$') { throw "Dependency '$($dependency.name)' has no traceable metadata digest." }
    $componentIndex++
    $spdxPackages.Add([ordered]@{
        SPDXID = "SPDXRef-Dependency-$componentIndex"
        checksums = @([ordered]@{ algorithm = "SHA256"; checksumValue = $dependency.metadataSha256 })
        copyrightText = "NOASSERTION"
        downloadLocation = $dependency.source
        filesAnalyzed = $false
        licenseConcluded = $dependency.license
        licenseDeclared = $dependency.license
        name = "$($dependency.ecosystem):$($dependency.name)"
        versionInfo = $dependency.version
    })
}
$sourceDate = Get-SourceDate
$sbom = [ordered]@{
    SPDXID = "SPDXRef-DOCUMENT"
    spdxVersion = "SPDX-2.3"
    dataLicense = "CC0-1.0"
    name = "Runic.Translations.Editor-$Version-$RuntimeIdentifier"
    documentNamespace = "https://runic.artifex/release/editor/$Version/$RuntimeIdentifier/$($artifact.sha256)"
    creationInfo = [ordered]@{ created = $sourceDate; creators = @("Tool: Runic Translations Editor release staging") }
    documentDescribes = @($spdxPackages | ForEach-Object { $_.SPDXID })
    packages = @($spdxPackages)
    relationships = @($spdxPackages | ForEach-Object { [ordered]@{ spdxElementId = "SPDXRef-DOCUMENT"; relationshipType = "DESCRIBES"; relatedSpdxElement = $_.SPDXID } })
}
Write-CanonicalJson $sbom (Join-Path $stagingRoot "sbom.spdx.json")

$provenance = [ordered]@{
    schema = "runic.translations.editor-provenance/1"
    source = [ordered]@{
        repository = "https://github.com/Runic-Artifex/runic-translations-editor"
        revision = $RepositoryCommit
        tree = $expectedRepositoryTree
    }
    artifact = $artifact
    build = [ordered]@{
        created = $sourceDate
        command = @("pwsh", "-File", "eng/package-editor.ps1", "-RuntimeIdentifier", $RuntimeIdentifier, "-OutputDirectory", "<output>", "-Version", $Version, "-RepositoryCommit", $RepositoryCommit, "-ReleaseChannel", $Channel)
        selfContained = $true
        reproducibility = "package manifest compared across two isolated publishes"
    }
}
Write-CanonicalJson $provenance (Join-Path $stagingRoot "provenance.json")

$receiptTemplate = [ordered]@{
    schemaVersion = 1
    artifact = $artifact
    attestationBundle = [ordered]@{ path = "REPLACE_WITH_GITHUB_ATTESTATION_BUNDLE"; sha256 = "REPLACE_WITH_64_LOWERCASE_HEX" }
    source = [ordered]@{
        repository = "https://github.com/Runic-Artifex/runic-translations-editor"
        revision = if ($RepositoryCommit -eq "local") { "REPLACE_WITH_GIT_REVISION" } else { $RepositoryCommit }
        tree = if ($RepositoryCommit -eq "local") { "REPLACE_WITH_GIT_TREE" } else { $expectedRepositoryTree }
    }
    builder = [ordered]@{ id = "REPLACE_WITH_GITHUB_BUILDER_ID" }
    invocation = [ordered]@{ id = "REPLACE_WITH_GITHUB_INVOCATION_ID" }
    materials = @([ordered]@{ uri = "REPLACE_WITH_MATERIAL_URI"; sha256 = "REPLACE_WITH_64_LOWERCASE_HEX" })
}
Write-CanonicalJson $receiptTemplate (Join-Path $stagingRoot "upstream-receipt.template.json")

$checksums = @(
    $artifact,
    [ordered]@{ path = "package-manifest.json"; sha256 = Get-Digest (Join-Path $stagingRoot "package-manifest.json") },
    [ordered]@{ path = "release-manifest.json"; sha256 = Get-Digest (Join-Path $stagingRoot "release-manifest.json") },
    [ordered]@{ path = "dependencies.json"; sha256 = Get-Digest (Join-Path $stagingRoot "dependencies.json") },
    [ordered]@{ path = "sbom.spdx.json"; sha256 = Get-Digest (Join-Path $stagingRoot "sbom.spdx.json") },
    [ordered]@{ path = "provenance.json"; sha256 = Get-Digest (Join-Path $stagingRoot "provenance.json") },
    [ordered]@{ path = "upstream-receipt.template.json"; sha256 = Get-Digest (Join-Path $stagingRoot "upstream-receipt.template.json") }
)
foreach ($item in $checksums) { Assert-SafeRelativePath ([string]$item.path) }
($checksums | Sort-Object path | ForEach-Object { "$($_.sha256)  $($_.path)" }) | Set-Content -Path (Join-Path $stagingRoot "SHA256SUMS") -Encoding ascii

Write-Host "Wrote closed unsigned release staging: $stagingRoot"
$global:LASTEXITCODE = 0
