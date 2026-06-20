$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = Join-Path $env:USERPROFILE ".nuget\packages"
$toolsRoot = Join-Path $packageRoot "grpc.tools\2.46.6\tools\windows_x64"
$outputRoot = Join-Path $projectRoot "racing game (unity)\Assets\Scripts\RL\Generated"

New-Item -ItemType Directory -Force $outputRoot | Out-Null

& (Join-Path $toolsRoot "protoc.exe") `
    --proto_path=(Join-Path $projectRoot "proto") `
    --csharp_out=$outputRoot `
    --grpc_out=$outputRoot `
    --plugin=protoc-gen-grpc=(Join-Path $toolsRoot "grpc_csharp_plugin.exe") `
    (Join-Path $projectRoot "proto\racing_rl.proto")

if ($LASTEXITCODE -ne 0)
{
    throw "C# protobuf generation failed."
}
