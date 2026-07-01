# Project Architecture

This project is a reinforcement learning playground for training a drone in a
Unity physics scene. Unity provides the simulator, Gymnasium provides the Python
environment interface, Stable-Baselines3 provides the PPO implementation, and
ML-Agents provides the communication bridge between Unity and Python.

## High-Level System

```text
Unity scene                         Python RL process
-----------                         -----------------
SampleScene.unity
  |
  | physics, collisions, rewards
  v
DroneAgent.cs  <---- ML-Agents ---->  Gymnasium-compatible env
  |                                      |
  | observations                         | obs, reward, done, info
  | rewards                              v
  | terminal resets                    Stable-Baselines3 PPO
  ^                                      |
  | actions: thrust, yaw, forward, right |
  +--------------------------------------+
```

The learning problem is modeled as a continuous-control task:

- Unity owns the authoritative drone state.
- `DroneAgent.cs` converts Unity state into observations.
- Python receives observations and selects continuous actions.
- Unity applies those actions to the Rigidbody.
- Unity computes reward and episode termination.
- PPO updates the policy from collected experience.

## Main Components

| Component | Location | Responsibility |
| --- | --- | --- |
| Unity simulator | `drone-simulator/Assets/Scenes/SampleScene.unity` | Runs physics, renders the scene, contains the drone, obstacles, ground, camera, and light. |
| Agent script | `drone-simulator/Assets/Scripts/DroneAgent.cs` | Defines observations, actions, rewards, resets, termination, and heuristic keyboard control. |
| Config file | `drone-simulator/Assets/Scripts/agent.json` | Stores runtime-tunable agent parameters such as forces, hover target, bounds, reset ranges, and penalties. |
| Collision handler | `drone-simulator/Assets/Scripts/DroneCollisionHandler.cs` | Reports obstacle collisions back to the agent so the episode can end with a penalty. |
| Camera follow | `drone-simulator/Assets/Scripts/DroneFollowCamera.cs` | Keeps the camera following the drone for visual debugging. |
| Python control script | `python_control_drone.py` | Connects to the Unity Editor through ML-Agents and exercises the observation/action loop. |
| Python dependencies | `pyproject.toml` | Pins ML-Agents, Gymnasium, Stable-Baselines3, PPO dependencies, Torch, Gym, and NumPy. |

## Unity Layer

Unity is the simulator and source of truth. The drone is a Rigidbody-driven
GameObject controlled by `DroneAgent`, which inherits from `Unity.MLAgents.Agent`.

At startup, the agent loads configuration from JSON:

```csharp
string json = File.ReadAllText(configPath);
JsonUtility.FromJsonOverwrite(json, this);
```

This means values in `agent.json` overwrite matching public fields on the
`DroneAgent` instance. The file currently controls:

- force constants: `liftForce`, `moveForce`, `yawTorque`, `tiltTorque`,
  `stabilizationStrength`
- hover target: `hoverTargetX`, `hoverTargetY`, `hoverTargetZ`
- scene bounds: `min_x`, `max_x`, `min_y`, `max_y`, `min_z`, `max_z`
- episode reset ranges: `resetHorizontalRange`, `resetVerticalRange`
- terminal penalties: `resetPenalty`, `obstacleCollisionPenalty`
- flip threshold: `max_uprightness`

This keeps training parameters outside the compiled C# script, so experiments
can change the task without editing the agent code.

## Reinforcement Learning Interface

`DroneAgent` exposes the simulation as a Markov decision process.

### Observation Space

The agent emits a 14-dimensional vector:

| Slice | Size | Meaning |
| --- | ---: | --- |
| `0:3` | 3 | Vector from drone to hover target, scaled by `10`. |
| `3:6` | 3 | Rigidbody linear velocity, scaled by `10`. |
| `6:9` | 3 | Rigidbody angular velocity, scaled by `10`. |
| `9:12` | 3 | Local Euler rotation normalized to `[-1, 1]`. |
| `12` | 1 | Height above ground, scaled by `10`. |
| `13` | 1 | Uprightness, computed as `dot(transform.up, Vector3.up)`. |

This gives PPO enough information to learn where the drone is relative to the
hover target, how fast it is moving, how fast it is rotating, and whether it is
upright.

### Action Space

The policy outputs 4 continuous actions:

| Index | Name | Unity effect |
| --- | --- | --- |
| `0` | Thrust | Applies upward force with `liftForce`. |
| `1` | Yaw | Applies yaw torque around world up. |
| `2` | Forward | Applies force along the drone forward vector. |
| `3` | Right | Applies force along the drone right vector. |

Each action is clamped to `[-1, 1]` before being applied.

## Reward and Episode Design

The current task is hover control. The drone is rewarded for staying near the
configured hover target while remaining stable and upright.

Per decision, the agent adds:

- small survival reward: `+0.01`
- height error penalty
- horizontal distance penalty
- linear speed penalty
- angular speed penalty
- uprightness penalty
- extra hover bonus when position, speed, angular speed, and uprightness are all
  within tight thresholds

An episode ends when:

- the drone leaves the configured scene bounds
- the drone flies too high
- the drone flips past the configured uprightness threshold
- the collision handler reports an obstacle collision

On reset, the drone returns near its original scene position with configurable
random horizontal and vertical offsets. This gives the policy varied initial
conditions while keeping episodes inside the intended training area.

## Python Training Layer

The Python side talks to Unity through `mlagents_envs`. The current control
script connects to the running Unity Editor:

```python
env = UnityEnvironment(
    file_name=None,
    worker_id=0,
    seed=42,
    side_channels=[channel],
)
```

`file_name=None` means Python waits for the Unity Editor in Play mode. For long
training runs, the same architecture can point at a standalone Unity build
instead.

The Gymnasium boundary is the API shape used by the training stack:

```text
obs, info = env.reset()
obs, reward, terminated, truncated, info = env.step(action)
```

Stable-Baselines3 PPO then treats the Unity drone scene like any other
continuous-control environment. PPO collects rollouts, estimates advantages, and
updates a neural network policy that maps the 14-dimensional observation vector
to the 4-dimensional action vector.

## Runtime Flow

1. Open `SampleScene.unity` in Unity.
2. Press Play or start a standalone Unity build.
3. Python connects through the ML-Agents communicator.
4. Unity calls `DroneAgent.Initialize()`.
5. `DroneAgent` loads `agent.json`.
6. Unity begins an episode with `OnEpisodeBegin()`.
7. Unity sends observations to Python.
8. PPO or a test controller returns actions.
9. Unity applies forces and torques to the Rigidbody.
10. Unity calculates reward and checks terminal conditions.
11. On terminal state, Unity ends the episode and resets the drone.

## Why This Architecture

Unity is a good fit for the simulator because it already provides rigidbody
physics, collisions, scene editing, camera debugging, and visual inspection.

Gymnasium is a good fit for the Python boundary because it gives the project the
standard RL interface: reset, step, observation space, action space, reward, and
termination.

Stable-Baselines3 PPO is a good fit for this drone task because the policy must
learn smooth continuous actions under noisy, unstable physics. PPO is robust for
continuous-control baselines and works well before adding more advanced
algorithms.

The JSON config keeps environment design fast. The C# script defines the rules,
while `agent.json` lets experiments change the task parameters without
recompiling Unity scripts.

## Current Status

Implemented:

- Unity drone scene with Rigidbody-based flight.
- ML-Agents `DroneAgent` with 14 observations and 4 continuous actions.
- Hover-target reward shaping and terminal conditions.
- Randomized episode reset around the scene start position.
- Obstacle collision penalty path.
- JSON-backed runtime configuration.
- Python connection to the Unity Editor through `mlagents_envs`.
- Dependency pins for Gymnasium, Stable-Baselines3, PPO, Torch, ML-Agents, Gym,
  and NumPy.

Next architecture step:

- Add a dedicated Gymnasium wrapper around the ML-Agents connection, then pass
  that wrapper directly into `stable_baselines3.PPO`.
