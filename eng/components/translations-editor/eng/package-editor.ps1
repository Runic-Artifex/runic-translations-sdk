param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("linux-x64", "win-x64", "osx-arm64")]
    [string]$RuntimeIdentifier,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$Version = "1.0.0-preview.local",
    [string]$RepositoryCommit = "local",
    [string]$RepositoryTree = "",
    [ValidateSet("preview", "stable")]
    [string]$ReleaseChannel = "preview",
    [switch]$SkipReproducibilityCheck
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$editorProject = Join-Path $repositoryRoot "Runic.Translations.Editor.csproj"
$packageVerifier = Join-Path $repositoryRoot "eng/verify-editor-package.ps1"
$stagingWriter = Join-Path $repositoryRoot "eng/write-editor-release-staging.ps1"
$stagingVerifier = Join-Path $repositoryRoot "eng/verify-editor-release-staging.ps1"
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("runic-editor-package-" + [Guid]::NewGuid().ToString("N"))
$publishRoot = Join-Path $workRoot "Runic.Translations.Editor"
$expectedRuntimeIdentifier = if ($IsWindows) { "win-x64" } elseif ($IsMacOS) { "osx-arm64" } else { "linux-x64" }

if ($RuntimeIdentifier -ne $expectedRuntimeIdentifier) {
    throw "Packaging startup tests must run on the target OS. Expected '$expectedRuntimeIdentifier', received '$RuntimeIdentifier'."
}
if ([string]::IsNullOrWhiteSpace($Version) -or $Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$') {
    throw "Version must be SemVer-compatible."
}
if ($RepositoryCommit -notmatch '^(local|[a-f0-9]{40})$') { throw "RepositoryCommit must be 'local' or a lowercase 40-character Git revision." }
if (-not [string]::IsNullOrWhiteSpace($RepositoryTree) -and $RepositoryTree -notmatch '^[a-f0-9]{40}$') { throw "RepositoryTree must be a lowercase 40-character Git tree when supplied." }
if ($ReleaseChannel -eq "stable" -and ($RepositoryCommit -notmatch '^[a-f0-9]{40}$' -or $RepositoryTree -notmatch '^[a-f0-9]{40}$')) { throw "Stable packaging requires exact 40-character source revision and tree identifiers." }

function New-DeterministicZip([string]$SourceDirectory, [string]$ArchivePath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [System.IO.File]::Open($ArchivePath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            Get-ChildItem -Path $SourceDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
                $entryName = ([System.IO.Path]::GetFileName($SourceDirectory) + "/" + [System.IO.Path]::GetRelativePath($SourceDirectory, $_.FullName)).Replace('\', '/')
                $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = [System.IO.File]::OpenRead($_.FullName)
                try { $output = $entry.Open(); try { $input.CopyTo($output) } finally { $output.Dispose() } } finally { $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function New-DeterministicTarGzip([string]$SourceParent, [string]$ArchivePath) {
    $tarCommand = if (Get-Command gtar -ErrorAction SilentlyContinue) { "gtar" } elseif (Get-Command tar -ErrorAction SilentlyContinue) { "tar" } else { throw "A tar implementation is required to package Unix editor archives." }
    $gzipCommand = if (Get-Command gzip -ErrorAction SilentlyContinue) { "gzip" } else { throw "gzip is required to package Unix editor archives." }
    $tarPath = "$ArchivePath.tar"
    try {
        & $tarCommand --format=gnu --sort=name --mtime="UTC 1980-01-01" --owner=0 --group=0 --numeric-owner -C $SourceParent -cf $tarPath "Runic.Translations.Editor"
        if ($LASTEXITCODE -ne 0) { throw "Deterministic editor tar creation failed. The target image must provide GNU tar." }
        & $gzipCommand -n -c $tarPath > $ArchivePath
        if ($LASTEXITCODE -ne 0) { throw "Deterministic editor gzip creation failed." }
    }
    finally {
        if (Test-Path $tarPath -PathType Leaf) { Remove-Item -Path $tarPath -Force }
    }
}

function Get-PackageFileMap([string]$PackageManifestPath) {
    $manifest = Get-Content -Raw -Path $PackageManifestPath | ConvertFrom-Json -AsHashtable
    $result = @{}
    foreach ($file in @($manifest.files)) {
        $result[[string]$file.path] = [ordered]@{
            bytes = [int64]$file.bytes
            sha256 = [string]$file.sha256
        }
    }
    return $result
}

function Write-ReproducibilityDiagnostics(
    [string]$PrimaryArchive,
    [string]$ReproducibilityArchive,
    [string]$PrimaryManifest,
    [string]$ReproducibilityManifest) {
    $primaryFiles = Get-PackageFileMap $PrimaryManifest
    $reproducibilityFiles = Get-PackageFileMap $ReproducibilityManifest
    $paths = @($primaryFiles.Keys + $reproducibilityFiles.Keys | Sort-Object -Unique)
    $differences = @()
    foreach ($path in $paths) {
        $left = $primaryFiles[$path]
        $right = $reproducibilityFiles[$path]
        if ($null -eq $left -or $null -eq $right -or $left.bytes -ne $right.bytes -or $left.sha256 -ne $right.sha256) {
            $differences += $path
            $leftDescription = if ($null -eq $left) { "missing" } else { "bytes=$($left.bytes) sha256=$($left.sha256)" }
            $rightDescription = if ($null -eq $right) { "missing" } else { "bytes=$($right.bytes) sha256=$($right.sha256)" }
            Write-Warning "Reproducibility file difference '$path': primary {$leftDescription}; independent {$rightDescription}."
        }
    }
    if ($differences.Count -eq 0) {
        Write-Warning "Reproducibility package manifests match; only the archive container bytes differ."
        return
    }
    if (-not $IsMacOS -or -not (Get-Command otool -ErrorAction SilentlyContinue)) { return }

    $diagnosticRoot = Join-Path $workRoot "reproducibility-diagnostics"
    $primaryRoot = Join-Path $diagnosticRoot "primary"
    $reproducibilityRoot = Join-Path $diagnosticRoot "independent"
    New-Item -ItemType Directory -Path $primaryRoot, $reproducibilityRoot -Force | Out-Null
    tar -C $primaryRoot -xzf $PrimaryArchive
    if ($LASTEXITCODE -ne 0) { return }
    tar -C $reproducibilityRoot -xzf $ReproducibilityArchive
    if ($LASTEXITCODE -ne 0) { return }
    foreach ($path in $differences) {
        $primaryPath = Join-Path $primaryRoot "Runic.Translations.Editor/$path"
        $reproducibilityPath = Join-Path $reproducibilityRoot "Runic.Translations.Editor/$path"
        if (-not (Test-Path $primaryPath -PathType Leaf) -or -not (Test-Path $reproducibilityPath -PathType Leaf)) { continue }
        $primaryUuid = (& otool -l $primaryPath 2>$null | Select-String -Pattern '^\s*uuid\s+' | ForEach-Object { $_.Line.Trim() }) -join '; '
        $reproducibilityUuid = (& otool -l $reproducibilityPath 2>$null | Select-String -Pattern '^\s*uuid\s+' | ForEach-Object { $_.Line.Trim() }) -join '; '
        if ($primaryUuid.Length -ne 0 -or $reproducibilityUuid.Length -ne 0) {
            Write-Warning "Reproducibility Mach-O UUID '$path': primary {$primaryUuid}; independent {$reproducibilityUuid}."
        }
    }
}

try {
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $restoreConfig = $env:RUNIC_EDITOR_NUGET_CONFIG
    if ($restoreConfig) {
        if (-not (Test-Path $restoreConfig -PathType Leaf)) { throw "Configured coordinated NuGet feed '$restoreConfig' does not exist." }
        dotnet tool restore --configfile $restoreConfig
    } else {
        dotnet tool restore
    }
    if ($LASTEXITCODE -ne 0) { throw "Runic Translations tool restore failed." }
    $frontend = Join-Path $repositoryRoot "Frontend"
    if ($env:RUNIC_EDITOR_FRONTEND_CANDIDATES -eq "1") {
        if (-not (Test-Path (Join-Path $frontend "node_modules") -PathType Container)) { throw "The coordinated frontend candidates were not installed." }
    } else {
        bun install --cwd $frontend --frozen-lockfile --ignore-scripts
        if ($LASTEXITCODE -ne 0) { throw "Frontend dependency restore failed." }
    }
    $restoreArguments = @("restore", $editorProject, "--runtime", $RuntimeIdentifier)
    if ($restoreConfig) { $restoreArguments += "-p:RestoreConfigFile=$restoreConfig" }
    dotnet @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw "Editor runtime restore failed." }
    dotnet publish $editorProject `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        --no-restore `
        --output $publishRoot `
        -p:RunicTranslationsBuildMode=Verification `
        -p:Version=$Version `
        -p:SourceRevisionId=$RepositoryCommit `
        -p:RunicEditorUpdateChannel=$ReleaseChannel
    if ($LASTEXITCODE -ne 0) { throw "Self-contained editor publish failed." }

    foreach ($required in @("ExampleWorkspace/product.catalog.json", "LICENSE.txt", "THIRD-PARTY-NOTICES.md", "PREVIEW-NOTICE.md", "runic-translations-editor", "runic-translations-editor.cmd")) {
        if (-not (Test-Path (Join-Path $publishRoot $required))) { throw "Published editor omitted '$required'." }
    }
    $executableName = if ($IsWindows) { "Runic.Translations.Editor.exe" } else { "Runic.Translations.Editor" }
    $executable = Join-Path $publishRoot $executableName
    if (-not (Test-Path $executable)) { throw "Published editor executable was not produced." }
    if (-not $IsWindows) {
        chmod +x (Join-Path $publishRoot "runic-translations-editor")
        if ($LASTEXITCODE -ne 0) { throw "The Unix launcher could not be marked executable." }
    }

    $files = Get-ChildItem -Path $publishRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($publishRoot, $_.FullName).Replace('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        schema = "runic.translations.editor-package/1"
        version = $Version
        updateChannel = $ReleaseChannel
        repositoryCommit = $RepositoryCommit
        repositoryTree = if ($RepositoryTree) { $RepositoryTree } else { "unavailable" }
        runtimeIdentifier = $RuntimeIdentifier
        selfContained = $true
        files = @($files)
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $publishRoot "package-manifest.json") -Encoding utf8NoBOM

    $baseName = "Runic.Translations.Editor-$Version-$RuntimeIdentifier"
    if ($IsWindows) {
        $archive = Join-Path $OutputDirectory "$baseName.zip"
        New-DeterministicZip $publishRoot $archive
    } else {
        $archive = Join-Path $OutputDirectory "$baseName.tar.gz"
        New-DeterministicTarGzip $workRoot $archive
    }
    $digest = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()
    Set-Content -Path "$archive.sha256" -Value "$digest  $([System.IO.Path]::GetFileName($archive))" -Encoding ascii
    $recordedDigest = ((Get-Content -Raw -Path "$archive.sha256").Trim() -split '\s+', 2)[0]
    if ($recordedDigest -ne (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()) {
        throw "The archive does not match its sibling SHA-256 file."
    }

    $extractionRoot = Join-Path $workRoot "archive-test"
    New-Item -ItemType Directory -Path $extractionRoot -Force | Out-Null
    if ($IsWindows) {
        Expand-Archive -Path $archive -DestinationPath $extractionRoot
    } else {
        tar -C $extractionRoot -xzf $archive
        if ($LASTEXITCODE -ne 0) { throw "The editor archive could not be extracted." }
    }
    $extractedPackage = Join-Path $extractionRoot "Runic.Translations.Editor"
    if (-not (Test-Path $extractedPackage -PathType Container)) {
        throw "The archive did not contain the expected Runic.Translations.Editor root directory."
    }
    & $packageVerifier `
        -PackageDirectory $extractedPackage `
        -Version $Version `
        -RepositoryCommit $RepositoryCommit `
        -RuntimeIdentifier $RuntimeIdentifier `
        -ReleaseChannel $ReleaseChannel `
        -RepositoryTree $RepositoryTree
    if ($LASTEXITCODE -ne 0) { throw "The extracted editor package verification failed." }

    & $stagingWriter `
        -ArtifactDirectory $OutputDirectory `
        -ArchivePath $archive `
        -PackageManifestPath (Join-Path $publishRoot "package-manifest.json") `
        -RuntimeIdentifier $RuntimeIdentifier `
        -Version $Version `
        -Channel $ReleaseChannel `
        -RepositoryCommit $RepositoryCommit `
        -RepositoryTree $RepositoryTree
    if ($LASTEXITCODE -ne 0) { throw "Closed release staging creation failed." }
    & $stagingVerifier `
        -StagingDirectory (Join-Path $OutputDirectory "release-staging") `
        -Channel $ReleaseChannel `
        -Version $Version `
        -RuntimeIdentifier $RuntimeIdentifier
    if ($LASTEXITCODE -ne 0) { throw "Closed release staging verification failed." }

    if (-not $SkipReproducibilityCheck) {
        $reproRoot = Join-Path $workRoot "reproducibility"
        New-Item -ItemType Directory -Path $reproRoot -Force | Out-Null
        & $PSCommandPath `
            -RuntimeIdentifier $RuntimeIdentifier `
            -OutputDirectory $reproRoot `
            -Version $Version `
            -RepositoryCommit $RepositoryCommit `
            -RepositoryTree $RepositoryTree `
            -ReleaseChannel $ReleaseChannel `
            -SkipReproducibilityCheck
        if ($LASTEXITCODE -ne 0) { throw "The independent reproducibility package build failed." }
        $reproArchive = Join-Path $reproRoot ([System.IO.Path]::GetFileName($archive))
        if (-not (Test-Path $reproArchive) -or (Get-FileHash -Algorithm SHA256 -Path $archive).Hash -ne (Get-FileHash -Algorithm SHA256 -Path $reproArchive).Hash) {
            Write-ReproducibilityDiagnostics `
                -PrimaryArchive $archive `
                -ReproducibilityArchive $reproArchive `
                -PrimaryManifest (Join-Path $OutputDirectory "release-staging/package-manifest.json") `
                -ReproducibilityManifest (Join-Path $reproRoot "release-staging/package-manifest.json")
            throw "The independent package build did not reproduce the exact archive digest."
        }
        foreach ($name in @("package-manifest.json", "dependencies.json", "sbom.spdx.json", "provenance.json")) {
            $left = Join-Path $OutputDirectory "release-staging/$name"
            $right = Join-Path $reproRoot "release-staging/$name"
            if ((Get-FileHash -Algorithm SHA256 -Path $left).Hash -ne (Get-FileHash -Algorithm SHA256 -Path $right).Hash) { throw "The reproducibility build changed '$name'." }
        }
    }

    Write-Host "Created self-contained editor $ReleaseChannel archive: $archive"
}
finally {
    if (Test-Path $workRoot) { Remove-Item -Path $workRoot -Recurse -Force }
}
