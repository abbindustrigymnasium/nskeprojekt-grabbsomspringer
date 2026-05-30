using UnityEngine;

public class BarInteractable : MonoBehaviour
{
    [Header("Bar Points")]
    [SerializeField] private Transform attachPoint;

    [Header("Optional Debug")]
    [SerializeField] private bool drawDebug = true;

    public Vector3 AttachPosition
    {
        get
        {
            if (attachPoint != null)
                return attachPoint.position;

            return transform.position;
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawDebug)
            return;

        if (attachPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(attachPoint.position, 0.12f);
            Gizmos.DrawLine(transform.position, attachPoint.position);
        }
    }
}