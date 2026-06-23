# Drone Scene

## Context

The scene is hand-built in `Assets/Scenes/SampleScene.unity`. There is no
runtime/editor scene builder — the hierarchy is edited directly in Unity.

## Files

| Path | Purpose |
| --- | --- |
| `Assets/Scripts/Drone Controller.cs` | Rigidbody-driven flight controls (note: filename has a space) |
| `Assets/Scripts/DroneFollowCamera.cs` | Smooth third-person follow camera |
| `Assets/Scenes/SampleScene.unity` | The scene described below |

## Current scene hierarchy (`SampleScene.unity`)

```
Environment
├── ground
├── obs_cube1
├── obs_cube2
└── obs_cube3

Drone                       position: (9.91, 7.98, -31.18), Rigidbody (gravity on)
├── body
├── frontBackArm
├── leftRightArm
├── propeller_front
├── propeller_back
├── propeller_left
└── propeller_right

Main Camera                 Camera + AudioListener (no follow script attached)
Directional Light
Global Volume
target
```

## Wiring status (important)

The GameObjects exist but **the scripts are not attached**:

- `Drone` has a `Rigidbody` but **no `DroneController`**. Without it, the
  drone will simply fall under gravity when you press Play.
- `Main Camera` has **no `DroneFollowCamera`** component and no target
  assigned. It will not track the drone.

To make the scene playable, in Unity:

1. Select **Drone** → **Add Component** → `DroneController` (the
   `RequireComponent(typeof(Rigidbody))` attribute means the existing
   Rigidbody is retained).
2. Select **Main Camera** → **Add Component** → `DroneFollowCamera`.
3. Drag the **Drone** onto the follow camera's `Target` field.
4. Press **Play**.

## Controls (`Drone Controller.cs`)

| Key | Action | Tunable |
| --- | --- | --- |
| **Space** | Thrust up | `liftForce = 15` |
| **Left Ctrl** | Thrust down (half-strength) | `liftForce × 0.5` |
| **W / S** | Move forward / back | `moveForce = 8` |
| **A / D** | Strafe left / right | `moveForce = 8` |
| **Q / E** | Yaw left / right | `yawTorque = 3` |
| **Up / Down arrows** | Pitch forward / back | `tiltTorque = 2` |
| **Left / Right arrows** | Roll left / right | `tiltTorque = 2` |

All input is read from `Keyboard.current` in `Update` and applied as
`Rigidbody.AddForce` / `AddTorque` in `FixedUpdate`. `StabilizeDrone()`
applies a counter-torque proportional to current pitch and roll, so the
drone gently auto-levels when no arrow keys are held.

## Validation checklist (run in Unity after wiring scripts)

- [ ] Drone Inspector shows Rigidbody **and** DroneController.
- [ ] Main Camera Inspector shows DroneFollowCamera with `Target = Drone`.
- [ ] Press Play → drone hovers (doesn't fall through the ground).
- [ ] WASD, QE, arrows, Space/Ctrl all produce the expected motion.
- [ ] Releasing the arrow keys levels the drone back out.
- [ ] Camera follows the drone as it moves.

## Notes

- Project uses **URP 17.5.0** on **Unity 6000.5.0f1**.
- The drone starts high above the ground (`y ≈ 8`) and gravity is on, so
  without `DroneController` it will drop on Play.
- `DroneFollowCamera` default offset is `(0, 4, -8)` with `smoothSpeed = 5`.
