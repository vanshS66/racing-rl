# Python training

Start Unity with `main_training` in Play mode before running any Python bridge or training command.

## Verify the bridge

```powershell
py .\python\check_gym_env.py
```

## PPO validation run

```powershell
py .\python\train_ppo.py --timesteps 2048 --run-name ppo_check
```

The baseline uses one `DummyVecEnv` because one Unity process serves one serialized car. Its model and TensorBoard files are saved under `runs/<run-name>/`.

## Evaluate a saved model

Keep Unity running, then evaluate the final model with deterministic actions:

```powershell
py .\python\evaluate_ppo.py --model .\runs\ppo_baseline_01\ppo_racing_final.zip
```

The evaluator prints each episode's reward, length, termination reason, checkpoints reached, and average gRPC time.

## View TensorBoard

```powershell
py -m tensorboard.main --logdir .\runs
```

Open the local URL TensorBoard prints. Check `rollout/ep_rew_mean`, `rollout/ep_len_mean`, PPO loss/KL/entropy values, and the `bridge/*` values. The baseline is deliberately CPU-only; Unity physics and gRPC dominate this single-environment run.
