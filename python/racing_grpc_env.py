from pathlib import Path
import sys
from time import perf_counter

import grpc
import gymnasium as gym
import numpy as np


GENERATED_ROOT = Path(__file__).resolve().parent / "generated"
sys.path.insert(0, str(GENERATED_ROOT))

import racing_rl_pb2
import racing_rl_pb2_grpc


class RacingGrpcEnv(gym.Env):
    metadata = {"render_modes": []}

    def __init__(self, address="127.0.0.1:50051", timeout_seconds=5):
        self.timeout_seconds = timeout_seconds
        self.channel = grpc.insecure_channel(address)
        self.stub = racing_rl_pb2_grpc.RacingEnvironmentStub(self.channel)

        try:
            health = self.stub.Health(racing_rl_pb2.HealthRequest(), timeout=self.timeout_seconds)
        except grpc.RpcError as error:
            self.close()
            raise ConnectionError(
                "Could not reach the Unity gRPC server at " + address + ". Open main_training and enter Play mode first."
            ) from error
        if health.action_size != 3:
            self.close()
            raise RuntimeError("Unity reported an unsupported action size: " + str(health.action_size))
        if health.observation_size < 5:
            self.close()
            raise RuntimeError("Unity reported an invalid observation size: " + str(health.observation_size))

        self.service_version = health.service_version
        ray_count = health.observation_size - 5
        observation_low = np.array([0.0] * ray_count + [-50.0, -50.0, -np.pi, -20.0, 0.0], dtype=np.float32)
        observation_high = np.array([1.0] * ray_count + [50.0, 50.0, np.pi, 20.0, 1.0], dtype=np.float32)
        self.observation_space = gym.spaces.Box(observation_low, observation_high, dtype=np.float32)
        self.action_space = gym.spaces.Box(
            low=np.array([-1.0, 0.0, 0.0], dtype=np.float32),
            high=np.array([1.0, 1.0, 1.0], dtype=np.float32),
            dtype=np.float32,
        )

    def reset(self, *, seed=None, options=None):
        super().reset(seed=seed)
        if seed is not None and seed < 0:
            raise ValueError("Gymnasium reset seeds must be non-negative.")

        request = racing_rl_pb2.ResetRequest(seed=0 if seed is None else seed)
        response = self.stub.Reset(request, timeout=self.timeout_seconds)
        return self._observation_from_proto(response.observation), dict(response.info)

    def step(self, action):
        action = np.asarray(action, dtype=np.float32)
        if action.shape != self.action_space.shape or not self.action_space.contains(action):
            raise ValueError("Action must be a float32 [steering, throttle, brake] value inside " + str(self.action_space))

        request = racing_rl_pb2.StepRequest(
            action=racing_rl_pb2.Action(
                steering=float(action[0]),
                throttle=float(action[1]),
                brake=float(action[2]),
            )
        )
        started_at = perf_counter()
        response = self.stub.Step(request, timeout=self.timeout_seconds)
        info = dict(response.info)
        info["rpc_seconds"] = perf_counter() - started_at
        return (
            self._observation_from_proto(response.observation),
            float(response.reward),
            bool(response.terminated),
            bool(response.truncated),
            info,
        )

    def close(self):
        if self.channel is not None:
            self.channel.close()
            self.channel = None

    def _observation_from_proto(self, observation):
        values = np.asarray(
            [
                *observation.ray_distances,
                observation.forward_speed,
                observation.lateral_speed,
                observation.slip_angle,
                observation.yaw_rate,
                observation.progress,
            ],
            dtype=np.float32,
        )
        if not self.observation_space.contains(values):
            raise ValueError("Unity returned an observation outside the declared observation space.")
        return values
