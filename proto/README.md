# Racing RL protocol

`racing_rl.proto` is the Unity/Python contract. Generated C# and Python files come from this file and should not be edited by hand.

## Generate Python bindings

```powershell
py -m pip install -r python/requirements.txt
py -m grpc_tools.protoc --proto_path=proto --python_out=python/generated --grpc_python_out=python/generated proto/racing_rl.proto
```

## Generate Unity bindings

The Unity spike uses the pinned `Grpc.Tools` package in `tools/RacingRlCodegen`.

```powershell
dotnet restore tools/RacingRlCodegen/RacingRlCodegen.csproj
.\tools\generate_unity_proto.ps1
```

Generated C# files go in `racing game (unity)/Assets/Scripts/RL/Generated`.

## RPCs

| RPC | Purpose |
| --- | --- |
| `Health` | Reports protocol version, observation size, and action size. |
| `Reset` | Resets the car and returns the first observation. |
| `Step` | Applies one action, advances one Unity physics tick, then returns the result. |

`Reset` and `Step` are unary calls. One Python action maps to one completed simulation transition.

## Observation

Gymnasium receives the flattened observation in this order:

```text
[ray_distances..., forward_speed, lateral_speed, slip_angle, yaw_rate,
 target_lateral, target_forward, progress]
```

| Field | Range | Notes |
| --- | --- | --- |
| `ray_distances` | `[0, 1]` | 17 downward `RoadSensor` probes. `1` means road was found below the probe. |
| `forward_speed` | `[-50, 50]` | Local Rigidbody Z velocity in m/s. |
| `lateral_speed` | `[-50, 50]` | Local Rigidbody X velocity in m/s. |
| `slip_angle` | `[-pi, pi]` | Calculated from local lateral and forward velocity. |
| `yaw_rate` | `[-20, 20]` | Rigidbody Y angular velocity. |
| `target_lateral` | `[-1, 1]` | Local normalized direction to the expected checkpoint. |
| `target_forward` | `[-1, 1]` | Local normalized direction to the expected checkpoint. |
| `progress` | `[0, 1]` | Expected checkpoint index divided by checkpoint count. |

The training scene currently returns 24 values: 17 probes and 7 scalar values. `HealthResponse.observation_size` is the authoritative count.

## Action

The wire contract always sends Unity these values:

| Field | Range |
| --- | --- |
| `steering` | `[-1, 1]` |
| `throttle` | `[0, 1]` |
| `brake` | `[0, 1]` |

Unity clamps the action at the car boundary. Reverse and handbrake are not part of the v1 contract.

## Episode result

`StepResponse` includes reward plus Gymnasium-style terminal state:

- `terminated`: lap complete, below world, rollover, off-road, or stalled.
- `truncated`: decision-step limit reached.

## Versioning

- Never reuse or renumber a field number.
- Add optional fields with new numbers.
- Keep `racingrl.v1` until there is a deliberate incompatible protocol change.
- `seed = 0` currently means the scene's normal reset. Seeded scene randomization is not implemented yet.

## Scope

This protocol is for one serialized caller and one car. It intentionally has no agent IDs, generic spaces, streaming, reconnection IDs, vectorized environments, image observations, or inference support.
