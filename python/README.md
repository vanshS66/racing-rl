# Python training

Unity must be running `main_training` in Play mode before any bridge, training, or evaluation command.

## Install

```powershell
py -m pip install -r python/requirements.txt
```

## Check the environment

```powershell
py .\python\health_check.py
py .\python\bridge_check.py
py .\python\check_gym_env.py
```

`check_gym_env.py` validates the Gymnasium API, observation bounds, action bounds, reset, and step behavior.

## Train PPO

Short check:

```powershell
py .\python\train_ppo.py --timesteps 4096 --run-name ppo_check
```

Longer run:

```powershell
py .\python\train_ppo.py --timesteps 100000 --run-name ppo_baseline
```

Training output is saved under `runs/<run-name>/`. The final model is `ppo_racing_final.zip`.

The project uses one `DummyVecEnv` because one Unity process serves one serialized car.

## Actions

PPO uses:

```text
[steering, throttle_control, brake]
```

- `steering`: `[-1, 1]`
- `throttle_control`: `[-1, 1]`
- `brake`: `[0, 1]`

`RacingGrpcEnv` maps `throttle_control` to Unity's `[0, 1]` throttle range. A zero-centered PPO policy therefore starts at 50% throttle with no brake.

## Evaluate

Keep Unity running, then use deterministic actions from a saved model:

```powershell
py .\python\evaluate_ppo.py --model .\runs\ppo_baseline\ppo_racing_final.zip
```

The evaluator prints reward, steps, terminal reason, checkpoint count, and average gRPC duration for each episode.

## TensorBoard

```powershell
py -m tensorboard.main --logdir .\runs
```

Useful values:

- `rollout/ep_rew_mean` and `rollout/ep_len_mean`
- `bridge/progress`, `bridge/min_ray`, and `bridge/forward_speed`
- `bridge/rpc_seconds` and `bridge/physics_ticks_per_rpc`
- PPO KL, clip fraction, entropy, and value loss

One `Step` request advances one Unity physics tick. The current bridge usually takes about 20 ms per request, so Unity physics and gRPC set the practical training speed.
