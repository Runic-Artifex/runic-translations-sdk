param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$RepositoryCommit,
    [Parameter(Mandatory = $true)]
    [ValidateSet("linux-x64", "win-x64", "osx-arm64")]
    [string]$RuntimeIdentifier
)

$ErrorActionPreference = "Stop"
$packageRoot = (Resolve-Path $PackageDirectory).Path
$manifestPath = Join-Path $packageRoot "package-manifest.json"
if (-not (Test-Path $manifestPath -PathType Leaf)) { throw "The package manifest is missing." }

$manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
if ($manifest.schema -ne "runic.translations.editor-package/1") { throw "The package manifest schema is unsupported." }
if ($manifest.version -ne $Version) { throw "The package version does not match '$Version'." }
if ($manifest.updateChannel -ne "preview") { throw "The package update channel is not 'preview'." }
if ($manifest.repositoryCommit -ne $RepositoryCommit) { throw "The package source commit does not match '$RepositoryCommit'." }
if ($manifest.runtimeIdentifier -ne $RuntimeIdentifier) { throw "The package runtime identifier does not match '$RuntimeIdentifier'." }
if ($manifest.selfContained -ne $true) { throw "The package is not marked self-contained." }

$listed = @($manifest.files | ForEach-Object { $_.path } | Sort-Object)
if (@($listed | Select-Object -Unique).Count -ne $listed.Count) { throw "The package manifest contains duplicate paths." }
$actual = @(Get-ChildItem -Path $packageRoot -File -Recurse |
    ForEach-Object { [System.IO.Path]::GetRelativePath($packageRoot, $_.FullName).Replace('\', '/') } |
    Where-Object { $_ -ne "package-manifest.json" } |
    Sort-Object)
$difference = @(Compare-Object -ReferenceObject $listed -DifferenceObject $actual)
if ($difference.Count -ne 0) {
    throw "The package file set differs from package-manifest.json: $($difference | Out-String)"
}

foreach ($file in $manifest.files) {
    if ([System.IO.Path]::IsPathRooted($file.path) -or ($file.path.Split('/') -contains '..')) {
        throw "The package manifest path '$($file.path)' is not contained in the package."
    }
    $path = Join-Path $packageRoot $file.path
    if (-not (Test-Path $path -PathType Leaf)) { throw "The manifest entry '$($file.path)' is missing." }
    $item = Get-Item $path
    if ($item.Length -ne $file.bytes) { throw "The byte count for '$($file.path)' does not match the manifest." }
    $digest = (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLowerInvariant()
    if ($digest -ne $file.sha256) { throw "The SHA-256 digest for '$($file.path)' does not match the manifest." }
}

$executableName = if ($IsWindows) { "RunicTranslations.Editor.exe" } else { "RunicTranslations.Editor" }
$executable = Join-Path $packageRoot $executableName
$workspace = Join-Path $packageRoot "ExampleWorkspace"
$versionOutput = (& $executable --version) -join "`n"
if ($LASTEXITCODE -ne 0 -or -not $versionOutput.Contains($Version) -or -not $versionOutput.Contains($RepositoryCommit) -or -not $versionOutput.Contains("Channel: preview")) {
    throw "The packaged executable did not carry the requested version, commit, and update channel."
}
& $executable validate $workspace
if ($LASTEXITCODE -ne 0) { throw "The packaged validation command rejected the example workspace." }
& $executable --smoke-test --workspace $workspace
if ($LASTEXITCODE -ne 0) { throw "The packaged editor failed its compiler/save/recovery smoke test." }

$launcher = if ($IsWindows) {
    Join-Path $packageRoot "runic-translations-editor.cmd"
} else {
    Join-Path $packageRoot "runic-translations-editor"
}
& $launcher validate $workspace
if ($LASTEXITCODE -ne 0) { throw "The public launcher did not run validation successfully." }

Write-Host "Verified extracted editor package '$packageRoot'."
