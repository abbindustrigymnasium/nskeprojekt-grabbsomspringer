using TMPro;
using UnityEngine;

public class AltitudeTracker : MonoBehaviour
{
    [SerializeField] private Transform playerTransform;
    [SerializeField] private TextMeshProUGUI altitudeText;



    void FixedUpdate()
    {
        float altitude = playerTransform.position.y;
        altitudeText.text = altitude.ToString("F1") + " m";
    }
}
