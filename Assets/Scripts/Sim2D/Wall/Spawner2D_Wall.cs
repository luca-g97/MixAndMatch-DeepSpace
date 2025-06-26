using Seb.Fluid2D.Simulation;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public class Spawner2D_Wall : MonoBehaviour
{
    public Vector2 initialVelocity;
    public float jitterStr;
    public bool showSpawnBoundsGizmos;

    [Header("Initial Spawn Settings")]
    public bool allowInitialSpawn = true;

    [Header("Continuous Spawn Settings")]
    public bool allowContinuousSpawning = true;

    public SpawnRegion[] spawnRegions;

    [Header("References")]
    [Tooltip("Assign the FluidSim2D instance here to display its current particle count.")]
    public FluidSim2D_Wall fluidSimulation; // << NEW: Assign FluidSim2D instance in Inspector

    [Header("Debug Info")]
    [Tooltip("Number of particles this spawner would create in an initial burst based on its settings.")]
    public int initialSpawnParticleCount;
    [Tooltip("Current total particles in the Fluid Simulation (read-only). Updated at runtime.")]
    public int currentSimParticleCount_Inspector; // << NEW: To display live count from FluidSim2D

    private Unity.Mathematics.Random _continuousSpawnRng;

    void Awake()
    {
        _continuousSpawnRng = new Unity.Mathematics.Random((uint)System.Environment.TickCount + (uint)GetInstanceID().GetHashCode());
    }

    void Update()
    {
        // Update the Inspector field with the live particle count from FluidSim2D
        if (fluidSimulation != null)
        {
            currentSimParticleCount_Inspector = fluidSimulation.numParticles;
        }
        else
        {
            // If FluidSim2D is not assigned, perhaps indicate this
            // currentSimParticleCount_Inspector = -1; // Or some other indicator
        }
    }

    public ParticleSpawnData GetSpawnData()
    {
        if (!allowInitialSpawn)
        {
            return new ParticleSpawnData(0);
        }
        var initialSpawnRng = new Unity.Mathematics.Random(42);
        List<float2> allPoints = new();
        List<float2> allVelocities = new();
        List<int> allIndices = new();
        List<int2> particleTypes = new();

        for (int regionIndex = 0; regionIndex < spawnRegions.Length; regionIndex++)
        {
            SpawnRegion region = spawnRegions[regionIndex];
            float2[] points = SpawnInRegionUsingDensity(region);
            for (int i = 0; i < points.Length; i++)
            {
                float angle = (float)initialSpawnRng.NextDouble() * Mathf.PI * 2f;
                float2 dir = new float2(Mathf.Cos(angle), Mathf.Sin(angle));
                float2 jitter = dir * jitterStr * ((float)initialSpawnRng.NextDouble() - 0.5f);
                allPoints.Add(points[i] + jitter);
                allVelocities.Add(initialVelocity);
                allIndices.Add(regionIndex);
                particleTypes.Add(new int2((int)region.particleType, -1));
            }
        }
        return new ParticleSpawnData
        {
            positions = allPoints.ToArray(),
            velocities = allVelocities.ToArray(),
            spawnIndices = allIndices.ToArray(),
            particleTypes = particleTypes.ToArray(),
        };
    }

    public ParticleSpawnData GetNewlySpawnedParticles(float deltaTime, int currentTotalParticles, int maxTotalParticles)
    {
        if (!allowContinuousSpawning || currentTotalParticles >= maxTotalParticles)
        {
            return new ParticleSpawnData(0);
        }
        List<float2> newPoints = new();
        List<float2> newVelocities = new();
        List<int> newSpawnIndices = new();
        List<int2> newParticleTypes = new();
        int particlesAddedThisFrame = 0;

        for (int regionIndex = 0; regionIndex < spawnRegions.Length; regionIndex++)
        {
            SpawnRegion region = spawnRegions[regionIndex]; // Struct copy
            if (region.particlesPerSecond <= 0) continue;

            float newSpawnsPotential = region.particlesPerSecond * deltaTime + spawnRegions[regionIndex].spawnAccumulator;
            int numToSpawnThisRegion = Mathf.FloorToInt(newSpawnsPotential);
            // Update the accumulator in the actual array element
            spawnRegions[regionIndex].spawnAccumulator = newSpawnsPotential - numToSpawnThisRegion;


            if (numToSpawnThisRegion > 0)
            {
                int maxCanSpawnGlobal = maxTotalParticles - (currentTotalParticles + particlesAddedThisFrame);
                numToSpawnThisRegion = Mathf.Min(numToSpawnThisRegion, maxCanSpawnGlobal);

                for (int i = 0; i < numToSpawnThisRegion; i++)
                {
                    if (currentTotalParticles + particlesAddedThisFrame >= maxTotalParticles) break;
                    float px = region.position.x + ((float)_continuousSpawnRng.NextDouble() - 0.5f) * region.size.x;
                    float py = region.position.y + ((float)_continuousSpawnRng.NextDouble() - 0.5f) * region.size.y;
                    float2 spawnPos = new float2(px, py);
                    float angle = (float)_continuousSpawnRng.NextDouble() * Mathf.PI * 2f;
                    float2 dir = new float2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float2 jitter = dir * jitterStr * ((float)_continuousSpawnRng.NextDouble() - 0.5f);
                    newPoints.Add(spawnPos + jitter);
                    newVelocities.Add(initialVelocity);
                    newSpawnIndices.Add(regionIndex);
                    newParticleTypes.Add(new int2((int)region.particleType, -1));
                    particlesAddedThisFrame++;
                }
            }
            //spawnRegions[regionIndex].particlesPerSecond = 0; <-- ONLY USED FOR FLOOR!!
            if (currentTotalParticles + particlesAddedThisFrame >= maxTotalParticles) break;
        }
        return new ParticleSpawnData
        {
            positions = newPoints.ToArray(),
            velocities = newVelocities.ToArray(),
            spawnIndices = newSpawnIndices.ToArray(),
            particleTypes = newParticleTypes.ToArray()
        };
    }

    float2[] SpawnInRegionUsingDensity(SpawnRegion region)
    {
        Vector2 centre = region.position;
        Vector2 size = region.size;
        Vector2Int numPerAxis = CalculateSpawnCountPerAxisBox2D(region.size, region.spawnDensity);
        if (numPerAxis.x * numPerAxis.y == 0) return new float2[0];
        float2[] points = new float2[numPerAxis.x * numPerAxis.y];
        int pointIndex = 0;
        for (int y = 0; y < numPerAxis.y; y++)
        {
            for (int x = 0; x < numPerAxis.x; x++)
            {
                float tx = (numPerAxis.x > 1) ? (float)x / (numPerAxis.x - 1) : 0.5f;
                float ty = (numPerAxis.y > 1) ? (float)y / (numPerAxis.y - 1) : 0.5f;
                float px = (tx - 0.5f) * size.x + centre.x;
                float py = (ty - 0.5f) * size.y + centre.y;
                points[pointIndex++] = new float2(px, py);
            }
        }
        return points;
    }

    static Vector2Int CalculateSpawnCountPerAxisBox2D(Vector2 size, float spawnDensity)
    {
        if (size.x <= 0 || size.y <= 0 || spawnDensity <= 0) return Vector2Int.zero;
        float area = size.x * size.y;
        int targetTotal = Mathf.CeilToInt(area * spawnDensity);
        if (targetTotal == 0) return Vector2Int.zero;
        float lenSum = size.x + size.y;
        if (lenSum == 0) return (targetTotal > 0) ? new Vector2Int(Mathf.Max(1, targetTotal), 1) : Vector2Int.zero;
        Vector2 t = size / lenSum;
        if (t.x == 0 || t.y == 0)
        {
            if (t.x == 0 && t.y > 0) return new Vector2Int(1, Mathf.Max(1, targetTotal));
            if (t.y == 0 && t.x > 0) return new Vector2Int(Mathf.Max(1, targetTotal), 1);
            return new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(targetTotal))), Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(targetTotal))));
        }
        float m = Mathf.Sqrt(targetTotal / (t.x * t.y));
        int nx = Mathf.Max(1, Mathf.CeilToInt(t.x * m));
        int ny = Mathf.Max(1, Mathf.CeilToInt(t.y * m));
        return new Vector2Int(nx, ny);
    }

    public struct ParticleSpawnData
    {
        public float2[] positions;
        public float2[] velocities;
        public int[] spawnIndices;
        public int2[] particleTypes;
        public ParticleSpawnData(int num)
        {
            positions = new float2[num];
            velocities = new float2[num];
            spawnIndices = new int[num];
            particleTypes = new int2[num];
        }
    }

    void OnValidate()
    {
        initialSpawnParticleCount = 0;
        if (spawnRegions == null) return;
        foreach (SpawnRegion region in spawnRegions)
        {
            Vector2Int spawnCountPerAxis = CalculateSpawnCountPerAxisBox2D(region.size, region.spawnDensity);
            initialSpawnParticleCount += spawnCountPerAxis.x * spawnCountPerAxis.y;

            region.name = region.particleType.ToString();
        }
    }

    void OnDrawGizmos()
    {
        if (showSpawnBoundsGizmos && !Application.isPlaying)
        {
            if (spawnRegions == null) return;
            foreach (SpawnRegion region in spawnRegions)
            {
                Gizmos.color = region.debugCol;
                Gizmos.DrawWireCube(region.position, region.size);
            }
        }
    }
}