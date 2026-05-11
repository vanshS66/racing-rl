# Unity Racing gRPC RL Bridge — Plan

## Objective

Build a deliberately small, complete Unity-to-Python reinforcement-learning bridge for the existing racing car. It follows Schola's core pattern—Protobuf contract, engine-side gRPC service, Python Gymnasium adapter, and Stable-Baselines3 training—without attempting a Unity feature-parity port.

Scope: one car, one Unity process, one serialized Python client, local gRPC, float-vector observations, continuous actions, and PPO. The vehicle physics and track remain the environment.

## Stage 0 — Prepare the existing car

Before networking, audit `PlayerControls.cs`, `Engine.cs`, and `Car.cs` to locate action input, the Rigidbody and wheel physics, existing raycasts/state, and reset/spawn support.

Create a small `RacingEnvironmentController` façade (not a rewrite of the car):

- `SetAction(steering, throttle, brake)` clamps and stores control inputs.
- The existing car applies these inputs only in `FixedUpdate`.
- `BuildObservation()` reads a documented post-physics state.
- `ResetEpisode(seed)` teleports to spawn, zeroes linear/angular velocity, and resets controller/wheel/drift state and counters.
- `ComputeRewardAndTermination()` returns reward, `terminated`, `truncated`, and a reason.

Make keyboard input optional; RL and keyboard input must never write controls simultaneously. Start with a fixed observation vector: normalized ray distances (5–9), forward/local lateral velocity, signed slip angle or defined proxy, yaw rate, normalized steering state, and optionally trustworthy track progress. All must be finite `float32` values with documented units/ranges/order.

Initial actions: steering `[-1,1]`, throttle `[0,1]`, brake `[0,1]`. Initial reward: checkpoint progress, small time cost, crash/off-track terminal penalty, lap-completion reward. Do not reward speed alone.

**Gate:** repeated resets give equivalent physics state; manual calls can drive/reset/read observations; no NaN/Infinity; a terminal and a truncation case exist.

Learning checkpoint: explain why this narrow control/observation seam is the small-scale counterpart to Schola's extensible sensor/actuator boundary.

## Stage 0.5 — Create a minimal training build

Before adding Protobuf or gRPC, strip the project down into a reproducible **training scene/configuration**. The goal is not to delete the playable game: retain the original scene/configuration for manual driving, and create a lean training variant that contains only what is necessary to simulate, reset, observe, and render/debug the car and track.

### Steps

1. Commit or back up the current working project first. Perform this stage as an audit with a written keep/remove decision; disable components/packages before deleting them so a broken dependency can be restored quickly.
2. Create a dedicated training scene (or a clearly documented training bootstrap) containing only:
   - the track colliders/visuals necessary for reliable driving;
   - the car, its rigidbody, wheel/collider physics, and required controller scripts;
   - spawn/checkpoint/reset objects;
   - the RL environment controller, raycast origins, and minimal debug gizmos;
   - one simple camera/light only if needed for Editor inspection. No image observations are in v1.
3. Audit all scene objects, `Assets/`, and Package Manager entries. For each item record: **keep**, **disable for training**, or **remove after validation**, plus the component/script that depends on it. Use Unity's Console and a development build after each small removal batch.
4. Disable in the training scene, then remove only after a clean test, non-essential systems such as:
   - FMOD audio events, listeners, banks, mixer components, and audio UI; retain only if a car script incorrectly depends on an audio callback, in which case first separate that dependency;
   - menu/HUD, lap presentation, pause, camera rigs, replay/cinematic effects, post-processing, particles, and decorative scene objects without physics relevance;
   - player-only input/UI scripts once the Stage 0 action seam is in place;
   - analytics, ads, multiplayer/networking, editor tooling, sample assets, and packages unused by the training scene or build pipeline.
5. Do **not** remove packages just because they look unrelated. First use project-wide references and a clean training-scene play/build test. Unity package removal can break assembly definitions, serialized components, imported assets, or project settings even where no C# reference is obvious.
6. Keep or replace deliberately: the Input System only if the retained manual-debug mode needs it; Cinemachine only if the debugging camera needs it; TextMeshPro/UI only if a minimal status panel is genuinely useful; and rendering/physics packages required by the chosen Unity project template.
7. Add a `TrainingMode` setting/boot path that disables all retained presentation/audio systems in one place rather than scattering `if` checks through car physics. Run the training configuration at the target fixed timestep and with a capped or disabled render frame rate when visual inspection is unnecessary.
8. Establish a baseline measurement before and after cleanup: fixed-step duration, frame time, memory, scene load time, and reset time. The bridge cannot compensate for a slow or nondeterministic simulation.

### Done checklist

- [ ] Original manual-driving scene/configuration remains runnable or recoverable from version control.
- [ ] Training scene launches with no missing-script/component errors or Console exceptions.
- [ ] Removing/turning off FMOD does not affect car physics, reset, or observation collection.
- [ ] The minimal scene can reset and run a fixed random-action smoke test without UI/audio/camera dependencies.
- [ ] Unused packages are removed only after a clean Editor and standalone development-build test.
- [ ] Baseline performance/reset measurements are recorded.

Learning checkpoint: explain why a separate training scene is safer than deleting gameplay features globally, why audio/UI should be decoupled from physics rather than merely hidden, and why faster rendering does not automatically mean faster RL simulation when `FixedUpdate`/physics is the bottleneck.

## Stage 1 — Define the Protobuf contract

Create `../proto/racing_rl.proto` with `syntax = "proto3"` and package `racingrl.v1`.

| Type | v1 fields |
| --- | --- |
| `Observation` | `repeated float ray_distances`, `forward_speed`, `lateral_speed`, `slip_angle`, `yaw_rate`, `progress` |
| `Action` | `steering`, `throttle`, `brake` |
| `ResetRequest` | `uint64 seed` |
| `ResetResponse` | `Observation observation`, diagnostic `info` |
| `StepRequest` | `Action action` |
| `StepResponse` | observation, reward, terminated, truncated, info |
| optional `HealthRequest/Response` | service/version status |

Define `RacingEnvironment` with unary `Reset`, `Step`, and optional `Health`. A `Step` holds an action for a server-configured `decision_ticks` number of physics frames (start around four), then returns the post-step transition.

Proto source is the wire-contract source of truth. Never hand-edit generated `.cs`, `_pb2.py`, or `_pb2_grpc.py`; never reuse released field numbers. Comment every unit, bounds and normalization rule. Validate finite/clamped actions at the Unity boundary.

Schola generalizes this cycle for multiple environments/agents, structured spaces, reusable observers/actuators, and lifecycle handling. This bridge intentionally fixes one agent and one float layout.

Learning checkpoint: explain the difference between Protobuf (message serialization) and gRPC (HTTP/2 RPC transport); and unary calls versus streaming. Unary is ideal for simple strict action/transition ordering. Streaming can reduce per-call overhead, but adds backpressure, cancellation, ordering and reconnection complexity.

## Stage 2 — Generate bindings and host gRPC in Unity

Use one reproducible generation script/command to generate C# into `Assets/Generated/RacingRL/` and Python into `python/generated/`. Pin compatible Protobuf/gRPC dependencies and record the exact package/version setup.

First prove a Unity `Health` service from a tiny Python client before touching car state. Test Editor and a Windows standalone build separately.

Unity-specific checks:

- A Unity player is not automatically an ASP.NET host; select a gRPC server/runtime path proven for Unity 2022.3.
- Match 64-bit native gRPC DLLs and Unity plug-in import settings to Editor/player target.
- Test Mono first; IL2CPP/managed stripping can require explicit preservation and is a later milestone.
- Server lifecycle must be idempotent across play-mode/domain reload and must release the port.
- Never access Unity objects/physics from a gRPC worker thread.

**Gate:** `protoc` generates both languages from the same proto; one Unity service starts/stops cleanly; Python health request succeeds in Editor and standalone build.

## Stage 3 — Unity service and main-thread bridge

Implement `RacingRlGrpcService` on gRPC worker threads and `UnityMainThreadDispatcher` plus `RacingEnvironmentController` on Unity's main thread.

For each RPC, the worker enqueues immutable request data plus async completion; it awaits completion with timeout/cancellation. A main-thread state machine drains one serialized request. `Step` validates action, applies it, advances exactly `decision_ticks` through `FixedUpdate`, builds post-step observation/reward/flags, then completes the response. Prefer this fixed-tick state machine over manual `Physics.Simulate` initially.

Do not block `FixedUpdate` for the network, call `Task.Result`/`.Wait()` on Unity's main thread, or use Unity APIs from the gRPC thread. These freeze/deadlock Unity or access non-thread-safe engine state.

Rules: reject `Step` before `Reset`; allow one in-flight request only; clear counters/actions on reset; cancel/mark abandoned pending work; include step count, simulation time and termination reason in info.

**Gate:** Python can reset and perform 100 steps; Unity stays responsive on disconnect; each step performs the configured number of physics ticks; all Unity API access is main-thread-only.

Learning checkpoint: explain why a coroutine is useful for the main-thread state machine but is not itself a gRPC server.

## Stage 4 — Custom Python Gymnasium environment

Write `RacingGrpcEnv(gymnasium.Env)` directly over generated gRPC bindings, not ML-Agents or an SB3 wrapper.

- `__init__`: create `grpc.insecure_channel`, generated stub, timeouts and closed flag; call `Health` and fail clearly when Unity is unavailable.
- Declare `observation_space = Box(..., shape=(N,), dtype=np.float32)` matching the flattened schema and explicit bounds.
- Declare `action_space = Box(low=[-1,0,0], high=[1,1,1], dtype=np.float32)`.
- `reset(*, seed=None, options=None)`: call `super().reset(seed=seed)`, send a representable seed, return `(obs, info)` and validate shape/dtype/space.
- `step(action)`: cast/validate `float32`, call RPC, return exactly `(obs, reward, terminated, truncated, info)`.
- `close()`: close the channel.

Add `check_bridge.py` for health, reset, random rollouts, forced terminal/truncation test, close, then run Gymnasium `check_env`.

Learning checkpoint: distinguish `terminated` (MDP terminal state such as crash/lap complete) from `truncated` (time/decision cap), and explain why the Python class hides protocol details behind Gymnasium's API.

## Stage 5 — PPO baseline and bridge verification

Create a pinned Python virtual environment with Gymnasium, `grpcio`, SB3 and CPU PyTorch initially. GTX 1650 is sufficient for a small vector MLP, but single-environment Unity physics/RPC time will usually dominate.

Start SB3 PPO with one `RacingGrpcEnv` in `DummyVecEnv`, `Monitor`, checkpoints and TensorBoard. Do not use `SubprocVecEnv` against one Unity car. Train on a short/easy checkpoint progress task before full laps or drifting. Record proto version, scene, reward values, fixed decision ticks, seed and dependency versions with models.

In TensorBoard confirm:

- `rollout/ep_rew_mean` changes plausibly compared with random play;
- `rollout/ep_len_mean` matches expected caps (not one-step failures);
- custom speed, progress, min-ray, off-track, reset count, server latency and physics-ticks-per-RPC logs are sensible;
- PPO loss/KL/entropy/value metrics are finite, not all zero or NaN;
- action histograms vary and stay within bounds.

Record one rollout and replay its actions; compare Unity's observation/reward/terminal sequence with Python's results. That transition trace verifies the bridge better than PPO merely running.

## Stage 6 — Demonstrate and document

Ship a diagram: `Env.step → gRPC worker → main-thread queue → fixed physics → response → SB3`. Add a README explaining setup, generation, run order, ports, limitations and a one-minute smoke test. Add tests for generated bindings, API/smoke rollouts, reset determinism within float limits, shape/action validation, timeout and shutdown.

## Explicit non-goals / later extensions

| Intentionally omitted now | Needed for production-style expansion |
| --- | --- |
| One serialized car/client | agent/environment IDs, lifecycle scheduler, multi-agent dict spaces |
| Fixed vector state | generic/structured spaces; reusable sensor/actuator interfaces |
| Unary RPCs | streaming/batching/backpressure/reconnect design |
| Localhost/no auth | security, process/port management and telemetry |
| One Unity process | headless isolated builds and real vectorized environments |
| SB3 PPO only | framework adapters, inference/export, evaluation/recording |

## Implementation order and decision gates

1. Complete Stage 0 before adding networking.
2. Review/commit proto before generated code.
3. Prove Health endpoint in Unity before car integration.
4. Prove random Python rollout and Gymnasium compliance before PPO.
5. Diagnose timing/reward/transition traces before optimizing transport.
6. Add only one advanced feature after a stable baseline.
