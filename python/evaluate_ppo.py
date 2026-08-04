import argparse
from pathlib import Path

from stable_baselines3 import PPO

from racing_grpc_env import RacingGrpcEnv


def parse_args():
    parser = argparse.ArgumentParser(description="Evaluate a saved PPO model against the Unity racing environment.")
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--episodes", type=int, default=3)
    parser.add_argument("--seed", type=int, default=123)
    parser.add_argument("--address", default="127.0.0.1:50051")
    parser.add_argument("--checkpoint-count", type=int, default=12)
    return parser.parse_args()


def main():
    args = parse_args()
    if args.episodes <= 0:
        raise ValueError("episodes must be positive.")
    if args.seed < 0:
        raise ValueError("seed must be non-negative.")
    if args.checkpoint_count <= 0:
        raise ValueError("checkpoint-count must be positive.")
    if not args.model.exists():
        raise FileNotFoundError("Model not found: " + str(args.model))

    model = PPO.load(str(args.model), device="cpu")
    env = RacingGrpcEnv(address=args.address)
    try:
        rewards = []
        checkpoints = []

        for episode in range(args.episodes):
            observation, _ = env.reset(seed=args.seed + episode)
            total_reward = 0.0
            total_rpc_seconds = 0.0
            max_progress = 0.0
            steps = 0
            reason = ""

            while True:
                action, _ = model.predict(observation, deterministic=True)
                observation, reward, terminated, truncated, info = env.step(action)
                total_reward += reward
                total_rpc_seconds += env.last_rpc_seconds
                max_progress = max(max_progress, float(observation[-1]))
                steps += 1

                if terminated or truncated:
                    reason = info.get("reason", "")
                    break

            checkpoint_count = args.checkpoint_count if reason == "lap_complete" else round(max_progress * args.checkpoint_count)
            rewards.append(total_reward)
            checkpoints.append(checkpoint_count)
            print(
                f"episode {episode + 1}: reward {total_reward:.3f}, steps {steps}, "
                f"reason {reason}, checkpoints {checkpoint_count}/{args.checkpoint_count}, "
                f"average rpc {total_rpc_seconds / steps:.4f}s"
            )

        print(
            f"average: reward {sum(rewards) / len(rewards):.3f}, "
            f"checkpoints {sum(checkpoints) / len(checkpoints):.1f}/{args.checkpoint_count}"
        )
    finally:
        env.close()


if __name__ == "__main__":
    main()
