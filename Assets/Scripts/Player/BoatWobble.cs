using UnityEngine;

public class BoatWobble : MonoBehaviour
{
    [Header("Rotation Wobble Settings")]
    [SerializeField] private float _rotationSpeed = 3f;
    [SerializeField] private float _rotationAmount = 2f;

    [Header("Bobbing Settings (on Z axis)")]
    [SerializeField] private float _bobSpeed = 1f;
    [SerializeField] private float _bobAmount = 0.1f;

    private Vector3 _startLocalPos;
    private Quaternion _initialLocalRotation;

    private float _rotTime = 0f;
    private float _bobTime = 0f;

    // Random phase offsets
    private float _rotPhaseOffsetX;
    private float _rotPhaseOffsetY;
    private float bobPhaseOffset;

    public BoatWobble(float bobPhaseOffset)
    {
        this.bobPhaseOffset = bobPhaseOffset;
    }

    private void Start()
    {
        _startLocalPos = transform.localPosition;
        _initialLocalRotation = transform.localRotation;

        // Random phase offsets (per object)
        _rotPhaseOffsetX = Random.Range(0f, Mathf.PI * 2f);
        _rotPhaseOffsetY = Random.Range(0f, Mathf.PI * 2f);
        bobPhaseOffset   = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        _rotTime += Time.deltaTime * _rotationSpeed;
        _bobTime += Time.deltaTime * _bobSpeed;

        // Apply phase offsets for variation
        float rotX = Mathf.Sin(_rotTime * 1.1f + _rotPhaseOffsetX) * _rotationAmount;
        float rotY = Mathf.Sin(_rotTime * 0.9f + _rotPhaseOffsetY) * _rotationAmount;
        float bobOffset = Mathf.Sin(_bobTime + bobPhaseOffset) * _bobAmount;

        // Apply rotation and position
        transform.localRotation = _initialLocalRotation * Quaternion.Euler(rotX, rotY, 0f);
        transform.localPosition = _startLocalPos + new Vector3(0f, 0f, bobOffset);
    }
}