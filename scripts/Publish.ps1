param(
    [string[]]$Runtimes = @('win-x64', 'linux-x64', 'linux-arm64')
)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
foreach ($rid in $Runtimes) {
    $published = Join-Path $taskRoot "artifacts\publish\$rid"
    $output = Join-Path $taskRoot ("artifacts\staging\" + [guid]::NewGuid().ToString('N') + '\' + $rid)
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    & dotnet publish (Join-Path $taskRoot 'EnterpriseTester.ServerHost\EnterpriseTester.ServerHost.csproj') `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o (Join-Path $output 'app')
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed: $rid"
    }
    foreach ($directory in @('backend', 'config', 'docs')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $output $directory) | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'backend\EnterpriseTester.jar') -Destination (Join-Path $output 'backend') -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'backend\backend.json') -Destination (Join-Path $output 'backend') -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'config\server.example.json') -Destination (Join-Path $output 'config') -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $output -Force
    Copy-Item -Path (Join-Path $taskRoot 'docs\*') -Destination (Join-Path $output 'docs') -Force
    if ($rid.StartsWith('win-')) {
        Copy-Item -Path (Join-Path $taskRoot '*.bat') -Destination $output -Force
        Compress-Archive -LiteralPath $output -DestinationPath (Join-Path $taskRoot "artifacts\EnterpriseTesterServer-$rid.zip") -Force
    }
    else {
        Copy-Item -Path (Join-Path $taskRoot '*.sh') -Destination $output -Force
        & tar -czf (Join-Path $taskRoot "artifacts\EnterpriseTesterServer-$rid.tar.gz") -C $output .
        if ($LASTEXITCODE -ne 0) {
            throw 'Linux archive creation failed.'
        }
    }
    New-Item -ItemType Directory -Force -Path $published | Out-Null
    Get-ChildItem -LiteralPath $output | Copy-Item -Destination $published -Recurse -Force
    Write-Output "Ready: $published"
}
