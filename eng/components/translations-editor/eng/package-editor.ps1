param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("linux-x64", "win-x64", "osx-arm64")]
    [string]$RuntimeIdentifier,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$Version = "0.1.0-preview.local",
    [string]$RepositoryCommit = "local"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$editorProject = Join-Path $repositoryRoot "RunicTranslations.Editor.csproj"
$packageVerifier = Join-Path $repositoryRoot "eng/verify-editor-package.ps1"
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("runic-editor-package-" + [Guid]::NewGuid().ToString("N"))
$publishRoot = Join-Path $workRoot "RunicTranslations.Editor"
$expectedRuntimeIdentifier = if ($IsWindows) { "win-x64" } elseif ($IsMacOS) { "osx-arm64" } else { "linux-x64" }

if ($RuntimeIdentifier -ne $expectedRuntimeIdentifier) {
    throw "Packaging startup tests must run on the target OS. Expected '$expectedRuntimeIdentifier', received '$RuntimeIdentifier'."
}
if ([string]::IsNullOrWhiteSpace($Version) -or $Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$') {
    throw "Version must be SemVer-compatible."
}

try {
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "Runic Translations tool restore failed." }
    npm --prefix (Join-Path $repositoryRoot "Frontend") ci --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "Frontend dependency restore failed." }
    dotnet restore $editorProject --runtime $RuntimeIdentifier
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
        -p:RunicEditorUpdateChannel=preview
    if ($LASTEXITCODE -ne 0) { throw "Self-contained editor publish failed." }

    foreach ($required in @("www/index.html", "ExampleWorkspace/product.catalog.json", "LICENSE.txt", "THIRD-PARTY-NOTICES.md", "PREVIEW-NOTICE.md", "runic-translations-editor", "runic-translations-editor.cmd")) {
        if (-not (Test-Path (Join-Path $publishRoot $required))) { throw "Published editor omitted '$required'." }
    }
    $executableName = if ($IsWindows) { "RunicTranslations.Editor.exe" } else { "RunicTranslations.Editor" }
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
        updateChannel = "preview"
        repositoryCommit = $RepositoryCommit
        runtimeIdentifier = $RuntimeIdentifier
        selfContained = $true
        files = @($files)
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $publishRoot "package-manifest.json") -Encoding utf8NoBOM

    $baseName = "RunicTranslations.Editor-$Version-$RuntimeIdentifier"
    if ($IsWindows) {
        $archive = Join-Path $OutputDirectory "$baseName.zip"
        Compress-Archive -Path $publishRoot -DestinationPath $archive -CompressionLevel Optimal
    } else {
        $archive = Join-Path $OutputDirectory "$baseName.tar.gz"
        tar -C $workRoot -czf $archive "RunicTranslations.Editor"
        if ($LASTEXITCODE -ne 0) { throw "Editor archive creation failed." }
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
    $extractedPackage = Join-Path $extractionRoot "RunicTranslations.Editor"
    if (-not (Test-Path $extractedPackage -PathType Container)) {
        throw "The archive did not contain the expected RunicTranslations.Editor root directory."
    }
    & $packageVerifier `
        -PackageDirectory $extractedPackage `
        -Version $Version `
        -RepositoryCommit $RepositoryCommit `
        -RuntimeIdentifier $RuntimeIdentifier
    if ($LASTEXITCODE -ne 0) { throw "The extracted editor package verification failed." }

    Write-Host "Created self-contained editor preview: $archive"
}
finally {
    if (Test-Path $workRoot) { Remove-Item -Path $workRoot -Recurse -Force }
}
