param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "RunicTextResources.Editor.csproj"
$frontend = Join-Path $repositoryRoot "Frontend"
$workspace = Join-Path $repositoryRoot "ExampleWorkspace"
$publishOutput = Join-Path ([System.IO.Path]::GetTempPath()) ("runic-editor-smoke-" + [Guid]::NewGuid().ToString("N"))

try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "Runic Translations tool restore failed." }
    npm --prefix $frontend ci --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "Frontend dependency restore failed." }
    dotnet build $project --configuration $Configuration -p:RunicTextResourcesBuildMode=Verification
    if ($LASTEXITCODE -ne 0) { throw "Warning-free editor build failed." }

    node (Join-Path $frontend "test/verify-message-preview.mjs")
    if ($LASTEXITCODE -ne 0) { throw "Message preview test failed." }
    node (Join-Path $frontend "test/verify-review-model.mjs")
    if ($LASTEXITCODE -ne 0) { throw "Review and scale test failed." }
    dotnet run --project $project --configuration $Configuration --no-build -- --smoke-test --workspace $workspace
    if ($LASTEXITCODE -ne 0) { throw "Editor smoke test failed." }

    dotnet publish $project --configuration $Configuration --no-restore --output $publishOutput
    if ($LASTEXITCODE -ne 0) { throw "Editor publish failed." }
    dotnet (Join-Path $publishOutput "RunicTextResources.Editor.dll") --smoke-test --workspace (Join-Path $publishOutput "ExampleWorkspace")
    if ($LASTEXITCODE -ne 0) { throw "Published editor smoke test failed." }
    Write-Host "Cross-platform editor smoke passed."
}
finally {
    if (Test-Path $publishOutput) { Remove-Item -Path $publishOutput -Recurse -Force }
}

$global:LASTEXITCODE = 0

