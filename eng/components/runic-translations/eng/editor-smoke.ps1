param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("runic-editor-cross-platform-" + [Guid]::NewGuid().ToString("N"))
$templateOutput = Join-Path $artifactsRoot "template"
$publishOutput = Join-Path $artifactsRoot "editor"
$templatePackage = $null

try {
    New-Item -ItemType Directory -Path $artifactsRoot | Out-Null

    dotnet restore (Join-Path $repositoryRoot "RunicTextResources.slnx")
    if ($LASTEXITCODE -ne 0) { throw "Solution restore failed." }

    dotnet build (Join-Path $repositoryRoot "RunicTextResources.slnx") `
        --configuration $Configuration `
        --no-restore `
        -p:RunicTextResourcesBuildMode=Verification
    if ($LASTEXITCODE -ne 0) { throw "Warning-free solution build failed." }

    $authoringTests = Join-Path $repositoryRoot "dotnet/tests/RunicTextResources.Authoring.Tests/RunicTextResources.Authoring.Tests.csproj"
    dotnet run --project $authoringTests --configuration $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Authoring and hostile-workspace tests failed." }

    $templateProject = Join-Path $repositoryRoot "dotnet/templates/RunicTextResources.Templates/RunicTextResources.Templates.csproj"
    $packageOutput = Join-Path $artifactsRoot "packages"
    dotnet pack $templateProject --configuration $Configuration --no-build --output $packageOutput -p:PackageVersion=1.0.0-smoke
    if ($LASTEXITCODE -ne 0) { throw "Template package creation failed." }
    $templatePackage = (Get-ChildItem -Path $packageOutput -Filter "RunicTextResources.Templates.*.nupkg" | Select-Object -First 1).FullName
    if ([string]::IsNullOrWhiteSpace($templatePackage)) { throw "Template package was not produced." }

    dotnet new install $templatePackage | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Template installation failed." }
    dotnet new runic-textresources `
        --output $templateOutput `
        --catalog smoke `
        --defaultLocale de `
        --namespace Customer.Smoke `
        --className SmokeText
    if ($LASTEXITCODE -ne 0) { throw "Template instantiation failed." }

    $editorProject = Join-Path $repositoryRoot "samples/RunicTextResources.Editor/RunicTextResources.Editor.csproj"
    $exampleWorkspace = Join-Path $repositoryRoot "samples/RunicTextResources.Editor/ExampleWorkspace"
    $previewTest = Join-Path $repositoryRoot "samples/RunicTextResources.Editor/Frontend/test/verify-message-preview.mjs"
    node $previewTest
    if ($LASTEXITCODE -ne 0) { throw "Editor message preview test failed." }
    $reviewTest = Join-Path $repositoryRoot "samples/RunicTextResources.Editor/Frontend/test/verify-review-model.mjs"
    node $reviewTest
    if ($LASTEXITCODE -ne 0) { throw "Editor review and scale test failed." }
    dotnet run --project $editorProject --configuration $Configuration --no-build -- --smoke-test --workspace $exampleWorkspace
    if ($LASTEXITCODE -ne 0) { throw "Editor smoke test failed." }

    dotnet publish $editorProject --configuration $Configuration --no-restore --output $publishOutput
    if ($LASTEXITCODE -ne 0) { throw "Editor publish failed." }
    dotnet (Join-Path $publishOutput "RunicTextResources.Editor.dll") --smoke-test --workspace (Join-Path $publishOutput "ExampleWorkspace")
    if ($LASTEXITCODE -ne 0) { throw "Published editor smoke test failed." }

    Write-Host "Cross-platform editor smoke passed."
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($templatePackage)) {
        dotnet new uninstall $templatePackage 2>$null | Out-Null
    }
    if (Test-Path $artifactsRoot) {
        Remove-Item -Path $artifactsRoot -Recurse -Force
    }
}

# A best-effort template uninstall must not turn an otherwise successful smoke
# run into a failing process on platforms where the CLI returns a cleanup error.
$global:LASTEXITCODE = 0
