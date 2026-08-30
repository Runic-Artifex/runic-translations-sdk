param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactsDirectory,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [ValidateSet("preview", "stable")]
    [string]$Channel
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$verifier = Join-Path $repositoryRoot "eng/verify-editor-release-staging.ps1"
$root = (Resolve-Path $ArtifactsDirectory).Path
$stagingDirectories = @(Get-ChildItem -Path $root -Directory -Recurse | Where-Object Name -eq "release-staging" | Sort-Object FullName)
$expectedRuntimes = @("linux-x64", "osx-arm64", "win-x64")
if ($stagingDirectories.Count -ne $expectedRuntimes.Count) { throw "A public editor release requires exactly three platform staging directories." }

$manifests = [System.Collections.Generic.List[object]]::new()
foreach ($staging in $stagingDirectories) {
    $manifestPath = Join-Path $staging.FullName "release-manifest.json"
    if (-not (Test-Path $manifestPath -PathType Leaf)) { throw "Release staging '$($staging.FullName)' omitted its release manifest." }
    $manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json -AsHashtable
    if ($manifest.runtimeIdentifier -notin $expectedRuntimes) { throw "Unexpected editor runtime '$($manifest.runtimeIdentifier)'." }
    & $verifier -StagingDirectory $staging.FullName -Channel $Channel -Version $Version -RuntimeIdentifier $manifest.runtimeIdentifier
    if ($LASTEXITCODE -ne 0) { throw "Platform staging verification failed for '$($manifest.runtimeIdentifier)'." }
    $manifests.Add($manifest)
}

$runtimes = @($manifests | ForEach-Object runtimeIdentifier | Sort-Object)
if (@(Compare-Object -ReferenceObject $expectedRuntimes -DifferenceObject $runtimes).Count -ne 0) { throw "The public release platform set is incomplete or duplicated." }
$commits = @($manifests | ForEach-Object repositoryCommit | Select-Object -Unique)
$trees = @($manifests | ForEach-Object repositoryTree | Select-Object -Unique)
if ($commits.Count -ne 1 -or $trees.Count -ne 1) { throw "Every public editor platform artifact must bind one source revision and tree." }

Write-Host "Verified closed $Channel editor release set for $Version across all supported platforms."
$global:LASTEXITCODE = 0
