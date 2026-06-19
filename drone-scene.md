# Drone Scene Setup

## Context

The UnityMCP server disconnected mid-task, so the scene couldn't be built by
driving Unity directly. Instead, everything is assembled at runtime by a
single editor script — more reliable than hand-writing the scene YAML.

## Files

| Path | Purpose |
| --- | --- |
| `Assets/Scripts/DroneController.cs` | (Pre-existing) Rigidbody-driven flight controls |
| `Assets/Scripts/DroneFollowCamera.cs` | (Pre-existing) Smooth third-person follow camera |
| `Assets/Editor/DroneSceneBuilder.cs` | **New.** Menu-driven scene builder |

## How to build the scene

1. Open the **drone-simulator** project in Unity.
2. Wait for the compile to finish (spinner bottom-right clears).
3. Top menu: **Drone → Build Drone Simulation Scene**.
4. `Assets/Scenes/DroneSimulationScene.unity` is saved and opened.
5. Press **Play**.

## Scene hierarchy produced by the builder

```
Environment
├── Ground              plane, 30×30, grass green
├── Sun                 directional light, soft shadows
├── Obstacle_Cube_1     cube, orange
├── Obstacle_Cube_2     cube, blue, taller
└── Obstacle_Cylinder   cylinder, yellow

Drone                   Rigidbody + DroneController, spawns at y = 2.5
├── Body                capsule
├── Arm_FR / FL / BR / BL       horizontal cylinders, X-pattern
└── Propeller_FR / FL / BR / BL flat red discs

CameraRig
└── MainCamera          Camera + AudioListener + DroneFollowCamera (target = Drone)

Targets
└── Target              yellow sphere at (0, 2.5, 8)
    └── TargetTrigger   larger 1.5× sphere trigger collider
```

## Controls (DroneController)

| Key | Action |
| --- | --- |
| W / S | Thrust forward / back |
| A / D | Yaw left / right |
| Space / Left Ctrl or Shift | Altitude up / down |
| Arrow keys | Pitch + roll (tilt) |

All input is applied via `Rigidbody.AddForce` / `AddTorque` in `FixedUpdate`.
Auto-stabilization gently levels the drone when no tilt is given.

## Validation checklist (run in Unity after building)

- [ ] Hierarchy shows the four roots (Environment / Drone / CameraRig / Targets).
- [ ] Drone hovers above ground in the Scene view.
- [ ] Drone Inspector has Rigidbody and DroneController.
- [ ] Target sphere is visible in front of the drone.
- [ ] Console shows only the builder's success log (no red errors).
- [ ] Play mode: keyboard input moves the drone and the camera trails behind.

## Notes

- Project uses **URP 17.5.0** on **Unity 6 (6000.5.0f1)**. The builder picks
  the `Universal Render Pipeline/Lit` shader for materials, with a `Standard`
  fallback.
- Re-running the menu item rebuilds the scene from scratch — safe to iterate.
