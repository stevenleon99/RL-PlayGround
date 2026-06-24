import numpy as np

from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)


OBS_SCALE = 10.0


def decode_drone_observation(obs: np.ndarray) -> dict:
    """
    Decode the 14-D observation vector from your DroneAgent.cs.
    """
    return {
        "to_target": obs[0:3] * 10.0,
        "linear_velocity": obs[3:6] * 10.0,
        "angular_velocity": obs[6:9] * 10.0,
        "local_euler_deg": obs[9:12] * 180.0,
        "height": obs[12] * 10.0,
        "uprightness": obs[13],
    }


def main():
    channel = EngineConfigurationChannel()
    channel.set_configuration_parameters(time_scale=1.0)

    env = UnityEnvironment(
        file_name=None,      # connect to Unity Editor
        worker_id=0,
        seed=42,
        side_channels=[channel],
    )

    try:
        env.reset()

        behavior_name = list(env.behavior_specs.keys())[0]
        spec = env.behavior_specs[behavior_name]

        print("Behavior name:", behavior_name)
        print("Observation specs:", spec.observation_specs)
        print("Action spec:", spec.action_spec)

        assert spec.action_spec.num_continuous_actions == 4, (
            "Your DroneAgent should have 4 continuous actions: "
            "thrust, yaw, pitch, roll."
        )

        for step in range(300):
            decision_steps, terminal_steps = env.get_steps(behavior_name)

            if len(decision_steps) == 0:
                env.step()
                continue

            n_agents = len(decision_steps)

            # Your C# action order:
            # c[0] = thrust
            # c[1] = yaw
            # c[2] = pitch
            # c[3] = roll

            action = np.array([
                0.65,   # thrust
                0.0,    # yaw
                0.0,    # pitch
                0.0,    # roll
            ], dtype=np.float32)

            actions = np.tile(action, (n_agents, 1))

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

                print(f"\nStep {step}")
                print("Reward:", reward)
                print("State:", state)

            if len(terminal_steps) > 0:
                obs = terminal_steps.obs[0][0]
                reward = terminal_steps.reward[0]
                state = decode_drone_observation(obs)

                print("\nEpisode ended")
                print("Final reward:", reward)
                print("Final state:", state)

                env.reset()

    finally:
        env.close()


if __name__ == "__main__":
    main()