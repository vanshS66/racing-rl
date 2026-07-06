from gymnasium.utils.env_checker import check_env

from racing_grpc_env import RacingGrpcEnv


def main():
    env = RacingGrpcEnv()
    try:
        check_env(env, skip_render_check=True)

        observation, info = env.reset(seed=123)
        print("reset shape:", observation.shape)
        print("reset info:", info)

        for _ in range(10):
            observation, reward, terminated, truncated, info = env.step(env.action_space.sample())
            print("step", info["decision_step"], "reward", reward, "terminated", terminated, "truncated", truncated)
            if terminated or truncated:
                observation, info = env.reset()
    finally:
        env.close()


if __name__ == "__main__":
    main()
