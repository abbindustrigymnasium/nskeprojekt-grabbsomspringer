using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform cameraTarget;
    private float fixedX, fixedY;

    void Start()
    {
        fixedX = transform.position.x;
        fixedY = transform.position.y;
    }

    void LateUpdate()
    {
        transform.position = new Vector3(fixedX, cameraTarget.position.y, cameraTarget.position.z);
    }
}
