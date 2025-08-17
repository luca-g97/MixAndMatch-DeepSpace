using System;
using DG.Tweening;
using UnityEngine;
using Random = UnityEngine.Random;

[RequireComponent(typeof(LineRenderer))]
public class Current : MonoBehaviour
{
    [Header("Current Settings")]
    public float currentVelocity = 1f;
    [Min(0.001f)] public float currentWidth = 5f;
    [Range(-1, 1)] public float linearFactor = 0f;

    [SerializeField] private float _currentVelocityMultiplier = 1f;
    [SerializeField] public float _minVelocity = 0.01f;
    [SerializeField] public float _maxVelocity = 0.1f;
    [SerializeField] private float _minWidth = 0.25f;
    [SerializeField] private float _maxWidth = 0.75f;
    [SerializeField] private float _minChangeDuration = 4f;
    [SerializeField] private float _maxChangeDuration = 8f;
    [SerializeField] private float _minStayTime = 0.5f;
    [SerializeField] private float _maxStayTime = 5f;

    [Header("Visualization")]
    public Color _currentColor = Color.cyan;

    private LineRenderer _lineRenderer;
    private Sequence _currentSequence;

    private float _targetVelocity;
    private float _targetWidth;
    private float _newChangeDuration;
    private float _newStayTime;

    void OnValidate()
    {
        InitializeLineRenderer();
        UpdateVisual();
    }

    private void Start()
    {
#if !UNITY_EDITOR
        InitializeLineRenderer();
        UpdateVisual();
#endif
        RerollTargetValues();
        SetCurrentVelocity(_targetVelocity);
        SetCurrentWidth(_targetWidth);
        CurrentSequence();
    }

    private void OnDestroy()
    {
        _currentSequence?.Kill();
    }

    private void Update()
    {
        if (_currentSequence == null || !_currentSequence.IsActive() || !_currentSequence.IsPlaying())
        {
            _currentSequence?.Kill();
            _currentSequence = CurrentSequence();
        }
    }

    void InitializeLineRenderer()
    {
        if (!_lineRenderer) _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.alignment = LineAlignment.View;
        _lineRenderer.textureMode = LineTextureMode.Tile;
    }

    void UpdateVisual()
    {
        if (!_lineRenderer)
        {
            InitializeLineRenderer();
        }
        _lineRenderer.startColor = _currentColor;
        _lineRenderer.endColor = _currentColor;
        _lineRenderer.startWidth = currentWidth * 0.1f;
        _lineRenderer.endWidth = currentWidth * 0.1f;

        Vector3[] points = new Vector3[_lineRenderer.positionCount];
        _lineRenderer.GetPositions(points);
        for (int i = 0; i < points.Length; i++)
        {
            points[i].z = 0;
        }

        _lineRenderer.SetPositions(points);
    }

    public Vector2[] GetWorldPoints()
    {
        if (!_lineRenderer)
        {
            InitializeLineRenderer();
        }
        Vector3[] localPoints = new Vector3[_lineRenderer.positionCount];
        _lineRenderer.GetPositions(localPoints);

        Vector2[] worldPoints = new Vector2[localPoints.Length];
        for (int i = 0; i < localPoints.Length; i++)
        {
            Vector3 worldPoint = transform.TransformPoint(localPoints[i]);
            worldPoints[i] = new Vector2(worldPoint.x, worldPoint.y);
        }

        return worldPoints;
    }

    private Sequence CurrentSequence()
    {
        return DOTween.Sequence()
            .AppendCallback(RerollTargetValues)
            .Append(DOTween.To(() => currentVelocity, SetCurrentVelocity, _targetVelocity,
                _newChangeDuration))
            .Join(DOTween.To(() => currentWidth, SetCurrentWidth,
                _targetWidth, _newChangeDuration))
            .AppendInterval(_newStayTime);
    }
    
    private void RerollTargetValues()
    {
        _targetVelocity = Random.Range(_minVelocity, _maxVelocity);
        _targetWidth = Random.Range(_minWidth, _maxWidth);
        _newChangeDuration = Random.Range(_minChangeDuration, _maxChangeDuration);
        _newStayTime = Random.Range(_minStayTime, _maxStayTime);
    }

    private void SetCurrentVelocity(float velocity)
    {
        currentVelocity = Mathf.Clamp(velocity, _minVelocity, _maxVelocity);
        currentVelocity *= _currentVelocityMultiplier;
        //UpdateVisual();
    }

    private void SetCurrentWidth(float width)
    {
        currentWidth = Mathf.Clamp(width, _minWidth, _maxWidth);
        UpdateVisual();
    }
}