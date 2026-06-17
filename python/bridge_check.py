from pathlib import Path
import sys

import grpc


PROJECT_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(PROJECT_ROOT / "python" / "generated"))

import racing_rl_pb2
import racing_rl_pb2_grpc


def observation_values(observation):
    return [*observation.ray_distances, observation.forward_speed, observation.lateral_speed,
            observation.slip_angle, observation.yaw_rate, observation.progress]


def main():
    with grpc.insecure_channel("127.0.0.1:50051") as channel:
        stub = racing_rl_pb2_grpc.RacingEnvironmentStub(channel)
        reset = stub.Reset(racing_rl_pb2.ResetRequest(), timeout=5)
        print("reset observation:", observation_values(reset.observation))

        action = racing_rl_pb2.Action(steering=0.0, throttle=0.25, brake=0.0)
        for _ in range(5):
            step = stub.Step(racing_rl_pb2.StepRequest(action=action), timeout=5)
            print("step", step.info["decision_step"], "reward", step.reward, "speed", step.observation.forward_speed)


if __name__ == "__main__":
    main()
