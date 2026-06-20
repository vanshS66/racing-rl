from pathlib import Path
import sys

import grpc


PROJECT_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(PROJECT_ROOT / "python" / "generated"))

import racing_rl_pb2
import racing_rl_pb2_grpc


def main():
    with grpc.insecure_channel("127.0.0.1:50051") as channel:
        stub = racing_rl_pb2_grpc.RacingEnvironmentStub(channel)
        response = stub.Health(racing_rl_pb2.HealthRequest(), timeout=5)

    print("service version:", response.service_version)
    print("observation size:", response.observation_size)
    print("action size:", response.action_size)


if __name__ == "__main__":
    main()
