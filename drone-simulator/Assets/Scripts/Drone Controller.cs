using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    public float liftForce = 15f;
    public float moveForce = 8f;
    public float yawTorque = 3f;
    public float tiltTorque = 2f;
    public float stabilizationStrength = 1.5f;

    private Rigidbody rb;
    private Keyboard keyboard;

    private bool liftUp;
    private bool liftDown;
    private bool moveForward;
    private bool moveBackward;
    private bool moveLeft;
    private bool moveRight;
    private bool yawLeft;
    private bool yawRight;
    private bool pitchForward;
    private bool pitchBackward;
    private bool rollLeft;
    private bool rollRight;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        liftUp = keyboard.spaceKey.isPressed;
        liftDown = keyboard.leftCtrlKey.isPressed;

        moveForward = keyboard.wKey.isPressed;
        moveBackward = keyboard.sKey.isPressed;
        moveLeft = keyboard.aKey.isPressed;
        moveRight = keyboard.dKey.isPressed;

        yawLeft = keyboard.qKey.isPressed;
        yawRight = keyboard.eKey.isPressed;

        pitchForward = keyboard.upArrowKey.isPressed;
        pitchBackward = keyboard.downArrowKey.isPressed;
        rollLeft = keyboard.leftArrowKey.isPressed;
        rollRight = keyboard.rightArrowKey.isPressed;
    }

    void FixedUpdate()
    {
        HandleLift();
        HandleMovement();
        HandleRotation();
        StabilizeDrone();
    }

    void HandleLift()
    {
        if (liftUp)
        {
            rb.AddForce(Vector3.up * liftForce, ForceMode.Force);
        }

        if (liftDown)
        {
            rb.AddForce(Vector3.down * liftForce * 0.5f, ForceMode.Force);
        }
    }

    void HandleMovement()
    {
        if (moveForward)
        {
            rb.AddForce(transform.forward * moveForce, ForceMode.Force);
        }

        if (moveBackward)
        {
            rb.AddForce(-transform.forward * moveForce, ForceMode.Force);
        }

        if (moveLeft)
        {
            rb.AddForce(-transform.right * moveForce, ForceMode.Force);
        }

        if (moveRight)
        {
            rb.AddForce(transform.right * moveForce, ForceMode.Force);
        }
    }

    void HandleRotation()
    {
        if (yawLeft)
        {
            rb.AddTorque(Vector3.down * yawTorque, ForceMode.Force);
        }

        if (yawRight)
        {
            rb.AddTorque(Vector3.up * yawTorque, ForceMode.Force);
        }

        if (pitchForward)
        {
            rb.AddTorque(transform.right * tiltTorque, ForceMode.Force);
        }

        if (pitchBackward)
        {
            rb.AddTorque(-transform.right * tiltTorque, ForceMode.Force);
        }

        if (rollLeft)
        {
            rb.AddTorque(transform.forward * tiltTorque, ForceMode.Force);
        }

        if (rollRight)
        {
            rb.AddTorque(-transform.forward * tiltTorque, ForceMode.Force);
        }
    }

    void StabilizeDrone()
    {
        Vector3 rotation = transform.rotation.eulerAngles;

        float pitch = NormalizeAngle(rotation.x);
        float roll = NormalizeAngle(rotation.z);

        Vector3 correction = new Vector3(-pitch, 0f, -roll) * stabilizationStrength;

        rb.AddRelativeTorque(correction, ForceMode.Force);
    }

    float NormalizeAngle(float angle)
    {
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return angle;
    }
}