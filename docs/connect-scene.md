# Connecting the Drone Scene to a Reinforcement Learning Loop

This guide extends [`setup-unity.md`](./setup-unity.md) and [`drone-scene.md`](./drone-scene.md).
It walks through, end to end, how to:

1. Put a learned policy in the pilot's seat of the existing drone scene.
2. Expose that scene to Python as a **gymnasium** environment.
3. Train the policy with **PPO** to fly the drone to the existing `target` object.

Prerequisite: complete the validation checklist in [`drone-scene.md`](./drone-scene.md) first —
the scene must fly correctly under keyboard control before adding ML on top.

## 1. Architecture

```
   Unity Editor (C#)                       Python (training)
   ─────────────────                       ────────────────
   DroneAgent : Agent          <========>  mlagents_envs.UnityEnv
   ├─ CollectObservations      socket       (implements gymnasium.Env)
   ├─ OnActionReceived              │              │
   └─ Heuristic (manual test)       │              │
                                   │              v
                                   │       ┌──────────────┐
                                   │       │  PPO policy  │
                                   │       │  (PyTorch)   │
                                   │       └──────────────┘
                                   │
   └─ per Decision Period ──┘
```

Per decision step (every `Decision Period` physics frames):

1. Unity calls `CollectObservations` → vector sent over a local socket.
2. Python's policy maps observation → action vector.
3. Unity applies the action in `OnActionReceived`, accumulates reward, repeats.

The socket is the **External communicator** built into the `com.unity.ml-agents`
package — no custom networking code required.

## 2. Prerequisites

| Component | Version |
| --- | --- |
| Unity Editor | 6000.5.0f1 (already installed, see `drone-scene.md`) |
| Python | `>=3.10.1,<=3.10.12` (pinned by `pyproject.toml`) |
| `com.unity.ml-agents` | 4.0.3 |
| `mlagents`, `mlagents-envs` | `==1.1.0` |
| `gymnasium` | `>=0.29.1,<1.3.0` |
| `stable-baselines3` | `==2.7.0` |
| `torch` | `>=2.3,<3.0` |
| `gym` | `==0.26.2` (pinned to dodge the `gym==0.21` build failure) |
| `numpy` | `>=1.23.5,<1.24.0` (required by `mlagents`) |

All Python pins live in [`pyproject.toml`](../pyproject.toml) at the repo root.
Install from there rather than re-typing versions:

```bash
# from the repo root (D:\claude_project\rl-playground)
python -m venv .venv
source .venv/Scripts/activate    # Windows Git Bash; use .venv\Scripts\activate on cmd
pip install --upgrade pip
pip install -e .                 # resolves everything pyproject.toml lists
```

> Don't bump `mlagents`/`mlagents-envs` past 1.1.0 without also rechecking the
> `gym` / `numpy` / `torch` bounds — those pins exist to dodge known build and
> ABI breakages (see comments in `pyproject.toml`).

## 3. Install ML-Agents in Unity

Edit `drone-simulator/Packages/manifest.json` and add the dependency:

```json
{
  "dependencies": {
    "com.unity.ml-agents": "4.0.3",
    ...
  }
}
```

Save the file. Back in Unity, the Package Manager will resolve and recompile.
Confirm `Window → Package Manager → ML Agents` shows the version.

> The existing `com.coplaydev.unity-mcp` package is an editor-automation MCP
> server and is unrelated to RL training. Leave it alone.

## 4. Write `DroneAgent.cs`

Create `drone-simulator/Assets/Scripts/DroneAgent.cs` (no space in the name —
deliberately distinct from the existing `Drone Controller.cs`):

```csharp
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class DroneAgent : Agent
{
    [Header("References")]
    [SerializeField] private Transform target;

    [Header("Force constants — mirror DroneController so behavior matches")]
    public float liftForce = 15f;
    public float moveForce = 8f;            // kept for parity; not used by the 4-D action mapping
    public float yawTorque = 3f;
    public float tiltTorque = 2f;
    public float stabilizationStrength = 1.5f;

    [Header("Episode")]
    public float reachThreshold = 1.5f;
    public float groundY = 0.3f;

    private Rigidbody rb;
    private Vector3 startPos;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        startPos = transform.localPosition;
    }

    public override void OnEpisodeBegin()
    {
        transform.localPosition = startPos;
        transform.localRotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // 14-D observation vector.
    public override void CollectObservations(VectorSensor sensor)
    {
        // Relative vector to target (3) — normalized by a rough scene scale.
        Vector3 toTarget = (target != null ? target.position : transform.position) - transform.position;
        sensor.AddObservation(toTarget / 10f);

        // Linear velocity (3) and angular velocity (3).
        sensor.AddObservation(rb.linearVelocity / 10f);
        sensor.AddObservation(rb.angularVelocity / 10f);

        // Local euler rotation, normalized to [-1, 1] (3).
        Vector3 e = transform.localEulerAngles;
        sensor.AddObservation(new Vector3(
            NormalizeAngle(e.x) / 180f,
            NormalizeAngle(e.y) / 180f,
            NormalizeAngle(e.z) / 180f));

        // Height above ground (1).
        sensor.AddObservation(transform.position.y / 10f);

        // Upright-ness: 1 = level, -1 = upside-down (1).
        sensor.AddObservation(Vector3.Dot(transform.up, Vector3.up));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var c = actions.ContinuousActions;
        float thrust = Mathf.Clamp(c[0], -1f, 1f);
        float yaw    = Mathf.Clamp(c[1], -1f, 1f);
        float pitch  = Mathf.Clamp(c[2], -1f, 1f);
        float roll   = Mathf.Clamp(c[3], -1f, 1f);

        rb.AddForce(Vector3.up * thrust * liftForce, ForceMode.Force);
        rb.AddTorque(Vector3.up * yaw * yawTorque, ForceMode.Force);
        rb.AddTorque(transform.right * pitch * tiltTorque, ForceMode.Force);
        rb.AddTorque(transform.forward * -roll * tiltTorque, ForceMode.Force);

        StabilizeDrone();

        // --- Rewards (fly-to-target) --------------------------------------
        if (target != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            AddReward(-dist * 0.001f);                       // dense progress
            if (dist < reachThreshold)
            {
                AddReward(10f);                              // sparse success bonus
                EndEpisode();
            }
        }

        if (transform.position.y < groundY ||
            Vector3.Dot(transform.up, Vector3.up) < 0f)
        {
            AddReward(-5f);                                  // crash / flip penalty
            EndEpisode();
        }

        AddReward(-0.0001f);                                 // tiny step cost
    }

    // Manual control for sanity-checking the agent's physics.
    // Set Behavior Type = "Heuristic Only" in the Inspector to use.
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var c = actionsOut.ContinuousActions;
        var kb = Keyboard.current;
        if (kb == null) return;

        c[0] = kb.spaceKey.isPressed ? 1f : (kb.leftCtrlKey.isPressed ? -0.5f : 0f);
        c[1] = (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);
        c[2] = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
        c[3] = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
    }

    // --- Helpers (lifted from DroneController so stabilization is identical) ---
    private void StabilizeDrone()
    {
        Vector3 rotation = transform.rotation.eulerAngles;
        float pitch = NormalizeAngle(rotation.x);
        float roll  = NormalizeAngle(rotation.z);
        Vector3 correction = new Vector3(-pitch, 0f, -roll) * stabilizationStrength;
        rb.AddRelativeTorque(correction, ForceMode.Force);
    }

    private float NormalizeAngle(float angle)
    {
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}
```

### Why this shape

| Choice | Reason |
| --- | --- |
| Continuous 4-D action | Thrust + 3 torques maps cleanly onto the existing physics in `Drone Controller.cs`. Discrete branches lose hover resolution. |
| 14-D observation | Enough signal for "where is the target relative to me" + "am I about to crash" without padding. |
| Dense reward `−dist·0.001` | PPO needs shaping signal; the bare `+10` on success is too sparse for a 6-DoF body. |
| Crash penalty on flip | Without it, the policy learns to dive into the ground for `-dist` reward. |
| `linearVelocity` (not `velocity`) | `Rigidbody.velocity` is deprecated in Unity 6; `linearVelocity` is the replacement. |

## 5. Wire the agent into the scene

Open `Assets/Scenes/SampleScene.unity`, select the **Drone** GameObject, then:

1. **Remove** the existing `Drone Controller` component.
   Both scripts write forces to the same Rigidbody; running both doubles every
   force and the drone accelerates uncontrollably. (You can disable instead of
   remove if you want to swap back for non-RL testing.)
2. **Add Component → ML Agents → Behavior Parameters**:
   - **Behavior Name**: `Drone` (must match the YAML key in §7)
   - **Vector Observation → Space Size**: `14`
   - **Actions → Space Type**: `Continuous`
   - **Actions → Continuous Actions → Branch 0 Size**: `4`
   - **Behavior Type**: `Default` (training + inference) or `Heuristic Only` (manual test)
3. **Add Component → ML Agents → Decision Requester**:
   - **Decision Period**: `5`
   - **Take Actions Between Decisions**: ✓ (keeps forces applied every frame)
4. **Add Component → Drone Agent** (the script from §4):
   - Drag the **target** GameObject onto the `Target` field.
5. Confirm the final Inspector state:

| Component | Present |
| --- | --- |
| Rigidbody | yes (already there) |
| Behavior Parameters | yes |
| Decision Requester | yes |
| Drone Agent (target assigned) | yes |
| ~~Drone Controller~~ | removed |

Save the scene. Press **Play** with Behavior Type = `Heuristic Only` and fly
with the same keys as before — this proves the agent's physics is correct
before introducing a policy.

## 6. Expose the scene as a gym env

In Python:

```python
from mlagents_envs.environment import UnityEnv
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)

channel = EngineConfigurationChannel()
channel.set_configuration_parameters(time_scale=20.0)   # 20x physics speed during training

env = UnityEnv(
    file_name=None,        # None -> connect to the running Unity Editor in Play mode
    worker_id=0,           # bump per concurrent training process
    seed=42,
    side_channels=[channel],
)

obs, info = env.reset()
print("observation space:", env.observation_space)
print("action space:     ", env.action_space)

# Random-policy smoke test — should run 100 steps without raising.
for _ in range(100):
    obs, reward, terminated, truncated, info = env.step(env.action_space.sample())
    if terminated or truncated:
        obs, info = env.reset()

env.close()
```

`UnityEnv` is a `gymnasium.Env` — `step()` returns the 5-tuple
`(obs, reward, terminated, truncated, info)` per the modern gym API.

> Workflow: open Unity, load `SampleScene.unity`, **then** run the Python
> script, **then** press Play in Unity. Order matters — Python waits for the
> editor to start broadcasting.

## 7. Train with PPO — two paths

### Path A: `mlagents-learn` (recommended for the first run)

Create `drone-simulator/config/ppo/drone.yaml`:

```yaml
behaviors:
  Drone:                                    # must match Behavior Parameters -> Behavior Name
    trainer_type: ppo
    hyperparameters:
      batch_size: 128
      buffer_size: 2048
      learning_rate: 3.0e-4
      beta: 5.0e-4                          # entropy regularization
      epsilon: 0.2                          # PPO clip
      lambd: 0.95                           # GAE
      num_epoch: 3
    network_settings:
      num_layers: 2
      hidden_units: 128
      normalize: false
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
    max_steps: 500000
    time_horizon: 64
    summary_freq: 10000
    keep_checkpoints: 5
    checkpoint_interval: 50000
    threaded: true
```

Run:

```bash
mlagents-learn config/ppo/drone.yaml --run-id=drone_v1
```

The CLI prints *"Start training by pressing the Play button in the Unity Editor"*.
Press Play. TensorBoard monitoring:

```bash
tensorboard --logdir results
```

Output: `results/drone_v1/Drone-<time>.onnx` — the trained policy.

### Path B: Stable Baselines3 (for non-default algorithms)

```python
from stable_baselines3 import PPO

# env is the UnityEnv instance from section 6
model = PPO("MlpPolicy", env, verbose=1, n_steps=2048, batch_size=128, learning_rate=3e-4)
model.learn(total_timesteps=500_000)
model.save("drone_ppo")
```

> SB3 needs either `file_name=None` (running editor) or a standalone build
> (`file_name="path/to/DroneBuild.exe"`). For long training runs prefer a
> build: `File -> Build And Run` with Windows x86_64 target, then point
> `UnityEnv` at the executable.

## 8. Watch the trained policy

**Path A** (`mlagents-learn`):

1. Stop training (Ctrl+C in the CLI, or wait for `max_steps`).
2. Select **Drone -> Behavior Parameters -> Model** and drag the generated
   `.onnx` file from `results/drone_v1/` into the slot.
3. Set **Behavior Type** = `Default`.
4. Press Play. The drone should fly toward `target` without keyboard input.

**Path B** (SB3):

```python
model = PPO.load("drone_ppo")
obs, info = env.reset()
for _ in range(1000):
    action, _ = model.predict(obs, deterministic=True)
    obs, reward, terminated, truncated, info = env.step(action)
    if terminated or truncated:
        obs, info = env.reset()
```

Optional: restore the `DroneFollowCamera` target assignment (still on the
Main Camera) for cinematic third-person viewing of the trained flight.

## 9. Verification checklist

- [ ] `manifest.json` resolves with `com.unity.ml-agents` 4.0.3 present in Package Manager.
- [ ] Drone GameObject has `Rigidbody`, `Behavior Parameters`, `Decision Requester`, `Drone Agent`, and **no** `Drone Controller`.
- [ ] `Behavior Parameters` shows Space Size = 14, Continuous Branch 0 Size = 4.
- [ ] Behavior Type = `Heuristic Only` -> keyboard flies the drone (sanity check on agent physics).
- [ ] `pip list | grep mlagents` returns the 1.1.0 line (per `pyproject.toml`).
- [ ] `UnityEnv(file_name=None)` connects to the running editor without error.
- [ ] `env.observation_space` shape matches the 14-D vector from `CollectObservations`.
- [ ] Random policy: `env.step(env.action_space.sample())` runs for 100 steps without raising.
- [ ] `mlagents-learn` TensorBoard shows the reward curve trending upward over the first ~50k steps.
- [ ] Loaded `.onnx` model: drone reaches `target` within ~10 s of Play.

## 10. Common pitfalls

| Symptom | Cause | Fix |
| --- | --- | --- |
| Drone accelerates uncontrollably on Play | `Drone Controller` still attached alongside `Drone Agent` | Remove/disable `Drone Controller` (see section 5 step 1). |
| ML-Agents console warns "Space Size mismatch" | `Behavior Parameters` Space Size not equal to 14 | Set Space Size = 14 to match `CollectObservations`. |
| `mlagents-learn` hangs at *"Press Play"* | Editor not in Play mode, or wrong scene loaded | Press Play in the editor after starting the CLI; ensure `SampleScene.unity` is the active scene. |
| Keyboard feels laggy in Heuristic mode | `time_scale` > 1 from a previous training run | Reset `EngineConfigurationChannel` to 1.0 for human testing, 20.0 for training. |
| `Connection refused` on `worker_id=0` | Another training process already owns port 5004 | Bump `worker_id` (each value maps to a distinct port). |
| SB3 errors on `env.step()` return shape | Mixing gym (4-tuple) and gymnasium (5-tuple) expectations | Use the SB3 >= 2.0 line, which expects the 5-tuple from `UnityEnv`. |

## References

- [Unity ML-Agents Toolkit — official repo](https://github.com/Unity-Technologies/ml-agents)
- [ML-Agents 3.0 documentation](https://docs.unity3d.com/Packages/com.unity.ml-agents@3.0/manual/index.html)
- [Gymnasium API](https://gymnasium.farama.org/)
- [Stable Baselines3](https://stable-baselines3.readthedocs.io/)
