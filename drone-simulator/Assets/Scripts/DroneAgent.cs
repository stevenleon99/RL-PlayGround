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