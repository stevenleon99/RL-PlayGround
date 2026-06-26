using UnityEngine;

public class DroneCollisionHandler : MonoBehaviour
{
    public bool IsCollided { get; set; }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Obstacle"))
        {
            IsCollided = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Obstacle"))
        {
            IsCollided = true;
        }
    }
}
