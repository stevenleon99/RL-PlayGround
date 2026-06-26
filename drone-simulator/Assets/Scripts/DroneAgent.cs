using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DroneCollisionHandler))]
public class DroneAgent : Agent
{
    [Header("Force constants — mirror DroneController so behavior matches")]
    public float liftForce = 15f;
    public float moveForce = 8f;
    public float yawTorque = 3f;
    public float tiltTorque = 2f;
    public float stabilizationStrength = 1.5f;

    [Header("Episode")]
    // Target hover position (x, y, z) in world space.
    public float hoverTargetX = 25.0f;
    public float hoverTargetY = 7.0f;
    public float hoverTargetZ = 1.0f;
    // constrains according to the environment size (see ground plane in the scene)
    public float min_y = 0.0f;
    public float max_y = 10f;
    public float min_x = -75.0f;
    public float max_x = 75.0f;
    public float min_z = -85.0f;
    public float max_z = 85.0f;
    public float resetHorizontalRange = 1.0f;
    public float resetVerticalRange = 0.1f;
    public float resetPenalty = -2.0f;
    public float obstacleCollisionPenalty = -2.0f;
    public float max_uprightness = 0.0f; // from -1.0 (upside down) to 1.0 (upright), 0 means sideways
    private Rigidbody rb;
    private DroneCollisionHandler collisionHandler;
    private Vector3 startPos;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        collisionHandler = GetComponent<DroneCollisionHandler>();
        startPos = transform.localPosition;

        Debug.Log(
            $"DroneAgent hover config: target=({hoverTargetX:F2}, {hoverTargetY:F2}, {hoverTargetZ:F2}), " +
            $"start=({startPos.x:F2}, {startPos.y:F2}, {startPos.z:F2}), " +
            $"maxHeight={max_y:F2}");
    }

    public override void OnEpisodeBegin()
    {
        transform.localPosition = GetRandomStartPosition();
        transform.localRotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        collisionHandler.IsCollided = false;
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

        float effectiveMaxHeight = Mathf.Max(max_y, hoverTargetY + 2f);
        string isResetReason = null;
        float terminalPenalty = resetPenalty;

        // Reset if out of bounds or flipped.
        if (transform.position.x < min_x || transform.position.x > max_x || 
            transform.position.z < min_z || transform.position.z > max_z || 
            transform.position.y < min_y || transform.position.y > effectiveMaxHeight ||
            uprightness < max_uprightness)
        {
            isResetReason = "out of bounds or flipped. transform.position=" + transform.position.ToString("F2") + ", uprightness=" + uprightness.ToString("F2");
        }
        // reset if hit by the obstacle (detected by DroneCollisionHandler)
        else if (collisionHandler.IsCollided)
        {
            isResetReason = "collided with obstacle";
            terminalPenalty = obstacleCollisionPenalty;
            collisionHandler.IsCollided = false;
        }
        
        if (isResetReason != null)
        {
            AddReward(terminalPenalty);
            Debug.Log($"DroneAgent ending episode: {isResetReason}");
            EndEpisode();
        }
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
        return startPos + new Vector3(
            Random.Range(-resetHorizontalRange, resetHorizontalRange),
            Random.Range(-resetVerticalRange, resetVerticalRange),
            Random.Range(-resetHorizontalRange, resetHorizontalRange));
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
}
