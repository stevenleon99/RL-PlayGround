import numpy as np

from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)


OBS_SCALE = 10.0
HOVER_THRUST = 9.81 / 15.0
HOVER_TARGET_X = 5.0
HOVER_TARGET_Y = 8.0
HOVER_TARGET_Z = 5.0
MAX_MOVE_ACTION = 1.0
LAUNCH_STEPS = 45


def decode_drone_observation(obs: np.ndarray) -> dict:
    """
    Decode the 14-D observation vector from your DroneAgent.cs.
    """
    height = obs[12] * 10.0
    to_hover_point = obs[0:3] * 10.0

    return {
        "to_hover_point": to_hover_point,
        "linear_velocity": obs[3:6] * 10.0,
        "angular_velocity": obs[6:9] * 10.0,
        "local_euler_deg": obs[9:12] * 180.0,
        "height": height,
        "hover_error_x": to_hover_point[0],
        "unity_hover_target_y": height + to_hover_point[1],
        "hover_error_z": to_hover_point[2],
        "uprightness": obs[13],
    }


def main():
    print("Starting Python drone controller...", flush=True)

    channel = EngineConfigurationChannel()
    channel.set_configuration_parameters(time_scale=1.0)

    print("Creating UnityEnvironment. Make sure Unity is already in Play mode.", flush=True)
    env = UnityEnvironment(
        file_name=None,      # connect to Unity Editor
        worker_id=0,
        seed=42,
        side_channels=[channel],
    )

    try:
        print("Waiting for Unity Editor connection at env.reset()...", flush=True)
        env.reset()
        print("Connected to Unity.", flush=True)

        behavior_name = list(env.behavior_specs.keys())[0]
        spec = env.behavior_specs[behavior_name]

        print("Behavior name:", behavior_name, flush=True)
        print("Observation specs:", spec.observation_specs, flush=True)
        print("Action spec:", spec.action_spec, flush=True)

        continuous_action_count = getattr(
            spec.action_spec,
            "num_continuous_actions",
            spec.action_spec.continuous_size,
        )

        assert continuous_action_count == 4, (
            "Your DroneAgent should have 4 continuous actions: "
            "thrust, yaw, forward, right."
        )

        for step in range(1000):
            decision_steps, terminal_steps = env.get_steps(behavior_name)

            if len(decision_steps) == 0:
                env.step()
                continue

            n_agents = len(decision_steps)

            # DroneAgent.cs action order:
            # c[0] = thrust
            # c[1] = yaw
            # c[2] = forward
            # c[3] = right
            obs_batch = decision_steps.obs[0]
            actions = np.zeros((n_agents, 4), dtype=np.float32)

            for i, obs in enumerate(obs_batch):
                state = decode_drone_observation(obs)
                to_hover_point = state["to_hover_point"]
                velocity = state["linear_velocity"]
                height = state["height"]

                thrust = HOVER_THRUST + 0.25 * to_hover_point[1] - 0.12 * velocity[1]
                forward = 0.35 * to_hover_point[2] - 0.10 * velocity[2]
                right = 0.35 * to_hover_point[0] - 0.10 * velocity[0]

                if step < LAUNCH_STEPS and height < HOVER_TARGET_Y - 0.08:
                    thrust = max(thrust, 1.0)
                    forward += 0.65
                    right += 0.35

                if height >= HOVER_TARGET_Y:
                    upward_velocity = max(0.0, velocity[1])
                    overshoot = height - HOVER_TARGET_Y
                    thrust = min(thrust, HOVER_THRUST - 0.20 * overshoot - 0.12 * upward_velocity)

                actions[i] = np.clip(
                    [thrust, 0.0, forward, right],
                    [-1.0, -1.0, -MAX_MOVE_ACTION, -MAX_MOVE_ACTION],
                    [1.0, 1.0, MAX_MOVE_ACTION, MAX_MOVE_ACTION],
                )

            action_tuple = ActionTuple(
                continuous=actions,
                discrete=np.empty((n_agents, 0), dtype=np.int32),
            )

            env.set_actions(behavior_name, action_tuple)
            env.step()

            decision_steps, terminal_steps = env.get_steps(behavior_name)

            if len(decision_steps) > 0:
                obs = decision_steps.obs[0][0]
                reward = decision_steps.reward[0]
                state = decode_drone_observation(obs)

                print(f"\nStep {step}", flush=True)
                print("Reward:", reward, flush=True)
                print("State:", state, flush=True)

            if len(terminal_steps) > 0:
                obs = terminal_steps.obs[0][0]
                reward = terminal_steps.reward[0]
                state = decode_drone_observation(obs)

                print("\nEpisode ended", flush=True)
                print("Final reward:", reward, flush=True)
                print("Final state:", state, flush=True)
                print(
                    "Likely reset reason:",
                    "crash/flip"
                    if state["height"] < 0.3 or state["uprightness"] < 0.0
                    else "too high, drifted too far, or max steps",
                    flush=True,
                )

                env.reset()

    finally:
        env.close()


if __name__ == "__main__":
    main()
