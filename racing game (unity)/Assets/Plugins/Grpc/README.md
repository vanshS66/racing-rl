# gRPC runtime

This folder contains the managed runtime for the Windows x64 Unity training spike.

- `Grpc.Core.dll` and `Grpc.Core.Api.dll`: `Grpc.Core` 2.46.6
- `Google.Protobuf.dll`: 3.21.12
- `System.Runtime.CompilerServices.Unsafe.dll`: 4.5.2
- `../grpc_csharp_ext.dll`: native `Grpc.Core` 2.46.6 runtime

The server listens only on `127.0.0.1:50051`. It is for the Unity Editor and Windows x64 standalone builds. Do not use it for IL2CPP, WebGL, mobile, or a network-exposed production server.

Run `tools/generate_unity_proto.ps1` after changing `proto/racing_rl.proto`.
