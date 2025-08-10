using UnityEngine;

public class HelicopterWobble : MonoBehaviour
{
    [Header("Rotation Wobble Settings")]
    [SerializeField] private float _rotationSpeed = 3f;
    [SerializeField] private float _rotationAmount = 2f;

    [Header("Bobbing Settings (on Y axis)")]
    [SerializeField] private float _bobSpeed = 1f;
    [SerializeField] private float _bobAmount = 0.1f;

    private Vector3 _startLocalPos;
    private Quaternion _initialLocalRotation;

    private float _rotTime = 0f;
    private float _bobTime = 0f;

    // Random phase offsets
    private float _rotPhaseOffsetX;
    private float _rotPhaseOffsetZ;
    private float _bobPhaseOffset;

    private void Start()
    {
        _startLocalPos = transform.localPosition;
        _initialLocalRotation = transform.localRotation;

        // Randomize wobble phases
        _rotPhaseOffsetX = Random.Range(0f, Mathf.PI * 2f);
        _rotPhaseOffsetZ = Random.Range(0f, Mathf.PI * 2f);
        _bobPhaseOffset  = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        _rotTime += Time.deltaTime * _rotationSpeed;
        _bobTime += Time.deltaTime * _bobSpeed;

        // Wobble rotation
        float rotX = Mathf.Sin(_rotTime * 1.1f + _rotPhaseOffsetX) * _rotationAmount;
        float rotZ = Mathf.Sin(_rotTime * 0.9f + _rotPhaseOffsetZ) * _rotationAmount;

        // Bobbing along Y axis (Unity's up)
        float bobOffset = Mathf.Sin(_bobTime + _bobPhaseOffset) * _bobAmount;

        transform.localRotation = _initialLocalRotation * Quaternion.Euler(rotX, 0f, rotZ);
        transform.localPosition = _startLocalPos + new Vector3(0f, bobOffset, 0f);
    }
}