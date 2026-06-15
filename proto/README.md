# Racing RL protocol

`racing_rl.proto` is the source of truth for the Unity/Python boundary. Generated C# and Python files are outputs and must not be edited by hand.

## Python generation

Install the pinned generator/runtime from the workspace root:

```powershell
py -m pip install -r python/requirements.txt
```

Generate Python bindings:

```powershell
py -m grpc_tools.protoc --proto_path=proto --python_out=python/generated --grpc_python_out=python/generated proto/racing_rl.proto
```

## Unity C# generation

The Unity spike uses the pinned `Grpc.Tools` package declared in `tools/RacingRlCodegen`. Restore it once, then regenerate the C# files after changing the schema:

```powershell
dotnet restore tools/RacingRlCodegen/RacingRlCodegen.csproj
.\tools\generate_unity_proto.ps1
```

The generated files belong in `racing game (unity)/Assets/Scripts/RL/Generated`. The native runtime is deliberately limited to the Windows x64 Editor/Standalone spike; see `Assets/Plugins/Grpc/README.md`.

Check the generated imports:

```powershell
py -c "import sys; sys.path.insert(0, 'python/generated'); import racing_rl_pb2; import racing_rl_pb2_grpc; print(racing_rl_pb2.DESCRIPTOR.package)"
```

## Step order

1. Python sends `ResetRequest`.
2. Unity resets the car and returns the first `Observation`.
3. Python sends one `Action` in `StepRequest`.
4. Unity applies that action, advances one fixed-physics tick, and then samples the result.
5. Unity returns the post-step observation, reward, terminal state, and info.

`Reset` and `Step` are unary calls on purpose. They make one action map to one completed simulation transition. Streaming is not needed until profiling shows call overhead matters.

With Unity in Play mode, use this transport smoke test before Gymnasium exists:

```powershell
py .\python\health_check.py
py .\python\bridge_smoke_test.py
```

## Observation order

`Observation` is flattened for Gymnasium as:

```text
[ray_distances..., forward_speed, lateral_speed, slip_angle, yaw_rate, progress]
```

| Field | Unity source | Range / unit |
| --- | --- | --- |
| `ray_distances` | `RacingEnvironmentController.BuildObservation` | 7 values by default, normalized `[0, 1]` |
| `forward_speed` | local Rigidbody Z velocity | `[-50, 50]` m/s |
| `lateral_speed` | local Rigidbody X velocity | `[-50, 50]` m/s |
| `slip_angle` | `atan2(localVelocity.x, localVelocity.z)` | `[-pi, pi]` radians |
| `yaw_rate` | Rigidbody Y angular velocity | `[-20, 20]` rad/s |
| `progress` | expected checkpoint index / checkpoint count | `[0, 1]` |

The current training scene has 7 rays, so the initial Gymnasium observation shape is `(12,)`. If ray count changes, `HealthResponse.observation_size` and the Python environment validation must change with it.

## Action contract

| Field | Range |
| --- | --- |
| `steering` | `[-1, 1]` |
| `throttle` | `[0, 1]` |
| `brake` | `[0, 1]` |

Unity clamps all three values at the car boundary. The agent does not control reverse or handbrake in v1.

## Versioning rules

- Never renumber or reuse an existing Protobuf field number.
- Add new optional fields with new numbers.
- Keep `racingrl.v1` until a deliberately incompatible contract needs `v2`.
- `seed = 0` currently means no seeded scene randomisation. A non-zero seed is reserved for a later reset-randomisation implementation.
- `terminated` is a true terminal state such as finish/crash; `truncated` is the decision-step limit.

## Deliberate limits

This protocol covers one car and one serialized caller. It has no agent IDs, generic spaces, streaming, reconnection IDs, vectorized environments, image observations, or inference support. Those are production concerns that Schola generalizes, but they would obscure the core bridge at this stage.
