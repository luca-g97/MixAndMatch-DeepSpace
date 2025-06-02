using UnityEngine;

public class SpinAroundZ : MonoBehaviour
{
    [SerializeField] private float _spinSpeed = 180f; // Degrees per second
    [SerializeField] private bool _randomStartRotation = true;

    private void Start()
    {
        if (!_randomStartRotation) return;
        
        float randomZ = Random.Range(0f, 360f);
        transform.Rotate(0f, 0f, randomZ);
    }

    private void Update()
    {
        // Rotate around local Z axis
        transform.Rotate(0f, 0f, _spinSpeed * Time.deltaTime);
    }
}