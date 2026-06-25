using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class DroneAgent : Agent
{
    [Header("Force constants — mirror DroneController so behavior matches")]
    public float liftForce = 15f;
    public float moveForce = 8f;
    public float yawTorque = 3f;
    public float tiltTorque = 2f;
    public float stabilizationStrength = 1.5f;

    [Header("Episode")]
    public float hoverTargetX = 5.0f;
    public float hoverTargetY = 8.0f;
    public float hoverTargetZ = 5.0f;
    public float startX = 0.0f;
    public float startY = 0.4f;
    public float startZ = 0.0f;
    public float groundY = 0.3f;
    public float maxHorizontalDrift = 30f;
    public float maxHeight = 10f;
    public float badHoverY = 0.8f;
    public float resetHorizontalRange = 1.0f;
    public float resetVerticalRange = 0.1f;

    private Rigidbody rb;
    private Vector3 startPos;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        startPos = transform.localPosition;

        Debug.Log(
            $"DroneAgent hover config: target=({hoverTargetX:F2}, {hoverTargetY:F2}, {hoverTargetZ:F2}), " +
            $"start=({startX:F2}, {startY:F2}, {startZ:F2}), " +
            $"maxHeight={maxHeight:F2}, badHoverY={badHoverY:F2}");
    }

    public override void OnEpisodeBegin()
    {
        transform.localPosition = GetRandomStartPosition();
        transform.localRotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // 14-D observation vector.
    public override void CollectObservations(VectorSensor sensor)
    {
        // Relative vector to the desired hover point (3).
        Vector3 toHoverPoint = GetHoverPosition() - transform.position;
        sensor.AddObservation(toHoverPoint / 10f);

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
        float forward = Mathf.Clamp(c[2], -1f, 1f);
        float right   = Mathf.Clamp(c[3], -1f, 1f);

        rb.AddForce(Vector3.up * thrust * liftForce, ForceMode.Force);
        rb.AddForce(transform.forward * forward * moveForce, ForceMode.Force);
        rb.AddForce(transform.right * right * moveForce, ForceMode.Force);
        rb.AddTorque(Vector3.up * yaw * yawTorque, ForceMode.Force);

        StabilizeDrone();

        // --- Rewards (move to the target position and hover there) --------
        Vector3 hoverPosition = GetHoverPosition();
        Vector3 positionError = transform.position - hoverPosition;
        float heightError = Mathf.Abs(positionError.y);
        float horizontalError = new Vector2(positionError.x, positionError.z).magnitude;
        float speed = rb.linearVelocity.magnitude;
        float angularSpeed = rb.angularVelocity.magnitude;
        float uprightness = Vector3.Dot(transform.up, Vector3.up);

        AddReward(0.01f);
        AddReward(-heightError * 0.03f);
        AddReward(-horizontalError * 0.01f);
        AddReward(-speed * 0.004f);
        AddReward(-angularSpeed * 0.002f);
        AddReward((uprightness - 1f) * 0.01f);

        bool isStoppedAtWrongHeight =
            Mathf.Abs(transform.position.y - badHoverY) < 0.15f &&
            Mathf.Abs(rb.linearVelocity.y) < 0.1f;

        if (isStoppedAtWrongHeight)
        {
            AddReward(-0.05f);
        }

        bool isHovering =
            heightError < 0.08f &&
            horizontalError < 0.25f &&
            speed < 0.2f &&
            angularSpeed < 0.2f &&
            uprightness > 0.95f;

        if (isHovering)
        {
            AddReward(0.05f);
        }

        float effectiveMaxHeight = Mathf.Max(maxHeight, hoverTargetY + 2f);
        string resetReason = null;

        if (transform.position.y < groundY)
        {
            resetReason = $"too low: y={transform.position.y:F2}, groundY={groundY:F2}";
        }
        else if (transform.position.y > effectiveMaxHeight)
        {
            resetReason = $"too high: y={transform.position.y:F2}, maxHeight={effectiveMaxHeight:F2}";
        }
        else if (uprightness < 0f)
        {
            resetReason = $"flipped: uprightness={uprightness:F2}";
        }

        if (resetReason != null)
        {
            AddReward(-2f);
            Debug.Log($"DroneAgent ending episode: {resetReason}");
            EndEpisode();
        }
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
        c[2] = ((kb.wKey.isPressed || kb.upArrowKey.isPressed) ? 1f : 0f) -
               ((kb.sKey.isPressed || kb.downArrowKey.isPressed) ? 1f : 0f);
        c[3] = ((kb.dKey.isPressed || kb.rightArrowKey.isPressed) ? 1f : 0f) -
               ((kb.aKey.isPressed || kb.leftArrowKey.isPressed) ? 1f : 0f);
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

    private Vector3 GetHoverPosition()
    {
        return new Vector3(hoverTargetX, hoverTargetY, hoverTargetZ);
    }

    private Vector3 GetRandomStartPosition()
    {
        Vector3 startPosition = new Vector3(startX, startY, startZ);
        return startPosition + new Vector3(
            Random.Range(-resetHorizontalRange, resetHorizontalRange),
            Random.Range(-resetVerticalRange, resetVerticalRange),
            Random.Range(-resetHorizontalRange, resetHorizontalRange));
    }
}
