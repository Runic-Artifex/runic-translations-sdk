param(
    [string]$CandidateSetOutput = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$writer = Join-Path $repositoryRoot "eng/write-editor-release-staging.ps1"
$verifier = Join-Path $repositoryRoot "eng/verify-editor-release-staging.ps1"
$bundleWriter = Join-Path $repositoryRoot "eng/create-editor-release-bundle.ps1"
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("runic-editor-release-contract-" + [Guid]::NewGuid().ToString("N"))
$version = "1.0.0-preview.contract"
$commit = if ([string]::IsNullOrWhiteSpace($CandidateSetOutput)) { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } else { (& git -C $repositoryRoot rev-parse HEAD).Trim() }
$tree = if ([string]::IsNullOrWhiteSpace($CandidateSetOutput)) { "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } else { (& git -C $repositoryRoot rev-parse "HEAD^{tree}").Trim() }
$fixtureLock = Join-Path $root "package-lock.fixture.json"
$fixtureFrontend = Join-Path $repositoryRoot "Frontend"
$fixtureAssets = Join-Path $root "project.assets.fixture.json"

try {
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $lock = Get-Content -Raw -Path (Join-Path $repositoryRoot "Frontend/package-lock.json") | ConvertFrom-Json -AsHashtable
    $lock.packages["node_modules/svelte-toolbelt"].Remove("license")
    $lock | ConvertTo-Json -Depth 64 | Set-Content -Path $fixtureLock -Encoding utf8NoBOM
    $nugetRoot = Join-Path $root "nuget"
    $nugetPackage = Join-Path $nugetRoot "fixture.package/1.2.3"
    New-Item -ItemType Directory -Force -Path $nugetPackage | Out-Null
    '<package><metadata><id>Fixture.Package</id><version>1.2.3</version><license type="expression">MIT</license></metadata></package>' | Set-Content -Path (Join-Path $nugetPackage "Fixture.Package.nuspec") -Encoding utf8NoBOM
    [ordered]@{ version = 2; contentHash = "sha512-fixture"; source = "https://fixture.invalid/v3/index.json" } | ConvertTo-Json | Set-Content -Path (Join-Path $nugetPackage ".nupkg.metadata") -Encoding utf8NoBOM
    [ordered]@{ version = 3; libraries = [ordered]@{ "Fixture.Package/1.2.3" = [ordered]@{ type = "package"; sha512 = "sha512-fixture" } }; packageFolders = [ordered]@{ ($nugetRoot + [IO.Path]::DirectorySeparatorChar) = [ordered]@{} }; project = [ordered]@{ restore = [ordered]@{ sources = [ordered]@{ "https://fixture.invalid/v3/index.json" = [ordered]@{} } } } } | ConvertTo-Json -Depth 16 | Set-Content -Path $fixtureAssets -Encoding utf8NoBOM
    $archive = Join-Path $root "Runic.Translations.Editor-$version-linux-x64.tar.gz"
    [System.IO.File]::WriteAllBytes($archive, [byte[]](1, 2, 3, 4))
    $archiveDigest = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()
    Set-Content -Path "$archive.sha256" -Value "$archiveDigest  $([System.IO.Path]::GetFileName($archive))" -Encoding ascii
    $manifest = [ordered]@{
        schema = "runic.translations.editor-package/1"
        version = $version
        updateChannel = "preview"
        repositoryCommit = $commit
        repositoryTree = $tree
        runtimeIdentifier = "linux-x64"
        selfContained = $true
        files = @(
            [ordered]@{ path = "Runic.Translations.Editor"; bytes = 4; sha256 = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant() },
            [ordered]@{ path = "LICENSE.txt"; bytes = (Get-Item (Join-Path $repositoryRoot "LICENSE")).Length; sha256 = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $repositoryRoot "LICENSE")).Hash.ToLowerInvariant() },
            [ordered]@{ path = "THIRD-PARTY-NOTICES.md"; bytes = (Get-Item (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md")).Length; sha256 = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md")).Hash.ToLowerInvariant() }
        )
    }
    $packageManifest = Join-Path $root "package-manifest.json"
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $packageManifest -Encoding utf8NoBOM

    $emptyAssets = Join-Path $root "empty.assets.json"
    [ordered]@{ version = 3; libraries = [ordered]@{}; packageFolders = [ordered]@{ ($nugetRoot + [IO.Path]::DirectorySeparatorChar) = [ordered]@{} }; project = [ordered]@{ restore = [ordered]@{ sources = [ordered]@{ "https://fixture.invalid/v3/index.json" = [ordered]@{} } } } } | ConvertTo-Json -Depth 16 | Set-Content -Path $emptyAssets -Encoding utf8NoBOM
    $failedClosed = $false
    try { & $writer -ArtifactDirectory (Join-Path $root "empty-closure") -ArchivePath $archive -PackageManifestPath $packageManifest -RuntimeIdentifier linux-x64 -Version $version -Channel preview -RepositoryCommit $commit -RepositoryTree $tree -LockFile $fixtureLock -FrontendRoot $fixtureFrontend -AssetsPath $emptyAssets } catch { $failedClosed = $true }
    if (-not $failedClosed) { throw "The dependency closure writer accepted an empty assets graph." }

    & $writer -ArtifactDirectory $root -ArchivePath $archive -PackageManifestPath $packageManifest -RuntimeIdentifier linux-x64 -Version $version -Channel preview -RepositoryCommit $commit -RepositoryTree $tree -LockFile $fixtureLock -FrontendRoot $fixtureFrontend -AssetsPath $fixtureAssets
    if ($LASTEXITCODE -ne 0) { throw "Preview staging writer failed." }
    & $verifier -StagingDirectory (Join-Path $root "release-staging") -Channel preview -Version $version -RuntimeIdentifier linux-x64
    if ($LASTEXITCODE -ne 0) { throw "Preview staging verifier failed." }
    $previewDependencies = Get-Content -Raw (Join-Path $root "release-staging/dependencies.json") | ConvertFrom-Json -AsHashtable
    $unassertedDependency = @($previewDependencies.packages | Where-Object { $_.ecosystem -eq "npm" -and $_.name -eq "svelte-toolbelt" })
    if ($unassertedDependency.Count -ne 1 -or $unassertedDependency[0].license -ne "NOASSERTION") { throw "Undeclared npm licenses must be recorded as SPDX NOASSERTION." }
    $previewSbomPath = Join-Path $root "release-staging/sbom.spdx.json"
    $previewSbom = Get-Content -Raw $previewSbomPath | ConvertFrom-Json -AsHashtable
    $previewDependency = @($previewSbom.packages | Where-Object { $_.name -like "nuget:*" })[0]
    $previewDependency.licenseDeclared = "FORGED"; $previewDependency.licenseConcluded = "FORGED"
    $previewSbom | ConvertTo-Json -Depth 32 | Set-Content -Path $previewSbomPath -Encoding utf8NoBOM
    $sbomDigest = (Get-FileHash -Algorithm SHA256 -Path $previewSbomPath).Hash.ToLowerInvariant()
    $sumPath = Join-Path $root "release-staging/SHA256SUMS"
    ((Get-Content $sumPath) | ForEach-Object { if ($_ -match '  sbom\.spdx\.json$') { "$sbomDigest  sbom.spdx.json" } else { $_ } }) | Set-Content -Path $sumPath -Encoding ascii
    $failedClosed = $false
    try { & $verifier -StagingDirectory (Join-Path $root "release-staging") -Channel preview -Version $version -RuntimeIdentifier linux-x64 } catch { $failedClosed = $true }
    if (-not $failedClosed) { throw "Staging verifier accepted a checksum-recomputed SBOM license mismatch." }
    & $writer -ArtifactDirectory $root -ArchivePath $archive -PackageManifestPath $packageManifest -RuntimeIdentifier linux-x64 -Version $version -Channel preview -RepositoryCommit $commit -RepositoryTree $tree -LockFile $fixtureLock -FrontendRoot $fixtureFrontend -AssetsPath $fixtureAssets
    if ($LASTEXITCODE -ne 0) { throw "Preview staging recreation failed after the SBOM license regression." }
    $previewDependenciesPath = Join-Path $root "release-staging/dependencies.json"
    $previewDependencies = Get-Content -Raw $previewDependenciesPath | ConvertFrom-Json -AsHashtable
    $previewDependencies.notices[0].sha256 = [string]::new([char]'f', 64)
    $previewDependencies | ConvertTo-Json -Depth 32 | Set-Content -Path $previewDependenciesPath -Encoding utf8NoBOM
    $dependenciesDigest = (Get-FileHash -Algorithm SHA256 -Path $previewDependenciesPath).Hash.ToLowerInvariant()
    $sumPath = Join-Path $root "release-staging/SHA256SUMS"
    ((Get-Content $sumPath) | ForEach-Object { if ($_ -match '  dependencies\.json$') { "$dependenciesDigest  dependencies.json" } else { $_ } }) | Set-Content -Path $sumPath -Encoding ascii
    $failedClosed = $false
    try { & $verifier -StagingDirectory (Join-Path $root "release-staging") -Channel preview -Version $version -RuntimeIdentifier linux-x64 } catch { $failedClosed = $true }
    if (-not $failedClosed) { throw "Staging verifier accepted a checksum-recomputed dependency notice mismatch." }

    $stableRoot = Join-Path $root "stable"
    New-Item -ItemType Directory -Force -Path $stableRoot | Out-Null
    $stableArchive = Join-Path $stableRoot ([System.IO.Path]::GetFileName($archive))
    Copy-Item $archive $stableArchive
    Set-Content -Path "$stableArchive.sha256" -Value "$archiveDigest  $([System.IO.Path]::GetFileName($stableArchive))" -Encoding ascii
    $manifest.updateChannel = "stable"
    $stablePackageManifest = Join-Path $stableRoot "package-manifest.json"
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $stablePackageManifest -Encoding utf8NoBOM
    & $writer -ArtifactDirectory $stableRoot -ArchivePath $stableArchive -PackageManifestPath $stablePackageManifest -RuntimeIdentifier linux-x64 -Version $version -Channel stable -RepositoryCommit $commit -RepositoryTree $tree -LockFile $fixtureLock -FrontendRoot $fixtureFrontend -AssetsPath $fixtureAssets
    if ($LASTEXITCODE -ne 0) { throw "Stable staging writer failed." }
    & $verifier -StagingDirectory (Join-Path $stableRoot "release-staging") -Channel stable -Version $version -RuntimeIdentifier linux-x64
    if ($LASTEXITCODE -ne 0) { throw "Stable staging verifier failed." }
    $tamperedRelease = Get-Content -Raw (Join-Path $stableRoot "release-staging/release-manifest.json") | ConvertFrom-Json -AsHashtable
    $tamperedRelease.repositoryCommit = "local"; $tamperedRelease.repositoryTree = "unavailable"
    $tamperedRelease | ConvertTo-Json -Depth 16 | Set-Content -Path (Join-Path $stableRoot "release-staging/release-manifest.json") -Encoding utf8NoBOM
    $releaseDigest = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $stableRoot "release-staging/release-manifest.json")).Hash.ToLowerInvariant()
    $sumPath = Join-Path $stableRoot "release-staging/SHA256SUMS"
    ((Get-Content $sumPath) | ForEach-Object { if ($_ -match '  release-manifest\.json$') { "$releaseDigest  release-manifest.json" } else { $_ } }) | Set-Content -Path $sumPath -Encoding ascii
    $failedClosed = $false
    try { & $verifier -StagingDirectory (Join-Path $stableRoot "release-staging") -Channel stable -Version $version -RuntimeIdentifier linux-x64 } catch { $failedClosed = $true }
    if (-not $failedClosed) { throw "Stable verifier accepted a checksum-recomputed source identity mismatch." }
    & $writer -ArtifactDirectory $stableRoot -ArchivePath $stableArchive -PackageManifestPath $stablePackageManifest -RuntimeIdentifier linux-x64 -Version $version -Channel stable -RepositoryCommit $commit -RepositoryTree $tree -LockFile $fixtureLock -FrontendRoot $fixtureFrontend -AssetsPath $fixtureAssets
    if ($LASTEXITCODE -ne 0) { throw "Stable staging recreation failed after the tamper regression." }
    $stableReleaseManifestPath = Join-Path $stableRoot "release-staging/release-manifest.json"
    $forgedRelease = Get-Content -Raw $stableReleaseManifestPath | ConvertFrom-Json -AsHashtable
    $forgedRelease.artifacts[0].version = "9.9.9-forged"
    $forgedRelease | ConvertTo-Json -Depth 16 | Set-Content -Path $stableReleaseManifestPath -Encoding utf8NoBOM
    $releaseDigest = (Get-FileHash -Algorithm SHA256 -Path $stableReleaseManifestPath).Hash.ToLowerInvariant()
    $sumPath = Join-Path $stableRoot "release-staging/SHA256SUMS"
    ((Get-Content $sumPath) | ForEach-Object { if ($_ -match '  release-manifest\.json$') { "$releaseDigest  release-manifest.json" } else { $_ } }) | Set-Content -Path $sumPath -Encoding ascii
    $failedClosed = $false
    try { & $verifier -StagingDirectory (Join-Path $stableRoot "release-staging") -Channel stable -Version $version -RuntimeIdentifier linux-x64 } catch { $failedClosed = $true }
    if (-not $failedClosed) { throw "Staging verifier accepted a checksum-recomputed forged artifact version." }
    & $writer -ArtifactDirectory $stableRoot -ArchivePath $stableArchive -PackageManifestPath $stablePackageManifest -RuntimeIdentifier linux-x64 -Version $version -Channel stable -RepositoryCommit $commit -RepositoryTree $tree -LockFile $fixtureLock -FrontendRoot $fixtureFrontend -AssetsPath $fixtureAssets
    if ($LASTEXITCODE -ne 0) { throw "Stable staging recreation failed after the forged-version regression." }

    $bundleInput = Join-Path $root "bundle-input"
    foreach ($rid in @("linux-x64", "osx-arm64", "win-x64")) {
        $platformRoot = Join-Path $bundleInput $rid
        New-Item -ItemType Directory -Force -Path $platformRoot | Out-Null
        $extension = if ($rid -eq "win-x64") { ".zip" } else { ".tar.gz" }
        $platformArchive = Join-Path $platformRoot "Runic.Translations.Editor-$version-$rid$extension"
        Copy-Item $archive $platformArchive
        $platformDigest = (Get-FileHash -Algorithm SHA256 -Path $platformArchive).Hash.ToLowerInvariant()
        Set-Content -Path "$platformArchive.sha256" -Value "$platformDigest  $([System.IO.Path]::GetFileName($platformArchive))" -Encoding ascii
        $manifest.runtimeIdentifier = $rid
        $platformManifest = Join-Path $platformRoot "package-manifest.json"
        $manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $platformManifest -Encoding utf8NoBOM
        & $writer -ArtifactDirectory $platformRoot -ArchivePath $platformArchive -PackageManifestPath $platformManifest -RuntimeIdentifier $rid -Version $version -Channel stable -RepositoryCommit $commit -RepositoryTree $tree -LockFile $fixtureLock -FrontendRoot $fixtureFrontend -AssetsPath $fixtureAssets
        if ($LASTEXITCODE -ne 0) { throw "Platform staging writer failed for $rid." }
    }
    $bundleOutput = Join-Path $root "bundle-output"
    & $bundleWriter -ArtifactsDirectory $bundleInput -OutputDirectory $bundleOutput -Version $version -Channel stable
    if ($LASTEXITCODE -ne 0) { throw "Central release-evidence bundle creation failed." }
    $bundlePath = Join-Path $bundleOutput "distribution/Runic.Translations.Editor-$version.zip"
    $receiptPath = Join-Path $bundleOutput "release-evidence-input/upstream-receipts/Runic.Translations.Editor-$version.receipt.template.json"
    if (-not (Test-Path $bundlePath) -or -not (Test-Path $receiptPath)) { throw "Central release-evidence bundle outputs are incomplete." }
    $receipt = Get-Content -Raw $receiptPath | ConvertFrom-Json -AsHashtable
    if ($receipt.artifact.path -ne "distribution/Runic.Translations.Editor-$version.zip") { throw "Central receipt template did not bind the declared authority artifact path." }
    if (-not [string]::IsNullOrWhiteSpace($CandidateSetOutput)) {
        if (Test-Path $CandidateSetOutput) { throw "Candidate-set output must not already exist." }
        New-Item -ItemType Directory -Path $CandidateSetOutput -Force | Out-Null
        foreach ($rid in @("linux-x64", "osx-arm64", "win-x64")) {
            $source = Join-Path $bundleInput $rid
            $target = Join-Path $CandidateSetOutput $rid
            $release = Get-Content -Raw (Join-Path $source "release-staging/release-manifest.json") | ConvertFrom-Json -AsHashtable
            $archiveName = [string]$release.artifacts[0].path
            New-Item -ItemType Directory -Path $target -Force | Out-Null
            Copy-Item (Join-Path $source $archiveName), (Join-Path $source "$archiveName.sha256") $target
            Copy-Item (Join-Path $source "release-staging") (Join-Path $target "release-staging") -Recurse
        }
    }
    Write-Host "Editor release staging contract passed."
}
finally {
    if (Test-Path $root) { Remove-Item -Recurse -Force $root }
}
