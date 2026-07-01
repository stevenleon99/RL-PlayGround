import argparse
from pathlib import Path

import gymnasium as gym
import numpy as np
from gymnasium import spaces
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)
from stable_baselines3 import DQN
from stable_baselines3.common.monitor import Monitor


def get_continuous_action_count(action_spec) -> int:
    return action_spec.continuous_size


def build_action_table(
    hover_thrust: float,
    thrust_delta: float,
    move_amount: float,
    yaw_amount: float,
) -> np.ndarray:
    """
    Map one DQN discrete action into DroneAgent's continuous action vector:
    [thrust, yaw, forward, right].
    """
    actions = np.array(
        [
            [hover_thrust, 0.0, 0.0, 0.0],
            [hover_thrust + thrust_delta, 0.0, 0.0, 0.0],
            [hover_thrust - thrust_delta, 0.0, 0.0, 0.0],
            [hover_thrust, 0.0, move_amount, 0.0],
            [hover_thrust, 0.0, -move_amount, 0.0],
            [hover_thrust, 0.0, 0.0, move_amount],
            [hover_thrust, 0.0, 0.0, -move_amount],
            [hover_thrust, yaw_amount, 0.0, 0.0],
            [hover_thrust, -yaw_amount, 0.0, 0.0],
            [hover_thrust + thrust_delta, 0.0, move_amount, 0.0],
            [hover_thrust + thrust_delta, 0.0, -move_amount, 0.0],
            [hover_thrust + thrust_delta, 0.0, 0.0, move_amount],
            [hover_thrust + thrust_delta, 0.0, 0.0, -move_amount],
        ],
        dtype=np.float32,
    )
    return np.clip(actions, -1.0, 1.0)


class UnityDroneDqnEnv(gym.Env):
    metadata = {"render_modes": []}

    def __init__(
        self,
        unity_file: str | None,
        worker_id: int,
        seed: int,
        time_scale: float,
        timeout_wait: int,
        behavior_name: str,
        max_episode_steps: int,
        action_table: np.ndarray,
    ) -> None:
        super().__init__()
        self.action_table = action_table
        self.max_episode_steps = max_episode_steps
        self.episode_steps = 0

        channel = EngineConfigurationChannel()
        channel.set_configuration_parameters(time_scale=time_scale)

        print(
            "Creating UnityEnvironment. If using the Unity Editor, press Play while this waits.",
            flush=True,
        )
        self.unity_env = UnityEnvironment(
            file_name=unity_file,
            worker_id=worker_id,
            seed=seed,
            timeout_wait=timeout_wait,
            side_channels=[channel],
        )

        print("Waiting for Unity connection at env.reset()...", flush=True)
        self.unity_env.reset()
        print("Connected to Unity.", flush=True)

        print("Available Unity behaviors:", flush=True)
        for name, behavior_spec in self.unity_env.behavior_specs.items():
            print(f"  {name}: {behavior_spec.action_spec}", flush=True)

        if behavior_name not in self.unity_env.behavior_specs:
            behavior_names = ", ".join(self.unity_env.behavior_specs.keys())
            raise ValueError(
                f"Behavior '{behavior_name}' was not found. Available behaviors: {behavior_names}"
            )

        self.behavior_name = behavior_name
        spec = self.unity_env.behavior_specs[self.behavior_name]
        self.action_spec = spec.action_spec

        obs_size = spec.observation_specs[0].shape[0]
        continuous_size = get_continuous_action_count(self.action_spec)
        print("Behavior name:", self.behavior_name, flush=True)
        print("Observation specs:", spec.observation_specs, flush=True)
        print("Action spec:", self.action_spec, flush=True)

        if continuous_size != 4:
            raise ValueError(
                "Expected DroneAgent to expose 4 continuous actions, "
                f"got {continuous_size}. In Unity, select the Drone and set "
                "Behavior Parameters > Actions to Continuous with Size 4, "
                "then make sure Behavior Name is Drone and Behavior Type is Default."
            )

        self.observation_space = spaces.Box(
            low=-np.inf,
            high=np.inf,
            shape=(obs_size,),
            dtype=np.float32,
        )
        self.action_space = spaces.Discrete(len(action_table))
        self._last_obs = self._read_current_observation()

    def reset(self, *, seed: int | None = None, options: dict | None = None):
        super().reset(seed=seed)
        self.unity_env.reset()
        self.episode_steps = 0
        self._last_obs = self._read_current_observation()
        return self._last_obs.copy(), {}

    def step(self, action: int):
        decision_steps, terminal_steps = self.unity_env.get_steps(self.behavior_name)
        if len(terminal_steps) > 0:
            obs = terminal_steps.obs[0][0].astype(np.float32)
            self._last_obs = obs
            return obs.copy(), 0.0, True, False, {"unity_behavior": self.behavior_name}

        if len(decision_steps) == 0:
            self._last_obs = self._read_current_observation()

        continuous_action = self.action_table[int(action)].reshape(1, 4)
        discrete_action = np.zeros((1, self.action_spec.discrete_size), dtype=np.int32)
        action_tuple = ActionTuple(
            continuous=continuous_action,
            discrete=discrete_action,
        )
        self.unity_env.set_actions(self.behavior_name, action_tuple)

        reward = 0.0
        terminated = False
        obs = self._last_obs

        while True:
            self.unity_env.step()
            decision_steps, terminal_steps = self.unity_env.get_steps(self.behavior_name)

            if len(terminal_steps) > 0:
                obs = terminal_steps.obs[0][0].astype(np.float32)
                reward = float(terminal_steps.reward[0])
                terminated = True
                break

            if len(decision_steps) > 0:
                obs = decision_steps.obs[0][0].astype(np.float32)
                reward = float(decision_steps.reward[0])
                break

        self.episode_steps += 1
        truncated = self.episode_steps >= self.max_episode_steps
        self._last_obs = obs

        info = {
            "unity_behavior": self.behavior_name,
            "continuous_action": continuous_action[0].copy(),
        }
        return obs.copy(), reward, terminated, truncated, info

    def close(self) -> None:
        self.unity_env.close()

    def _read_current_observation(self) -> np.ndarray:
        while True:
            decision_steps, terminal_steps = self.unity_env.get_steps(self.behavior_name)
            if len(decision_steps) > 0:
                return decision_steps.obs[0][0].astype(np.float32)
            if len(terminal_steps) > 0:
                return terminal_steps.obs[0][0].astype(np.float32)
            self.unity_env.step()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Train a discrete DQN drone policy.")
    parser.add_argument("--unity-file", default=None)
    parser.add_argument("--worker-id", type=int, default=0)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--time-scale", type=float, default=1.0)
    parser.add_argument("--timeout-wait", type=int, default=120)
    parser.add_argument("--behavior-name", default="Drone")
    parser.add_argument("--total-timesteps", type=int, default=100_000)
    parser.add_argument("--max-episode-steps", type=int, default=1_000)
    parser.add_argument("--learning-rate", type=float, default=1e-4)
    parser.add_argument("--buffer-size", type=int, default=50_000)
    parser.add_argument("--learning-starts", type=int, default=1_000)
    parser.add_argument("--batch-size", type=int, default=64)
    parser.add_argument("--tau", type=float, default=1.0)
    parser.add_argument("--gamma", type=float, default=0.99)
    parser.add_argument("--train-freq", type=int, default=4)
    parser.add_argument("--gradient-steps", type=int, default=1)
    parser.add_argument("--target-update-interval", type=int, default=1_000)
    parser.add_argument("--exploration-fraction", type=float, default=0.2)
    parser.add_argument("--exploration-initial-eps", type=float, default=1.0)
    parser.add_argument("--exploration-final-eps", type=float, default=0.05)
    parser.add_argument("--hover-thrust", type=float, default=9.81 / 15.0)
    parser.add_argument("--thrust-delta", type=float, default=0.25)
    parser.add_argument("--move-amount", type=float, default=0.6)
    parser.add_argument("--yaw-amount", type=float, default=0.5)
    parser.add_argument("--net-arch", default="256,256")
    parser.add_argument("--device", default="auto")
    parser.add_argument("--log-dir", default="runs/dqn")
    parser.add_argument("--model-path", default="models/drone_dqn")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    action_table = build_action_table(
        hover_thrust=args.hover_thrust,
        thrust_delta=args.thrust_delta,
        move_amount=args.move_amount,
        yaw_amount=args.yaw_amount,
    )
    net_arch = [int(width) for width in args.net_arch.split(",") if width.strip()]

    Path(args.log_dir).mkdir(parents=True, exist_ok=True)
    Path(args.model_path).parent.mkdir(parents=True, exist_ok=True)

    env = Monitor(
        UnityDroneDqnEnv(
            unity_file=args.unity_file,
            worker_id=args.worker_id,
            seed=args.seed,
            time_scale=args.time_scale,
            timeout_wait=args.timeout_wait,
            behavior_name=args.behavior_name,
            max_episode_steps=args.max_episode_steps,
            action_table=action_table,
        ),
        filename=str(Path(args.log_dir) / "monitor.csv"),
    )

    try:
        model = DQN(
            "MlpPolicy",
            env,
            learning_rate=args.learning_rate,
            buffer_size=args.buffer_size,
            learning_starts=args.learning_starts,
            batch_size=args.batch_size,
            tau=args.tau,
            gamma=args.gamma,
            train_freq=args.train_freq,
            gradient_steps=args.gradient_steps,
            target_update_interval=args.target_update_interval,
            exploration_fraction=args.exploration_fraction,
            exploration_initial_eps=args.exploration_initial_eps,
            exploration_final_eps=args.exploration_final_eps,
            policy_kwargs={"net_arch": net_arch},
            tensorboard_log=args.log_dir,
            device=args.device,
            verbose=1,
            seed=args.seed,
        )
        model.learn(total_timesteps=args.total_timesteps)
        model.save(args.model_path)
        print(f"Saved DQN model to {args.model_path}.zip", flush=True)
    finally:
        env.close()


if __name__ == "__main__":
    main()
