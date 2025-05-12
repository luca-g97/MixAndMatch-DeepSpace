using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Spawner2D : MonoBehaviour
{
    public enum ParticleType
    {
        Water = 0,
        // Primary Colors
        OilRed = 1,
        OilYellow = 2,
        OilBlue = 3,
        // Secondary Colors
        OilOrange = 4,
        OilLimeGreen = 5,
        OilViolet = 6,
        // Tertiary Colors
        OilYellowOrange = 7,
        OilRedOrange = 8,
        OilRedViolet = 9,
        OilBlueViolet = 10,
        OilBlueGreen = 11,
        OilYellowGreen = 12
    }

    //public float spawnDensity; // Commented out in original, keep as is

    [Header("Initial Spawn Settings")] // Clarified header
    public Vector2 initialVelocity;
    public float jitterStr;
    public SpawnRegion[] spawnRegions; // For initial setup
    public bool showSpawnBoundsGizmos;

    [Header("Debug Info")]
    public int spawnParticleCount; // Calculated in OnValidate for initial spawn

    // --- Existing Methods for Initial Spawning ---

    public ParticleSpawnData GetSpawnData()
    {
        var rng = new Unity.Mathematics.Random(42); // Consistent seed for initial spawn

        List<float2> allPoints = new();
        List<float2> allVelocities = new();
        List<int> allIndices = new();
        List<int> particleTypes = new();

        for (int regionIndex = 0; regionIndex < spawnRegions.Length; regionIndex++)
        {
            SpawnRegion region = spawnRegions[regionIndex];
            // Use the INSTANCE method for initial spawn
            float2[] points = SpawnInRegion(region);

            for (int i = 0; i < points.Length; i++)
            {
                // Apply jitter specific to initial spawn settings
                float angle = rng.NextFloat() * math.PI * 2; // Use Unity.Mathematics.Random
                float2 dir = new float2(math.cos(angle), math.sin(angle));
                float2 jitter = dir * jitterStr * (rng.NextFloat() - 0.5f); // Use instance jitterStr
                allPoints.Add(points[i] + jitter);
                allVelocities.Add(initialVelocity); // Use instance initialVelocity
                allIndices.Add(regionIndex);
                particleTypes.Add((int)region.particleType);
            }
        }

        ParticleSpawnData data = new()
        {
            positions = allPoints.ToArray(),
            velocities = allVelocities.ToArray(),
            spawnIndices = allIndices.ToArray(),
            particleTypes = particleTypes.ToArray(),
        };

        return data;
    }

    // Instance method used by GetSpawnData for initial spawn
    float2[] SpawnInRegion(SpawnRegion region)
    {
        // This method now simply calls the static helper
        return SpawnInRegionHelper(region);
    }

    // --- Static Helper Methods for Spawning Logic (used by Instance method and FluidSim2D) ---

    public static float2[] SpawnInRegionHelper(SpawnRegion region)
    {
        Vector2 centre = region.position;
        Vector2 size = region.size;

        // Use the static helper to calculate counts
        Vector2Int numPerAxis = CalculateSpawnCountPerAxisBox2D(region.size, region.spawnDensity);

        // Handle case where density or size results in zero particles
        if (numPerAxis.x <= 0 || numPerAxis.y <= 0)
        {
            return new float2[0]; // Return empty array
        }

        float2[] points = new float2[numPerAxis.x * numPerAxis.y];
        int i = 0;

        // --- Improved calculation for tx, ty to handle single-row/column cases ---
        // Calculate step size, handle division by zero if only one particle along an axis
        float stepX = (numPerAxis.x > 1) ? 1f / (numPerAxis.x - 1f) : 0f;
        float stepY = (numPerAxis.y > 1) ? 1f / (numPerAxis.y - 1f) : 0f;

        // Calculate starting offset: -0.5 for multiple particles, 0 for single particle
        float startOffsetX = (numPerAxis.x > 1) ? -0.5f : 0f;
        float startOffsetY = (numPerAxis.y > 1) ? -0.5f : 0f;
        // --- End Improvement ---

        for (int y = 0; y < numPerAxis.y; y++)
        {
            for (int x = 0; x < numPerAxis.x; x++)
            {
                // Use the calculated step and offset
                float tx = startOffsetX + (x * stepX);
                float ty = startOffsetY + (y * stepY);

                float px = tx * size.x + centre.x;
                float py = ty * size.y + centre.y;
                points[i] = new float2(px, py);
                i++;
            }
        }
        return points;
    }

    // Static version used by SpawnInRegionHelper and FluidSim2D
    public static Vector2Int CalculateSpawnCountPerAxisBox2D(Vector2 size, float spawnDensity)
    {
        // Basic validation
        if (size.x <= 0 || size.y <= 0 || spawnDensity <= 0)
        {
            return Vector2Int.zero; // Return zero if input is invalid
        }

        float area = size.x * size.y;
        int targetTotal = Mathf.CeilToInt(area * spawnDensity);
        if (targetTotal <= 0) return Vector2Int.zero; // No particles needed


        // Handle cases where one dimension might be effectively zero relative to the other
        const float epsilon = 0.0001f; // Tolerance for float comparison
        bool xEffectivelyZero = size.x < epsilon;
        bool yEffectivelyZero = size.y < epsilon;

        if (xEffectivelyZero && yEffectivelyZero)
        {
            return Vector2Int.zero; // Both dimensions too small
        }
        else if (xEffectivelyZero)
        {
            // Distribute along Y axis only
            return new Vector2Int(1, Mathf.Max(1, targetTotal));
        }
        else if (yEffectivelyZero)
        {
            // Distribute along X axis only
            return new Vector2Int(Mathf.Max(1, targetTotal), 1);
        }

        // Proceed with original calculation if both dimensions are reasonably sized
        float lenSum = size.x + size.y;
        // Should not be zero if we passed the checks above, but double-check
        if (Mathf.Approximately(lenSum, 0)) return new Vector2Int(1, 1); // Fallback to at least one particle if target > 0

        Vector2 t = size / lenSum;

        // Check t components before division/sqrt
        if (t.x <= 0 || t.y <= 0)
        {
            // Fallback: Distribute based on aspect ratio if t calculation failed
            float aspectRatio = size.x / size.y;
            int nx_fallback = Mathf.CeilToInt(Mathf.Sqrt(targetTotal * aspectRatio));
            int ny_fallback = Mathf.CeilToInt(Mathf.Sqrt(targetTotal / aspectRatio));
            // Ensure at least 1 in each direction if targetTotal > 0
            return new Vector2Int(Mathf.Max(1, nx_fallback), Mathf.Max(1, ny_fallback));
        }

        float denominator = t.x * t.y;
        if (denominator <= 0)
        { // Prevent sqrt of non-positive or division by zero
          // Repeat fallback from above if denominator is invalid
            float aspectRatio = size.x / size.y;
            int nx_fallback = Mathf.CeilToInt(Mathf.Sqrt(targetTotal * aspectRatio));
            int ny_fallback = Mathf.CeilToInt(Mathf.Sqrt(targetTotal / aspectRatio));
            return new Vector2Int(Mathf.Max(1, nx_fallback), Mathf.Max(1, ny_fallback));
        }

        float m = Mathf.Sqrt(targetTotal / denominator);
        int nx = Mathf.CeilToInt(t.x * m);
        int ny = Mathf.CeilToInt(t.y * m);

        // Final guarantee: Ensure at least 1x1 if targetTotal > 0
        nx = Mathf.Max(1, nx);
        ny = Mathf.Max(1, ny);

        return new Vector2Int(nx, ny);
    }

    // --- Struct Definitions ---

    // Data returned by GetSpawnData and used by FluidSim2D
    public struct ParticleSpawnData
    {
        public float2[] positions;
        public float2[] velocities;
        public int[] spawnIndices; // Relevant for initial spawn grouping
        public int[] particleTypes;

        // Constructor for pre-allocation (optional)
        public ParticleSpawnData(int num)
        {
            positions = new float2[num];
            velocities = new float2[num];
            spawnIndices = new int[num];
            particleTypes = new int[num];
        }
    }

    // Defines a region for spawning particles
    [System.Serializable]
    public struct SpawnRegion
    {
        public Vector2 position;
        public Vector2 size;
        public float spawnDensity;
        public ParticleType particleType;
        public Color debugCol;
    }

    // --- Unity Editor Methods ---

    void OnValidate()
    {
        // Recalculate total initial particle count for inspector display
        spawnParticleCount = 0;
        if (spawnRegions != null) // Add null check
        {
            foreach (SpawnRegion region in spawnRegions)
            {
                // Use the static helper for calculation
                Vector2Int spawnCountPerAxis = CalculateSpawnCountPerAxisBox2D(region.size, region.spawnDensity);
                spawnParticleCount += spawnCountPerAxis.x * spawnCountPerAxis.y;
            }
        }
    }

    void OnDrawGizmos()
    {
        // Draw wireframe boxes for initial spawn regions in the editor
        if (showSpawnBoundsGizmos && !Application.isPlaying && spawnRegions != null) // Add null check
        {
            foreach (SpawnRegion region in spawnRegions)
            {
                Gizmos.color = region.debugCol;
                Gizmos.DrawWireCube(region.position, region.size);
            }
        }
    }
}