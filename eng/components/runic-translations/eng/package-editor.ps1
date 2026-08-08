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
$editorProject = Join-Path $repositoryRoot "samples/RunicTextResources.Editor/RunicTextResources.Editor.csproj"
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("runic-editor-package-" + [Guid]::NewGuid().ToString("N"))
$publishRoot = Join-Path $workRoot "RunicTextResources.Editor"
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

    dotnet restore $editorProject --runtime $RuntimeIdentifier
    if ($LASTEXITCODE -ne 0) { throw "Editor runtime restore failed." }
    dotnet publish $editorProject `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        --no-restore `
        --output $publishRoot `
        -p:RunicTextResourcesBuildMode=Verification `
        -p:Version=$Version `
        -p:SourceRevisionId=$RepositoryCommit `
        -p:RunicEditorUpdateChannel=preview
    if ($LASTEXITCODE -ne 0) { throw "Self-contained editor publish failed." }

    foreach ($required in @("www/index.html", "ExampleWorkspace/product.catalog.json", "LICENSE.txt", "THIRD-PARTY-NOTICES.md")) {
        if (-not (Test-Path (Join-Path $publishRoot $required))) { throw "Published editor omitted '$required'." }
    }

    $executableName = if ($IsWindows) { "RunicTextResources.Editor.exe" } else { "RunicTextResources.Editor" }
    $executable = Join-Path $publishRoot $executableName
    if (-not (Test-Path $executable)) { throw "Published editor executable was not produced." }
    $versionOutput = (& $executable --version) -join "`n"
    if ($LASTEXITCODE -ne 0 -or -not $versionOutput.Contains($Version) -or -not $versionOutput.Contains($RepositoryCommit) -or -not $versionOutput.Contains("Channel: preview")) {
        throw "The packaged executable did not carry the requested version, commit, and update channel."
    }
    & $executable --smoke-test --workspace (Join-Path $publishRoot "ExampleWorkspace")
    if ($LASTEXITCODE -ne 0) { throw "The self-contained editor failed its packaged startup smoke test." }
    $files = Get-ChildItem -Path $publishRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($publishRoot, $_.FullName).Replace('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        schema = "runic.textresources.editor-package/1"
        version = $Version
        updateChannel = "preview"
        repositoryCommit = $RepositoryCommit
        runtimeIdentifier = $RuntimeIdentifier
        selfContained = $true
        files = @($files)
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $publishRoot "package-manifest.json") -Encoding utf8NoBOM

    $baseName = "RunicTextResources.Editor-$Version-$RuntimeIdentifier"
    if ($IsWindows) {
        $archive = Join-Path $OutputDirectory "$baseName.zip"
        Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $archive -CompressionLevel Optimal
    } else {
        $archive = Join-Path $OutputDirectory "$baseName.tar.gz"
        tar -C $workRoot -czf $archive "RunicTextResources.Editor"
        if ($LASTEXITCODE -ne 0) { throw "Editor archive creation failed." }
    }
    $digest = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()
    Set-Content -Path "$archive.sha256" -Value "$digest  $([System.IO.Path]::GetFileName($archive))" -Encoding ascii
    Write-Host "Created self-contained editor preview: $archive"
}
finally {
    if (Test-Path $workRoot) { Remove-Item -Path $workRoot -Recurse -Force }
}
