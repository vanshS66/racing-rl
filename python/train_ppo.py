import argparse
from datetime import datetime
import json
from pathlib import Path

import gymnasium
import numpy as np
import stable_baselines3
from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import BaseCallback, CheckpointCallback
from stable_baselines3.common.monitor import Monitor
from stable_baselines3.common.vec_env import DummyVecEnv
import torch

from racing_grpc_env import RacingGrpcEnv


class BridgeMetricsCallback(BaseCallback):
    def _on_step(self):
        observation = self.locals["new_obs"][0]
        action = self.locals["actions"][0]
        bounded_action = np.clip(action, self.training_env.action_space.low, self.training_env.action_space.high)

        self.logger.record_mean("bridge/forward_speed", float(observation[-7]))
        self.logger.record_mean("bridge/lateral_speed", float(observation[-6]))
        self.logger.record_mean("bridge/progress", float(observation[-1]))
        self.logger.record_mean("bridge/min_ray", float(min(observation[:-7])))
        self.logger.record_mean("bridge/target_lateral", float(observation[-3]))
        self.logger.record_mean("bridge/target_forward", float(observation[-2]))
        self.logger.record_mean("bridge/rpc_seconds", float(self.training_env.get_attr("last_rpc_seconds")[0]))
        self.logger.record_mean("bridge/steering", float(bounded_action[0]))
        self.logger.record_mean("bridge/throttle", float(bounded_action[1]))
        self.logger.record_mean("bridge/brake", float(bounded_action[2]))
        self.logger.record("bridge/physics_ticks_per_rpc", 1)
        return True


def parse_args():
    parser = argparse.ArgumentParser(description="Train PPO against the Unity racing gRPC environment.")
    parser.add_argument("--timesteps", type=int, default=100_000)
    parser.add_argument("--seed", type=int, default=123)
    parser.add_argument("--address", default="127.0.0.1:50051")
    parser.add_argument("--run-name", default=None)
    return parser.parse_args()


def main():
    args = parse_args()
    if args.timesteps <= 0:
        raise ValueError("timesteps must be positive.")

    torch.set_num_threads(1)
    run_name = args.run_name or datetime.now().strftime("ppo_%Y%m%d_%H%M%S")
    run_root = Path("runs") / run_name
    checkpoint_root = run_root / "checkpoints"
    checkpoint_root.mkdir(parents=True, exist_ok=False)

    base_env = RacingGrpcEnv(address=args.address)
    try:
        config = {
            "address": args.address,
            "seed": args.seed,
            "timesteps": args.timesteps,
            "service_version": base_env.service_version,
            "observation_shape": base_env.observation_space.shape,
            "action_shape": base_env.action_space.shape,
            "gymnasium": gymnasium.__version__,
            "stable_baselines3": stable_baselines3.__version__,
            "torch": torch.__version__,
            "device": "cpu",
            "scene": "main_training",
            "decision_ticks_per_rpc": 1,
        }
        (run_root / "run_config.json").write_text(json.dumps(config, indent=2), encoding="utf-8")

        env = DummyVecEnv([lambda: Monitor(base_env, filename=str(run_root / "monitor"))])
        checkpoint_callback = CheckpointCallback(
            save_freq=10_000,
            save_path=str(checkpoint_root),
            name_prefix="ppo_racing",
        )
        model = PPO(
            "MlpPolicy",
            env,
            learning_rate=3e-4,
            n_steps=256,
            batch_size=64,
            n_epochs=10,
            gamma=0.99,
            gae_lambda=0.95,
            ent_coef=0.01,
            policy_kwargs={"net_arch": [64, 64]},
            tensorboard_log=str(run_root / "tensorboard"),
            seed=args.seed,
            device="cpu",
            verbose=1,
        )
        model.learn(
            total_timesteps=args.timesteps,
            callback=[checkpoint_callback, BridgeMetricsCallback()],
            tb_log_name="ppo",
        )
        model.save(str(run_root / "ppo_racing_final"))
        print("saved model:", run_root / "ppo_racing_final.zip")
    finally:
        base_env.close()


if __name__ == "__main__":
    main()
