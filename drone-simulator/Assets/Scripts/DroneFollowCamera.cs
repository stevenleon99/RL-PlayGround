using UnityEngine;

public class DroneFollowCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 4f, -8f);
    public float smoothSpeed = 5f;

    void LateUpdate()
    {
        if (target == null)
        {
            Debug.LogWarning("Camera target is not assigned.");
            return;
        }

        Vector3 desiredPosition = target.position + target.rotation * offset;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            smoothSpeed * Time.deltaTime
        );

        transform.LookAt(target.position);
    }
}