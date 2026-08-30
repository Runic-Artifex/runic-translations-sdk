param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "Runic.Translations.Editor.csproj"
$frontend = Join-Path $repositoryRoot "Frontend"
$workspace = Join-Path $repositoryRoot "ExampleWorkspace"
$publishOutput = Join-Path ([System.IO.Path]::GetTempPath()) ("runic-editor-smoke-" + [Guid]::NewGuid().ToString("N"))

try {
    $restoreConfig = $env:RUNIC_EDITOR_NUGET_CONFIG
    if ($restoreConfig) {
        if (-not (Test-Path $restoreConfig -PathType Leaf)) { throw "Configured coordinated NuGet feed '$restoreConfig' does not exist." }
        dotnet tool restore --configfile $restoreConfig
    } else {
        dotnet tool restore
    }
    if ($LASTEXITCODE -ne 0) { throw "Runic Translations tool restore failed." }
    if ($env:RUNIC_EDITOR_FRONTEND_CANDIDATES -eq "1") {
        if (-not (Test-Path (Join-Path $frontend "node_modules") -PathType Container)) { throw "The coordinated frontend candidates were not installed." }
    } else {
        npm --prefix $frontend ci --ignore-scripts --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw "Frontend dependency restore failed." }
    }
    $buildArguments = @("build", $project, "--configuration", $Configuration, "-p:RunicTranslationsBuildMode=Verification")
    if ($restoreConfig) { $buildArguments += "-p:RestoreConfigFile=$restoreConfig" }
    dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) { throw "Warning-free editor build failed." }

    node (Join-Path $frontend "test/verify-message-preview.mjs")
    if ($LASTEXITCODE -ne 0) { throw "Message preview test failed." }
    node --expose-gc (Join-Path $frontend "test/verify-review-model.mjs")
    if ($LASTEXITCODE -ne 0) { throw "Review and scale test failed." }
    dotnet run --project $project --configuration $Configuration --no-build -- --smoke-test --workspace $workspace
    if ($LASTEXITCODE -ne 0) { throw "Editor smoke test failed." }
    dotnet run --project $project --configuration $Configuration --no-build -- validate $workspace
    if ($LASTEXITCODE -ne 0) { throw "Headless editor validation failed." }

    dotnet publish $project --configuration $Configuration --no-restore --output $publishOutput
    if ($LASTEXITCODE -ne 0) { throw "Editor publish failed." }
    dotnet (Join-Path $publishOutput "Runic.Translations.Editor.dll") --smoke-test --workspace (Join-Path $publishOutput "ExampleWorkspace")
    if ($LASTEXITCODE -ne 0) { throw "Published editor smoke test failed." }
    Write-Host "Cross-platform editor smoke passed."
}
finally {
    if (Test-Path $publishOutput) { Remove-Item -Path $publishOutput -Recurse -Force }
}

$global:LASTEXITCODE = 0
