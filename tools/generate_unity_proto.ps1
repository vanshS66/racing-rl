$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = Join-Path $env:USERPROFILE ".nuget\packages"
$toolsRoot = Join-Path $packageRoot "grpc.tools\2.46.6\tools\windows_x64"
$outputRoot = Join-Path $projectRoot "racing game (unity)\Assets\Scripts\RL\Generated"
$protocPath = Join-Path $toolsRoot "protoc.exe"
$pluginPath = Join-Path $toolsRoot "grpc_csharp_plugin.exe"

New-Item -ItemType Directory -Force $outputRoot | Out-Null

Push-Location $projectRoot
try
{
    & $protocPath `
        "--proto_path=proto" `
        "--csharp_out=$outputRoot" `
        "--grpc_out=$outputRoot" `
        "--plugin=protoc-gen-grpc=$pluginPath" `
        "proto/racing_rl.proto"

    if ($LASTEXITCODE -ne 0)
    {
        throw "C# protobuf generation failed."
    }
}
finally
{
    Pop-Location
}
