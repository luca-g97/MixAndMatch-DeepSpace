using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class Current : MonoBehaviour
{
    [Header("Current Settings")]
    public float maxVelocity = 50f;
    [Min(0.001f)]public float width = 5f;
    [Range(-1, 1)] public float linearFactor = 0f;

    [Header("Visualization")]
    public Color currentColor = Color.cyan;

    private LineRenderer lineRenderer;

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
    }

    void InitializeLineRenderer()
    {
        if (!lineRenderer) lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Tile;
    }

    void UpdateVisual()
    {
        lineRenderer.startColor = currentColor;
        lineRenderer.endColor = currentColor;
        lineRenderer.startWidth = width * 0.1f;
        lineRenderer.endWidth = width * 0.1f;

        Vector3[] points = new Vector3[lineRenderer.positionCount];
        lineRenderer.GetPositions(points);
        for (int i = 0; i < points.Length; i++)
        {
            points[i].z = 0;
        }
        lineRenderer.SetPositions(points);
    }

    public Vector2[] GetWorldPoints()
    {
        Vector3[] localPoints = new Vector3[lineRenderer.positionCount];
        lineRenderer.GetPositions(localPoints);

        Vector2[] worldPoints = new Vector2[localPoints.Length];
        for (int i = 0; i < localPoints.Length; i++)
        {
            Vector3 worldPoint = transform.TransformPoint(localPoints[i]);
            worldPoints[i] = new Vector2(worldPoint.x, worldPoint.y);
        }
        return worldPoints;
    }
}