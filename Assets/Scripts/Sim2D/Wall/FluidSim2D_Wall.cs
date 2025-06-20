using Seb.Helpers; // Assuming this namespace contains ComputeHelper and SpatialHash
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices; // For Marshal.SizeOf
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering; // Required for AsyncGPUReadback

namespace Seb.Fluid2D.Simulation
{
    public class FluidSim2D_Wall : MonoBehaviour
    {
        public event System.Action SimulationStepCompleted;

        private Spawner2D fluidSimSpawner;

        [Header("Simulation Settings")]
        public float timeScale = 1;
        public float maxTimestepFPS = 60;
        public int iterationsPerFrame = 1;
        public float gravity = -9.81f;
        [Range(0, 1)] public float collisionDamping = 0.95f;
        public float smoothingRadius = 2;
        public Vector2 boundsSize = new Vector2(20, 10);
        public float yOffset = 0;
        public int maxTotalParticles = 100000;

        [Header("Water Properties")]
        public float waterTargetDensity = 1;
        public float waterPressureMultiplier = 100;
        public float waterNearPressureMultiplier = 200;
        public float waterViscosityStrength = 0.1f;

        [Header("Oil Properties")]
        public float oilTargetDensity = 0.92f;
        public float oilPressureMultiplier = 80;
        public float oilNearPressureMultiplier = 150;
        public float oilViscosityStrength = 0.4f;

        [Header("Interaction Settings")]
        public float interactionRadius = 2f;
        public float interactionStrength = 50f;

        [Header("Mouse Gravity Settings")]
        public float mouseGravityStrength = 10f;
        public float mouseGravityRadius = 5f;
        public bool invertMouseGravity = false;

        [Header("References")]
        public ComputeShader compute;
        public Spawner2D_Wall spawner2D;

        public ComputeBuffer positionBuffer { get; private set; }
        ComputeBuffer predictedPositionBuffer;
        public ComputeBuffer velocityBuffer { get; private set; }
        public ComputeBuffer densityBuffer { get; private set; }
        public ComputeBuffer gravityScaleBuffer { get; private set; }
        public ComputeBuffer collisionBuffer { get; private set; }
        public ComputeBuffer particleTypeBuffer { get; private set; }

        ComputeBuffer sortTarget_Position;
        ComputeBuffer sortTarget_PredicitedPosition;
        ComputeBuffer sortTarget_Velocity;
        ComputeBuffer sortTarget_ParticleType;

        ComputeBuffer vertexBuffer;

        public ComputeBuffer obstacleBuffer { get; private set; }
        public ComputeBuffer obstacleColorsBuffer { get; private set; }

        SpatialHash_Wall spatialHash;

        const int externalForcesKernel = 0;
        const int spatialHashKernel = 1;
        const int reorderKernel = 2;
        const int copybackKernel = 3;
        const int densityKernel = 4;
        const int pressureKernel = 5;
        const int viscosityKernel = 6;
        const int updatePositionKernel = 7;

        bool isPaused;
        Spawner2D_Wall.ParticleSpawnData initialSpawnData;
        bool pauseNextFrame;
        public int numParticles { get; private set; }

        [Header("Obstacles")]

        public List<GameObject> obstacles = new List<GameObject>();
        [Min(0)] public float areaToColorAroundObstacles = 1.0f;
        [Min(0)] public float minDistanceToRemoveParticles = 0.2f;
        [Min(0)] public float coloredAreaAroundObstaclesDivider = 0.05f;

        private MaterialPropertyBlock _propBlock_Wall;
        private Material _sharedUnlitMaterial;

        private List<Vector2> currentVertices = new List<Vector2>();
        private ComputeBuffer currentsBuffer;
        private ComputeBuffer currentVerticesBuffer;

        [StructLayout(LayoutKind.Sequential)]
        public struct CurrentData
        {
            public int vertexStart;
            public int vertexCount;
            public float maxVelocity;
            public float width;
            public float linearFactor;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct ObstacleData
        {
            [FieldOffset(0)] public Vector2 centre;
            [FieldOffset(8)] public int vertexStart;
            [FieldOffset(12)] public int vertexCount;
            [FieldOffset(16)] public float lineWidth;
            [FieldOffset(20)] public int obstacleType;
        }

        private struct CachedObstacleInfo
        {
            public PolygonCollider2D polyCol;
            public LineRenderer lineRend;
            public Transform transform;
        }
        private Dictionary<GameObject, CachedObstacleInfo> _obstacleCache = new Dictionary<GameObject, CachedObstacleInfo>();

        public static List<Color> colorPalette = new List<Color> {
            new Color(0.9f, 0f, 0.4f), new Color(1f, 0.9f, 0f), new Color(0.0f, 0.4f, 0.7f),
            new Color(0.95f, 0.55f, 0f),  new Color(0.6f, 0.1f, 0.5f), new Color(0.6f, 0.75f, 0.1f),
            new Color(0.9f, 0.35f, 0f), new Color(1f, 0.75f, 0f), new Color(0.9f, 0f, 0.5f),
            new Color(0.4f, 0.3f, 0.6f), new Color(0.05f, 0.7f, 0.6f), new Color(0.8f, 0.85f, 0f)
        };

        private int[] removedParticlesPerColor = new int[colorPalette.Count];
        private int[] particlesReachedDestination = new int[colorPalette.Count];

        List<Vector2> _gpuVerticesData = new List<Vector2>();
        List<ObstacleData> _gpuObstacleDataList = new List<ObstacleData>();
        List<Color> _gpuObstacleColorsData = new List<Color>();

        Dictionary<GameObject, int> playerColors = new Dictionary<GameObject, int>();
        private List<int> mixableColors = new List<int>();
        public List<Color> mixableColorsForShader = new List<Color>();
        [Range(0, 6)] public int maxPlayerColors = 6;
        public Color colorSymbolizingNoPlayer = Color.white;
        public int lastPlayerCount = -1;

        [Header("Obstacle Visualization")]
        public Color obstacleLineColor = Color.white;
        [Min(0)] public float obstacleLineWidth = 0.1f;
        public Material lineRendererMaterial;

        private float autoUpdateInterval = 0.5f;
        private float nextAutoUpdateTime;
        private bool _forceObstacleBufferUpdate = false;

        void Start()
        {
            Debug.Log("Controls: Space = Play/Pause, R = Reset, LMB = Attract, RMB = Repel, G + Mouse = Gravity Well");
            InitSimulation();
        }

        void Awake()
        {
            fluidSimSpawner = GameObject.FindFirstObjectByType<Spawner2D>();
        }

        void InitSimulation()
        {
            Time.fixedDeltaTime = 1f / maxTimestepFPS;

            if (spawner2D != null)
            {
                initialSpawnData = spawner2D.GetSpawnData();
                numParticles = initialSpawnData.positions?.Length ?? 0;
            }
            else
            {
                Debug.LogError("Spawner2D is not assigned! No particles will be spawned.");
                initialSpawnData = new Spawner2D_Wall.ParticleSpawnData(0);
                numParticles = 0;
            }

            int initialCapacity = Mathf.Max(1, numParticles);
            CreateParticleBuffers(initialCapacity);

            if (numParticles > 0)
            {
                SetInitialBufferData(initialSpawnData);
            }

            spatialHash = new SpatialHash_Wall(initialCapacity);
            _propBlock_Wall = new MaterialPropertyBlock();

            if (lineRendererMaterial == null)
            {
                if (Shader.Find("Unlit/Color") != null)
                    _sharedUnlitMaterial = new Material(Shader.Find("Unlit/Color"));
                else
                    _sharedUnlitMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
            }

            UpdateAutoPlayers();
            UpdateObstacleBuffer(true); // Force initial creation/update
            SetupComputeShaderPersistent();
            BindComputeShaderBuffers();
            UpdateComputeShaderDynamicParams();
        }

        void CreateParticleBuffers(int capacity)
        {
            ReleaseParticleBuffers();
            int safeCapacity = Mathf.Max(1, capacity);

            positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            gravityScaleBuffer = ComputeHelper.CreateStructuredBuffer<float>(safeCapacity);
            collisionBuffer = ComputeHelper.CreateStructuredBuffer<int4>(safeCapacity);
            particleTypeBuffer = ComputeHelper.CreateStructuredBuffer<int2>(safeCapacity);

            sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            sortTarget_ParticleType = ComputeHelper.CreateStructuredBuffer<int2>(safeCapacity);
        }

        void ReleaseParticleBuffers()
        {
            ComputeHelper.Release(positionBuffer, predictedPositionBuffer, velocityBuffer, densityBuffer,
                gravityScaleBuffer, collisionBuffer, particleTypeBuffer, sortTarget_Position,
                sortTarget_PredicitedPosition, sortTarget_Velocity, sortTarget_ParticleType);
            positionBuffer = null; predictedPositionBuffer = null; velocityBuffer = null; densityBuffer = null;
            gravityScaleBuffer = null; collisionBuffer = null; particleTypeBuffer = null;
            sortTarget_Position = null; sortTarget_PredicitedPosition = null; sortTarget_Velocity = null; sortTarget_ParticleType = null;
            spatialHash?.Release();
            spatialHash = null;
        }

        void ReleaseObstacleBuffers()
        {
            ComputeHelper.Release(vertexBuffer, obstacleBuffer, obstacleColorsBuffer);
            vertexBuffer = null; obstacleBuffer = null; obstacleColorsBuffer = null;
        }

        void SetupComputeShaderPersistent()
        {
            if (compute == null) { Debug.LogError("Compute Shader not assigned!"); return; }
            compute.SetFloat("gravity_Wall", gravity);
            compute.SetFloat("collisionDamping_Wall", collisionDamping);
            compute.SetFloat("smoothingRadius_Wall", smoothingRadius);
            compute.SetVector("boundsSize_Wall", boundsSize);
            compute.SetFloat("yOffset_Wall", yOffset);
            compute.SetFloat("waterTargetDensity_Wall", waterTargetDensity);
            compute.SetFloat("waterPressureMultiplier_Wall", waterPressureMultiplier);
            compute.SetFloat("waterNearPressureMultiplier_Wall", waterNearPressureMultiplier);
            compute.SetFloat("waterViscosityStrength_Wall", waterViscosityStrength);
            compute.SetFloat("oilTargetDensity_Wall", oilTargetDensity);
            compute.SetFloat("oilPressureMultiplier_Wall", oilPressureMultiplier);
            compute.SetFloat("oilNearPressureMultiplier_Wall", oilNearPressureMultiplier);
            compute.SetFloat("oilViscosityStrength_Wall", oilViscosityStrength);
            float r = smoothingRadius;
            if (r > 0)
            {
                compute.SetFloat("Poly6ScalingFactor_Wall", 4f / (Mathf.PI * Mathf.Pow(r, 8)));
                compute.SetFloat("SpikyPow3ScalingFactor_Wall", 10f / (Mathf.PI * Mathf.Pow(r, 5)));
                compute.SetFloat("SpikyPow2ScalingFactor_Wall", 6f / (Mathf.PI * Mathf.Pow(r, 4)));
                compute.SetFloat("SpikyPow3DerivativeScalingFactor_Wall", 30f / (Mathf.Pow(r, 5) * Mathf.PI));
                compute.SetFloat("SpikyPow2DerivativeScalingFactor_Wall", 12f / (Mathf.Pow(r, 4) * Mathf.PI));
            }
            else { Debug.LogWarning("Smoothing radius is zero or negative."); }
            compute.SetFloat("areaToColorAroundObstacles_Wall", areaToColorAroundObstacles);
            compute.SetFloat("minDistanceToRemoveParticles_Wall", minDistanceToRemoveParticles);
            compute.SetFloat("coloredAreaAroundObstaclesDivider_Wall", coloredAreaAroundObstaclesDivider);
        }

        void BindComputeShaderBuffers()
        {
            if (compute == null) return;
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions_Wall", externalForcesKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions_Wall", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities_Wall", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Densities_Wall", densityKernel, pressureKernel, viscosityKernel);
            ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "GravityScales_Wall", externalForcesKernel);
            ComputeHelper.SetBuffer(compute, collisionBuffer, "CollisionBuffer_Wall", updatePositionKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypeBuffer_Wall", densityKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel, externalForcesKernel);
            if (spatialHash != null && spatialHash.SpatialIndices != null && spatialHash.SpatialOffsets != null && spatialHash.SpatialKeys != null)
            {
                ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices_Wall", spatialHashKernel, reorderKernel);
                ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets_Wall", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
                ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys_Wall", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
            }
            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions_Wall", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions_Wall", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities_Wall", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleType, "SortTarget_ParticleType_Wall", reorderKernel, copybackKernel);
            if (vertexBuffer != null && vertexBuffer.IsValid()) ComputeHelper.SetBuffer(compute, vertexBuffer, "VerticesBuffer_Wall", updatePositionKernel);
            if (obstacleBuffer != null && obstacleBuffer.IsValid()) ComputeHelper.SetBuffer(compute, obstacleBuffer, "ObstaclesBuffer_Wall", updatePositionKernel);
            if (obstacleColorsBuffer != null && obstacleColorsBuffer.IsValid()) ComputeHelper.SetBuffer(compute, obstacleColorsBuffer, "ObstacleColorsBuffer_Wall", updatePositionKernel);
        }

        void UpdateComputeShaderDynamicParams()
        {
            if (compute == null) return;
            compute.SetInt("numParticles_Wall", numParticles);
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            bool isPullInteraction = Input.GetMouseButton(0);
            bool isPushInteraction = Input.GetMouseButton(1);
            float currentInteractionStrength = 0;
            if (isPullInteraction) currentInteractionStrength = interactionStrength;
            else if (isPushInteraction) currentInteractionStrength = -interactionStrength;
            compute.SetVector("interactionInputPoint_Wall", mousePos);
            compute.SetFloat("interactionInputStrength_Wall", currentInteractionStrength);
            compute.SetFloat("interactionInputRadius_Wall", interactionRadius);
            compute.SetFloat("mouseGravityStrength_Wall", mouseGravityStrength);
            compute.SetFloat("mouseGravityRadius_Wall", mouseGravityRadius);
            compute.SetInt("invertMouseGravity_Wall", invertMouseGravity ? 1 : 0);
            compute.SetVector("mousePosition_Wall", mousePos);
            compute.SetInt("gKeyPressed_Wall", Input.GetKey(KeyCode.G) ? 1 : 0);
            compute.SetInt("numObstacles_Wall", _gpuObstacleDataList.Count);
        }

        void SetInitialBufferData(Spawner2D_Wall.ParticleSpawnData spawnData)
        {
            if (spawnData.positions == null || spawnData.positions.Length == 0 || numParticles == 0) return;
            if (numParticles != spawnData.positions.Length)
            {
                Debug.LogError($"SetInitialBufferData: Mismatch! numParticles ({numParticles}) vs spawnData.positions.Length ({spawnData.positions.Length}).");
                return;
            }
            positionBuffer.SetData(spawnData.positions);
            predictedPositionBuffer.SetData(spawnData.positions);
            velocityBuffer.SetData(spawnData.velocities);
            particleTypeBuffer.SetData(spawnData.particleTypes);

            float[] defaultGravityScales = new float[numParticles];
            int4[] defaultCollisionData = new int4[numParticles];

            for (int i = 0; i < numParticles; i++)
            {
                defaultGravityScales[i] = 1f;
                defaultCollisionData[i] = new int4(-1, -1, -1, -1);
            }

            gravityScaleBuffer.SetData(defaultGravityScales);
            collisionBuffer.SetData(defaultCollisionData);
        }

        void Update()
        {
            float unscaledDeltaTime = Time.deltaTime;
            float currentFrameTime = unscaledDeltaTime * timeScale;
            float maxAllowedSimDeltaTime = (maxTimestepFPS > 0) ? (1f / maxTimestepFPS) : float.PositiveInfinity;
            float cappedSimDeltaTime = Mathf.Min(currentFrameTime, maxAllowedSimDeltaTime);

            if (spawner2D != null && spawner2D.allowContinuousSpawning && numParticles < maxTotalParticles)
            {
                Spawner2D_Wall.ParticleSpawnData newParticleInfo = spawner2D.GetNewlySpawnedParticles(currentFrameTime, numParticles, maxTotalParticles);
                if (newParticleInfo.positions != null && newParticleInfo.positions.Length > 0)
                {
                    HandleAddingNewParticles(newParticleInfo);
                }
            }

            if (Time.time >= nextAutoUpdateTime)
            {
                UpdateAutoPlayers();
                nextAutoUpdateTime = Time.time + autoUpdateInterval;
            }

            UpdateObstacleBuffer(_forceObstacleBufferUpdate);
            _forceObstacleBufferUpdate = false;

            if (!isPaused && numParticles > 0)
            {
                RunSimulationFrame(cappedSimDeltaTime);
                ProcessParticleRemovals();
            }

            if (pauseNextFrame) { isPaused = true; pauseNextFrame = false; }
            HandleInput();
        }

        async void ProcessParticleRemovals()
        {
            if (numParticles == 0 || particleTypeBuffer == null || !particleTypeBuffer.IsValid()) return;

            // Request data from GPU
            // ERROR 1: sizeof(int2) is not valid C#. Use Marshal.SizeOf or UnsafeUtility.SizeOf.
            // Assuming int2 is Unity.Mathematics.int2
            var typesRequest = AsyncGPUReadback.Request(particleTypeBuffer, numParticles * Marshal.SizeOf(typeof(Unity.Mathematics.int2)), 0);

            // await System.Threading.Tasks.Task.Yield(); // Yield to allow GPU to process - this is okay but WaitForCompletion makes it synchronous from this point.
            // If this method is called from Update(), an async void can be okay, but be mindful of error handling.
            typesRequest.WaitForCompletion(); // This blocks until the GPU readback is complete.

            if (typesRequest.hasError)
            {
                Debug.LogError("GPU readback error for particle removal.");
                return;
            }

            // GetData<int2>() is correct for a buffer of int2.
            var typeData = typesRequest.GetData<Unity.Mathematics.int2>();

            List<int> indicesToRemove = new List<int>();
            // This list seems intended for scoring or logging. Let's clarify its purpose.
            // It's currently storing (flag_value, hardcoded_remover_type_0_or_2)
            // A better structure might be (originalParticleType, actualObstacleTypeThatCausedRemoval)
            // Let's assume typeData[i][0] IS the original type before removal marking (shader only sets flag).
            List<(int originalParticleType, int removingObstacleType)> removedParticleInfo = new List<(int, int)>();

            Color[] obstacleColorsArray = null;
            if (obstacleColorsBuffer != null && obstacleColorsBuffer.IsValid() && obstacleColorsBuffer.count > 0)
            {
                // 1. Create an array of the correct type and size to hold the data.
                obstacleColorsArray = new Color[obstacleColorsBuffer.count];

                // 2. Call GetData to populate the array.
                obstacleColorsBuffer.GetData(obstacleColorsArray);
            }

            int4[] collisionIndicesArray = null;
            if (collisionBuffer != null && collisionBuffer.IsValid() && collisionBuffer.count > 0)
            {
                // 1. Create an array of the correct type and size to hold the data.
                collisionIndicesArray = new int4[collisionBuffer.count];

                // 2. Call GetData to populate the array.
                collisionBuffer.GetData(collisionIndicesArray);
            }
            if (lastPlayerCount > 0)
            {
                for (int i = 0; i < numParticles; i++) // Iterate up to current numParticles
                {
                    // Assuming typeData is valid up to numParticles from the readback request
                    if (i < typeData.Length) // Safety check for array bounds
                    {
                        if (typeData[i].x > 0)
                        {
                            int particleOriginalType = mixableColors[typeData[i].x - 1]; // This is the type from the buffer
                            int particleFlag = typeData[i].y;

                            if (particleFlag >= -1) // Removed by Player (ObstacleType 0 as per HLSL mapping)
                            {
                                Color finalColour = new Color();

                                for (int j = 0; j < 4; j++)
                                {
                                    int obstacleIndex = collisionIndicesArray[i][j];
                                    if (obstacleIndex != -1)
                                    {
                                        finalColour += obstacleColorsArray[obstacleIndex];
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }

                                if (AreColorsClose(colorPalette[particleOriginalType], finalColour, 0.01f))
                                {
                                    removedParticlesPerColor[particleOriginalType]++;
                                    indicesToRemove.Add(i);
                                    // For scoring/logging: (particle's actual type, type of obstacle that removed it)
                                    removedParticleInfo.Add((particleOriginalType, particleFlag));
                                    // Debug.Log($"Particle (index {i}, type: {particleOriginalType}) marked for removal by Player (Flag 1 / ObstacleType 0).");
                                }
                            }
                            else if (particleFlag == -2) // Removed by Ventil (ObstacleType 2 as per HLSL mapping)
                            {
                                if (fluidSimSpawner != null)
                                {
                                    fluidSimSpawner.spawnRegions[particleOriginalType + 2].particlesPerSecond++;
                                    particlesReachedDestination[particleOriginalType]++;
                                    indicesToRemove.Add(i);
                                    removedParticleInfo.Add((particleOriginalType, 2));
                                }

                                // Debug.Log($"Particle (index {i}, type: {particleOriginalType}) marked for removal by Ventil (Flag 2 / ObstacleType 2).");
                            }
                        }

                    }
                    else
                    {
                        Debug.LogWarning($"ProcessParticleRemovals: Index {i} out of bounds for typeData (Length: {typeData.Length}). numParticles might be out of sync with readback size.");
                        break;
                    }
                }
            }

            if (indicesToRemove.Count == 0)
            {
                // IMPORTANT: Even if no particles are removed, flags might have been set and need resetting for the next frame
                // if those flags don't lead to removal but some other temporary state.
                // However, with current logic (flag 1 or 2 means remove), if indicesToRemove is empty, all flags were 0.
                // If flags could mean something else, you'd need to reset them here by re-writing particleTypeBuffer.
                // For now, this is fine.
                return;
            }

            // --- Compact buffers ---
            int oldNumParticles = numParticles; // numParticles before removal
            int newNumParticlesCount = oldNumParticles - indicesToRemove.Count;

            if (newNumParticlesCount <= 0)
            {
                numParticles = 0;
                // Release and create empty-ish buffers
                // These helpers need to be robust and correctly handle all buffers including sort targets and spatial hash.
                ReleaseParticleBuffers(); // This should release GPU ComputeBuffers
                CreateParticleBuffers(1); // Create with minimal capacity for GPU ComputeBuffers
                                          // _cpuParticleTypesArray (NativeArray) is managed separately (Awake/OnDestroy) and should be fine.

                if (spatialHash != null) spatialHash.Release(); // Ensure spatial hash is also handled
                spatialHash = new SpatialHash_Wall(1);

                BindComputeShaderBuffers();
                UpdateComputeShaderDynamicParams(); // Sets numParticles = 0 in shader
                Debug.Log("All particles removed.");
                return;
            }

            // Fetch all current data for active particles
            // These arrays should be sized to oldNumParticles because we're reading the entire current state
            float2[] allPositions = new float2[oldNumParticles];
            float2[] allPredicted = new float2[oldNumParticles];
            float2[] allVelocities = new float2[oldNumParticles];
            float2[] allDensities = new float2[oldNumParticles];
            float[] allGravityScales = new float[oldNumParticles];
            int4[] allCollisions = new int4[oldNumParticles];
            // typeData (NativeArray<int2>) already contains the types and flags. No need to re-read particleTypeBuffer.

            // Expensive GetData calls:
            if (positionBuffer.IsValid() && positionBuffer.count >= oldNumParticles) positionBuffer.GetData(allPositions, 0, 0, oldNumParticles); else Debug.LogError("PositionBuffer invalid or too small for GetData");
            if (predictedPositionBuffer.IsValid() && predictedPositionBuffer.count >= oldNumParticles) predictedPositionBuffer.GetData(allPredicted, 0, 0, oldNumParticles); else Debug.LogError("PredictedPositionBuffer invalid or too small for GetData");
            if (velocityBuffer.IsValid() && velocityBuffer.count >= oldNumParticles) velocityBuffer.GetData(allVelocities, 0, 0, oldNumParticles); else Debug.LogError("VelocityBuffer invalid or too small for GetData");
            if (densityBuffer.IsValid() && densityBuffer.count >= oldNumParticles) densityBuffer.GetData(allDensities, 0, 0, oldNumParticles); else Debug.LogError("DensityBuffer invalid or too small for GetData");
            if (gravityScaleBuffer.IsValid() && gravityScaleBuffer.count >= oldNumParticles) gravityScaleBuffer.GetData(allGravityScales, 0, 0, oldNumParticles); else Debug.LogError("GravityScaleBuffer invalid or too small for GetData");
            if (collisionBuffer.IsValid() && collisionBuffer.count >= oldNumParticles) collisionBuffer.GetData(allCollisions, 0, 0, oldNumParticles); else Debug.LogError("CollisionBuffer invalid or too small for GetData");


            // Create lists for surviving particles
            List<float2> keptPositions = new List<float2>(newNumParticlesCount);
            List<float2> keptPredicted = new List<float2>(newNumParticlesCount);
            List<float2> keptVelocities = new List<float2>(newNumParticlesCount);
            List<float2> keptDensities = new List<float2>(newNumParticlesCount);
            List<float> keptGravityScales = new List<float>(newNumParticlesCount);
            List<int4> keptCollisions = new List<int4>(newNumParticlesCount);
            List<int2> keptParticleTypesAndFlags = new List<int2>(newNumParticlesCount); // Store int2 (type, flag=0)

            int removedIdxIter = 0; // Iterator for sorted indicesToRemove
            for (int i = 0; i < oldNumParticles; i++)
            {
                bool isRemoved = false;
                if (removedIdxIter < indicesToRemove.Count && i == indicesToRemove[removedIdxIter])
                {
                    isRemoved = true;
                    removedIdxIter++;
                }

                if (!isRemoved)
                {
                    keptPositions.Add(allPositions[i]);
                    keptPredicted.Add(allPredicted[i]);
                    keptVelocities.Add(allVelocities[i]);
                    keptDensities.Add(allDensities[i]);
                    keptGravityScales.Add(allGravityScales[i]);
                    keptCollisions.Add(allCollisions[i]);
                    // Use type from typeData (which was read from particleTypeBuffer) and reset flag to 0.
                    // Assuming typeData[i].x is the original type because the shader only set the flag.
                    if (i < typeData.Length)
                    { // Additional safety for typeData access
                        keptParticleTypesAndFlags.Add(new int2(typeData[i].x, -1));
                    }
                    else
                    {
                        // This case implies a mismatch between numParticles used for GetData for primary buffers
                        // and the length of typeData. Should not happen if readback request size was correct.
                        // Add a dummy or default if this happens, and log an error.
                        keptParticleTypesAndFlags.Add(new int2(0, -1)); // Default particle type if data is missing
                        Debug.LogError($"ProcessParticleRemovals: Mismatch accessing typeData at index {i}");
                    }
                }
            }

            numParticles = newNumParticlesCount; // Update the global particle count

            // At this point, GPU buffers are still full size (e.g., maxTotalParticles).
            // We are just using fewer slots by setting data for 'numParticles'.
            // This avoids frequent Release/Create of GPU buffers if they are already at max capacity.
            // The CreateParticleBuffers(Mathf.Max(1, numParticles)) call in your original was for resizing down,
            // which can be okay but also means reallocations. If buffers are kept at maxTotalParticles,
            // we just need to SetData for the new 'numParticles'.

            if (numParticles > 0)
            {
                // Check if current buffers are large enough for 'numParticles'. They should be if sized to maxTotalParticles.
                // If you choose to resize buffers down to numParticles:
                // ReleaseParticleBuffers(); 
                // CreateParticleBuffers(Mathf.Max(1, numParticles)); // This assumes CreateParticleBuffers also handles SortTargets appropriately
                // spatialHash = new SpatialHash(Mathf.Max(1, numParticles));
                // BindComputeShaderBuffers();

                // If buffers are kept at maxTotalParticles capacity (preferred for stability):
                positionBuffer.SetData(keptPositions); // SetData will only write up to keptPositions.Count
                predictedPositionBuffer.SetData(keptPredicted);
                velocityBuffer.SetData(keptVelocities);
                densityBuffer.SetData(keptDensities);
                gravityScaleBuffer.SetData(keptGravityScales);
                collisionBuffer.SetData(keptCollisions);
                particleTypeBuffer.SetData(keptParticleTypesAndFlags); // Set compacted types with flags reset
            }
            // If numParticles is 0, no SetData needed. Dispatches will use numParticles = 0.

            // Update numParticles on the compute shader
            UpdateComputeShaderDynamicParams();

            // Debug.Log($"Particles removed. New count: {numParticles}");
        }

        // Fallback CPU-based buffer resizing (used if Graphics.CopyBuffer is problematic)
        ComputeBuffer FallbackResizeAndAppendBuffer<T>(ComputeBuffer buffer, int oldCount, T[] newDataToAppend) where T : struct
        {
            int newElementsCount = (newDataToAppend == null) ? 0 : newDataToAppend.Length;
            int newTotalCount = oldCount + newElementsCount;

            if (newElementsCount == 0 && buffer != null && buffer.IsValid() && buffer.count == oldCount)
            {
                return buffer; // No change needed if no new elements and old buffer matches oldCount
            }
            if (newTotalCount <= 0 && oldCount == 0 && newElementsCount == 0)
            {
                buffer?.Release();
                return new ComputeBuffer(1, Marshal.SizeOf(typeof(T)), ComputeBufferType.Structured);
            }

            ComputeBuffer newBuffer = new ComputeBuffer(Mathf.Max(1, newTotalCount), Marshal.SizeOf(typeof(T)), ComputeBufferType.Structured);

            if (oldCount > 0 && buffer != null && buffer.IsValid())
            {
                int countToRead = Mathf.Min(oldCount, buffer.count);
                if (countToRead > 0 && newBuffer.count >= countToRead)
                {
                    T[] oldData = new T[countToRead];
                    buffer.GetData(oldData, 0, 0, countToRead);
                    newBuffer.SetData(oldData, 0, 0, countToRead);
                }
                else if (countToRead > 0)
                { // newBuffer is too small, indicates logic error
                    Debug.LogError($"FallbackResizeAndAppendBuffer: newBuffer (count {newBuffer.count}) is smaller than old data to copy ({countToRead}). This should not happen.");
                }
            }

            if (newElementsCount > 0 && newDataToAppend != null)
            {
                if (newBuffer.count >= oldCount + newElementsCount)
                {
                    newBuffer.SetData(newDataToAppend, 0, oldCount, newElementsCount);
                }
                else
                {
                    Debug.LogError($"FallbackResizeAndAppendBuffer: newBuffer (count {newBuffer.count}) too small to append {newElementsCount} elements at offset {oldCount}. Required: {oldCount + newElementsCount}");
                }
            }

            buffer?.Release();
            return newBuffer;
        }

        void HandleAddingNewParticles(Spawner2D_Wall.ParticleSpawnData newParticleData)
        {
            int newSpawnCount = newParticleData.positions.Length;
            if (newSpawnCount == 0) return;

            int oldNumParticles = numParticles;
            numParticles += newSpawnCount;

            // --- USING FALLBACK RESIZE METHOD TO RESOLVE COMPILER ERRORS ---
            positionBuffer = FallbackResizeAndAppendBuffer(positionBuffer, oldNumParticles, newParticleData.positions);
            predictedPositionBuffer = FallbackResizeAndAppendBuffer(predictedPositionBuffer, oldNumParticles, newParticleData.positions);
            velocityBuffer = FallbackResizeAndAppendBuffer(velocityBuffer, oldNumParticles, newParticleData.velocities);
            particleTypeBuffer = FallbackResizeAndAppendBuffer(particleTypeBuffer, oldNumParticles, newParticleData.particleTypes);

            float[] newGravityScales = new float[newSpawnCount]; for (int i = 0; i < newSpawnCount; ++i) newGravityScales[i] = 1f;
            gravityScaleBuffer = FallbackResizeAndAppendBuffer(gravityScaleBuffer, oldNumParticles, newGravityScales);

            int4[] newCollisionData = new int4[newSpawnCount]; for (int i = 0; i < newSpawnCount; ++i) newCollisionData[i] = new int4(-1, -1, -1, -1);
            collisionBuffer = FallbackResizeAndAppendBuffer(collisionBuffer, oldNumParticles, newCollisionData);

            densityBuffer = FallbackResizeAndAppendBuffer(densityBuffer, oldNumParticles, new float2[newSpawnCount]);

            sortTarget_Position = FallbackResizeAndAppendBuffer(sortTarget_Position, oldNumParticles, new float2[newSpawnCount]);
            sortTarget_PredicitedPosition = FallbackResizeAndAppendBuffer(sortTarget_PredicitedPosition, oldNumParticles, new float2[newSpawnCount]);
            sortTarget_Velocity = FallbackResizeAndAppendBuffer(sortTarget_Velocity, oldNumParticles, new float2[newSpawnCount]);
            sortTarget_ParticleType = FallbackResizeAndAppendBuffer(sortTarget_ParticleType, oldNumParticles, new int2[newSpawnCount]);
            // --- END OF USING FALLBACK ---

            spatialHash?.Release();
            spatialHash = new SpatialHash_Wall(numParticles);

            BindComputeShaderBuffers();
            UpdateComputeShaderDynamicParams();
        }

        void RunSimulationFrame(float deltaTimeForFrame)
        {
            if (numParticles == 0 || compute == null) return;
            float timeStepPerIteration = deltaTimeForFrame / Mathf.Max(1, iterationsPerFrame);
            compute.SetFloat("deltaTime_Wall", timeStepPerIteration);
            UpdateComputeShaderDynamicParams();
            for (int i = 0; i < iterationsPerFrame; i++)
            {
                RunSimulationStep();
                SimulationStepCompleted?.Invoke();
            }
        }

        void RunSimulationStep()
        {
            UpdateCurrentsBuffer();
            if (numParticles == 0 || compute == null) return;
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: externalForcesKernel);
            RunSpatialHashPasses();
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: densityKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updatePositionKernel);
            AsyncGPUReadback.WaitAllRequests();
        }

        void RunSpatialHashPasses()
        {
            if (numParticles == 0 || spatialHash == null || compute == null) return;
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: spatialHashKernel);
            spatialHash.Run(); // Assuming SebLague's SpatialHash.Run() is appropriate.
                               // If it requires particle count for its internal sort/dispatch, it might be: spatialHash.Run(numParticles);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackKernel);
        }

        void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.Space)) isPaused = !isPaused;
            if (Input.GetKeyDown(KeyCode.RightArrow)) { isPaused = false; pauseNextFrame = true; }
            if (Input.GetKeyDown(KeyCode.R))
            {
                isPaused = true;
                Debug.Log("Resetting simulation...");
                if (spawner2D != null)
                {
                    initialSpawnData = spawner2D.GetSpawnData();
                    int newNumParticles = initialSpawnData.positions?.Length ?? 0;
                    ReleaseParticleBuffers();
                    ReleaseObstacleBuffers();
                    _obstacleCache.Clear();
                    _gpuObstacleDataList.Clear();
                    _gpuVerticesData.Clear();
                    _gpuObstacleColorsData.Clear();

                    int capacity = Mathf.Max(1, newNumParticles);
                    CreateParticleBuffers(capacity);
                    spatialHash = new SpatialHash_Wall(capacity);
                    numParticles = newNumParticles;
                    if (numParticles > 0) SetInitialBufferData(initialSpawnData);
                    UpdateAutoPlayers();
                    UpdateObstacleBuffer(true);
                }
                else
                {
                    ReleaseParticleBuffers(); ReleaseObstacleBuffers(); _obstacleCache.Clear();
                    _gpuObstacleDataList.Clear(); _gpuVerticesData.Clear(); _gpuObstacleColorsData.Clear();
                    CreateParticleBuffers(1); spatialHash = new SpatialHash_Wall(1); numParticles = 0;
                    UpdateObstacleBuffer(true);
                }
                BindComputeShaderBuffers();
                UpdateComputeShaderDynamicParams();
                Debug.Log("Simulation Reset. Particle count: " + numParticles);
            }
        }

        void UpdateCurrentsBuffer()
        {
            currentVertices.Clear();
            var currents = FindObjectsOfType<Current>();
            List<CurrentData> currentDataList = new List<CurrentData>();

            foreach (var current in currents)
            {
                Vector2[] points = current.GetWorldPoints();
                if (points.Length >= 2)
                {
                    currentDataList.Add(new CurrentData
                    {
                        vertexStart = currentVertices.Count,
                        vertexCount = points.Length,
                        maxVelocity = current.maxVelocity,
                        width = current.width,
                        linearFactor = current.linearFactor
                    });
                    currentVertices.AddRange(points);
                }
            }

            ComputeHelper.CreateStructuredBuffer(ref currentVerticesBuffer, currentVertices);
            ComputeHelper.CreateStructuredBuffer(ref currentsBuffer, currentDataList);

            compute.SetBuffer(updatePositionKernel, "CurrentsBuffer_Wall", currentsBuffer);
            compute.SetBuffer(updatePositionKernel, "CurrentVerticesBuffer_Wall", currentVerticesBuffer);
            compute.SetInt("numCurrents_Wall", currentDataList.Count);
        }

        void UpdateAutoPlayers()
        {
            if (!Application.isPlaying) return;
            var allGameObjectsInScene = GameObject.FindObjectsOfType<GameObject>();
            HashSet<GameObject> currentPlayersInScene = new HashSet<GameObject>();
            HashSet<GameObject> currentObstaclesInScene = new HashSet<GameObject>();
            HashSet<GameObject> currentVentilsInScene = new HashSet<GameObject>();

            foreach (GameObject go in allGameObjectsInScene)
            {
                if (!go.activeInHierarchy) continue;
                if (go.name.Contains("PharusPlayer")) currentPlayersInScene.Add(go);
                else if (go.name.Contains("TouchPlayer")) currentPlayersInScene.Add(go);
                else if (go.name.Contains("Obstacle")) currentObstaclesInScene.Add(go);
                else if (go.name.Contains("Ventil")) currentVentilsInScene.Add(go);
            }

            bool listActuallyChanged = false;
            List<GameObject> newMasterObstaclesList = new List<GameObject>();
            List<int> assignedIndices = new List<int>();

            System.Action<HashSet<GameObject>> processSet = (set) =>
            {
                foreach (GameObject go in set)
                {
                    if (!newMasterObstaclesList.Contains(go)) newMasterObstaclesList.Add(go);
                    if (!_obstacleCache.ContainsKey(go))
                    {
                        var info = new CachedObstacleInfo { transform = go.transform };
                        info.polyCol = go.GetComponent<PolygonCollider2D>();
                        if (!go.TryGetComponent<LineRenderer>(out info.lineRend))
                        {
                            info.lineRend = go.AddComponent<LineRenderer>();
                            info.lineRend.useWorldSpace = true;
                        }
                        // Ensure material on LineRenderer (new or existing)
                        if (info.lineRend != null)
                        {
                            info.lineRend.sharedMaterial = lineRendererMaterial != null ? lineRendererMaterial : _sharedUnlitMaterial;
                        }
                        _obstacleCache[go] = info;
                        listActuallyChanged = true;
                    }
                }
            };
            processSet(currentObstaclesInScene);
            processSet(currentVentilsInScene);
            processSet(currentPlayersInScene);

            List<GameObject> toRemoveFromCache = _obstacleCache.Keys.Where(go => go == null || !go.activeInHierarchy || !newMasterObstaclesList.Contains(go)).ToList();
            foreach (var go in toRemoveFromCache)
            {
                _obstacleCache.Remove(go);
                listActuallyChanged = true;
            }

            if (listActuallyChanged || obstacles.Count != newMasterObstaclesList.Count)
            { // Check if underlying list needs update
                obstacles = newMasterObstaclesList;
                _forceObstacleBufferUpdate = true;
            }

            int GetPriority(GameObject go, HashSet<GameObject> players, HashSet<GameObject> staticObs, HashSet<GameObject> ventils)
            {
                if (players.Contains(go)) return 0;
                if (staticObs.Contains(go)) return 1;
                if (ventils.Contains(go)) return 2;
                return 3;
            }
            // Always re-sort the current obstacles list. If it was re-assigned, this sorts the new list.
            // If not re-assigned but items were removed from cache (which implies they should be removed from obstacles too),
            // this sort will operate on the potentially stale 'obstacles' list before it's fully synced with 'newMasterObstaclesList'
            // if 'listActuallyChanged' was only due to cache removal but not count difference.
            // A safer approach is to always build 'obstacles' fresh from 'newMasterObstaclesList' after cache management.
            obstacles = newMasterObstaclesList.OrderBy(o => GetPriority(o, currentPlayersInScene, currentObstaclesInScene, currentVentilsInScene))
                                             .ThenBy(o => o.GetInstanceID())
                                             .ToList();
            if (listActuallyChanged) _forceObstacleBufferUpdate = true; // Ensure if the list order/content changed, buffers update.

            List<GameObject> sortedPlayersForColoring = obstacles
                .Where(o => _obstacleCache.ContainsKey(o) && (o.name.Contains("PharusPlayer") || o.name.Contains("TouchPlayer")))
                .ToList();

            if (listActuallyChanged || sortedPlayersForColoring.Count != lastPlayerCount)
            {
                Dictionary<GameObject, int> tempPlayerColors = new Dictionary<GameObject, int>();

                int numPaletteColors = colorPalette.Count;
                int colorLimit = Mathf.Min(maxPlayerColors, numPaletteColors);

                // 1. Categorize players from sortedPlayersForColoring
                List<KeyValuePair<GameObject, int>> existingPlayersWithOldColor = new List<KeyValuePair<GameObject, int>>();
                List<GameObject> newPlayersInSortedOrder = new List<GameObject>();

                foreach (GameObject player in sortedPlayersForColoring)
                {
                    // playerColors here refers to its state *before* this update
                    if (playerColors.TryGetValue(player, out int oldColorIndex))
                    {
                        existingPlayersWithOldColor.Add(new KeyValuePair<GameObject, int>(player, oldColorIndex));
                    }
                    else
                    {
                        newPlayersInSortedOrder.Add(player); // Order of new players preserved from sortedPlayersForColoring
                    }
                }

                // 2. Sort existing players by their old color index
                existingPlayersWithOldColor.Sort((a, b) => a.Value.CompareTo(b.Value));

                // 3.a. Assign new colors to sorted existing players
                foreach (var playerEntry in existingPlayersWithOldColor)
                {
                    GameObject player = playerEntry.Key;
                    int oldColorIndex = playerEntry.Value;
                    tempPlayerColors[player] = oldColorIndex % colorLimit;
                    assignedIndices.Add(oldColorIndex);
                }

                int nextColorIndex = lastPlayerCount;
                // 3.b. Assign new colors to new players
                foreach (GameObject player in newPlayersInSortedOrder)
                {
                    bool uniqueSlotFound = false;
                    for (int k = 0; k < colorLimit; k++)
                    {
                        if (!assignedIndices.Contains(k))
                        {
                            tempPlayerColors[player] = k;
                            assignedIndices.Add(k); // This unique slot is now taken
                            uniqueSlotFound = true;
                            break;
                        }
                    }

                    // IF no slot was found, simply use next available color
                    if (!uniqueSlotFound)
                    {
                        tempPlayerColors[player] = nextColorIndex % maxPlayerColors;
                        nextColorIndex++;
                    }
                }
                _forceObstacleBufferUpdate = true;
                playerColors = tempPlayerColors; // Update the main playerColors dictionary

                foreach (GameObject player in playerColors.Keys)
                {
                    Color colorToUse = colorPalette[playerColors[player]];
                    player.GetComponentInChildren<PlayerColor>().UpdateColor(colorToUse);
                }

                // Update lastPlayerCount based on the number of players considered for coloring
                lastPlayerCount = sortedPlayersForColoring.Count;
                mixableColors.Clear();

                assignedIndices = assignedIndices.OrderBy(i => i).ToList();
                for (int i = 0; i < colorPalette.Count; i++)
                {
                    if (assignedIndices.Contains(i))
                    {
                        mixableColors.Add(i);
                    }
                    else
                    {
                        // 0=Red, 1=Yellow, 2=Blue, 3=Orange, 4=Violet, 5=LimeGreen, 6=RedOrange, 7=YellowOrange, 8=RedViolet, 9=BlueViolet, 10=YellowGreen, 11=BlueGreen
                        if ((i == 3 && assignedIndices.Contains(0) && assignedIndices.Contains(1) && (maxPlayerColors <= 3 || lastPlayerCount <= 3)) || //Only assign if not player
                            (i == 4 && assignedIndices.Contains(0) && assignedIndices.Contains(2) && (maxPlayerColors <= 3 || lastPlayerCount <= 3)) || //Only assign if not player
                            (i == 5 && assignedIndices.Contains(1) && assignedIndices.Contains(2) && (maxPlayerColors <= 3 || lastPlayerCount <= 3)) || //Only assign if not player
                            (i == 6 && assignedIndices.Contains(0) && assignedIndices.Contains(3)) ||
                            (i == 7 && assignedIndices.Contains(1) && assignedIndices.Contains(3)) ||
                            (i == 8 && assignedIndices.Contains(0) && assignedIndices.Contains(4)) ||
                            (i == 9 && assignedIndices.Contains(2) && assignedIndices.Contains(4)) ||
                            (i == 10 && assignedIndices.Contains(1) && assignedIndices.Contains(5)) ||
                            (i == 11 && assignedIndices.Contains(2) && assignedIndices.Contains(5)))
                        {
                            mixableColors.Add(i);
                        }
                        else
                        {
                            mixableColors.Add(-1); // Using new Color(-1,-1,-1,-1) as a distinct invalid/placeholder
                        }
                    }
                }

                mixableColorsForShader.Clear();
                int currentIndex = 0;
                if (assignedIndices.Count > 0)
                {
                    for (int i = 0; i < colorPalette.Count; i++)
                    {
                        if (mixableColors[i] == -1)
                        {
                            mixableColors[i] = assignedIndices[currentIndex];
                            currentIndex++;
                            currentIndex = currentIndex % assignedIndices.Count;
                        }
                        mixableColorsForShader.Add(colorPalette[mixableColors[i]]);
                    }
                }
                else
                {
                    for (int i = 0; i < colorPalette.Count; i++)
                    {
                        mixableColorsForShader.Add(colorSymbolizingNoPlayer);
                    }
                }
            }
        }

        void UpdateObstacleBuffer(bool forceBufferRecreation = false)
        {
            if (!Application.isPlaying && compute == null) return;

            _gpuVerticesData.Clear();
            _gpuObstacleDataList.Clear();
            _gpuObstacleColorsData.Clear();

            if (_propBlock_Wall == null) _propBlock_Wall = new MaterialPropertyBlock();
            int currentVertexStartIndex = 0;
            bool anyObstacleTransformChanged = false;

            foreach (GameObject obstacleGO in obstacles)
            {
                if (!_obstacleCache.TryGetValue(obstacleGO, out CachedObstacleInfo cachedInfo)) continue;
                try
                {
                    if (cachedInfo.transform.hasChanged) { anyObstacleTransformChanged = true; cachedInfo.transform.hasChanged = false; }
                }
                catch
                {
                    continue;
                }


                PolygonCollider2D polyCol = cachedInfo.polyCol; LineRenderer lr = cachedInfo.lineRend;
                if (polyCol == null || lr == null || polyCol.points.Length < 2) continue;

                Material currentMat = lineRendererMaterial != null ? lineRendererMaterial : _sharedUnlitMaterial;
                if (lr.sharedMaterial != currentMat) lr.sharedMaterial = currentMat;

                var localPoints = polyCol.points;
                lr.positionCount = localPoints.Length; lr.loop = localPoints.Length > 2;
                int vertexCountForThisObstacle = localPoints.Length;
                Vector3[] worldLinePoints = new Vector3[vertexCountForThisObstacle];

                for (int i = 0; i < vertexCountForThisObstacle; ++i)
                {
                    Vector2 worldVert = cachedInfo.transform.TransformPoint(localPoints[i] + polyCol.offset);
                    _gpuVerticesData.Add(worldVert); worldLinePoints[i] = worldVert;
                }
                lr.SetPositions(worldLinePoints);

                int obsType = 1;
                if (obstacleGO.name.Contains("PharusPlayer") || obstacleGO.name.Contains("TouchPlayer")) obsType = 0; else if (obstacleGO.name.Contains("Ventil")) obsType = 2;
                _gpuObstacleDataList.Add(new ObstacleData
                {
                    centre = cachedInfo.transform.TransformPoint(polyCol.offset),
                    vertexStart = currentVertexStartIndex,
                    vertexCount = vertexCountForThisObstacle,
                    lineWidth = obstacleLineWidth,
                    obstacleType = obsType
                });
                currentVertexStartIndex += vertexCountForThisObstacle;

                Color displayColor = obstacleLineColor;
                if (obsType == 1) { displayColor = Color.white; }
                else if (obsType == 2) { displayColor = Color.gray; }

                if (obsType == 0 && playerColors.TryGetValue(obstacleGO, out int pColor)) displayColor = new Color(colorPalette[pColor].r, colorPalette[pColor].g, colorPalette[pColor].b, 0.0f); //colorPalette[pColor];
                _propBlock_Wall.SetColor("_Color", displayColor); lr.SetPropertyBlock(_propBlock_Wall);
                _gpuObstacleColorsData.Add(displayColor);
                lr.startWidth = obstacleLineWidth; lr.endWidth = obstacleLineWidth;
            }

            bool buffersNeedStructuralUpdate = forceBufferRecreation || anyObstacleTransformChanged; // Simplified: update data if any transform changed or forced

            // Vertex Buffer
            if (vertexBuffer == null || !vertexBuffer.IsValid() || vertexBuffer.count != _gpuVerticesData.Count || forceBufferRecreation)
            {
                ComputeHelper.Release(vertexBuffer);
                vertexBuffer = ComputeHelper.CreateStructuredBuffer(_gpuVerticesData.Count > 0 ? _gpuVerticesData.ToArray() : new Vector2[] { Vector2.zero });
                buffersNeedStructuralUpdate = true;
            }
            else if (_gpuVerticesData.Count > 0 && anyObstacleTransformChanged)
            {
                vertexBuffer.SetData(_gpuVerticesData);
            }
            else if (_gpuVerticesData.Count == 0 && (vertexBuffer == null || !vertexBuffer.IsValid() || vertexBuffer.count != 1))
            {
                ComputeHelper.Release(vertexBuffer); vertexBuffer = ComputeHelper.CreateStructuredBuffer<Vector2>(1);
                vertexBuffer.SetData(new Vector2[] { Vector2.zero }); buffersNeedStructuralUpdate = true;
            }

            // Obstacle Data Buffer
            if (obstacleBuffer == null || !obstacleBuffer.IsValid() || obstacleBuffer.count != _gpuObstacleDataList.Count || forceBufferRecreation)
            {
                ComputeHelper.Release(obstacleBuffer);
                obstacleBuffer = ComputeHelper.CreateStructuredBuffer(_gpuObstacleDataList.Count > 0 ? _gpuObstacleDataList.ToArray() : new ObstacleData[] { new ObstacleData() });
                buffersNeedStructuralUpdate = true;
            }
            else if (_gpuObstacleDataList.Count > 0 && (anyObstacleTransformChanged || forceBufferRecreation))
            {
                obstacleBuffer.SetData(_gpuObstacleDataList);
            }
            else if (_gpuObstacleDataList.Count == 0 && (obstacleBuffer == null || !obstacleBuffer.IsValid() || obstacleBuffer.count != 1))
            {
                ComputeHelper.Release(obstacleBuffer); obstacleBuffer = ComputeHelper.CreateStructuredBuffer<ObstacleData>(1);
                obstacleBuffer.SetData(new ObstacleData[] { new ObstacleData() }); buffersNeedStructuralUpdate = true;
            }

            // Obstacle Colors Buffer
            if (obstacleColorsBuffer == null || !obstacleColorsBuffer.IsValid() || obstacleColorsBuffer.count != _gpuObstacleColorsData.Count || forceBufferRecreation)
            {
                ComputeHelper.Release(obstacleColorsBuffer);
                obstacleColorsBuffer = ComputeHelper.CreateStructuredBuffer(_gpuObstacleColorsData.Count > 0 ? _gpuObstacleColorsData.ToArray() : new Color[] { Color.clear });
                buffersNeedStructuralUpdate = true;
            }
            else if (_gpuObstacleColorsData.Count > 0 && forceBufferRecreation)
            {
                obstacleColorsBuffer.SetData(_gpuObstacleColorsData);
            }
            else if (_gpuObstacleColorsData.Count == 0 && (obstacleColorsBuffer == null || !obstacleColorsBuffer.IsValid() || obstacleColorsBuffer.count != 1))
            {
                ComputeHelper.Release(obstacleColorsBuffer); obstacleColorsBuffer = ComputeHelper.CreateStructuredBuffer<Color>(1);
                obstacleColorsBuffer.SetData(new Color[] { Color.clear }); buffersNeedStructuralUpdate = true;
            }

            if (compute != null && buffersNeedStructuralUpdate)
            { // If buffers were re-created, they need to be re-bound
                if (vertexBuffer != null && vertexBuffer.IsValid()) compute.SetBuffer(updatePositionKernel, "VerticesBuffer_Wall", vertexBuffer);
                if (obstacleBuffer != null && obstacleBuffer.IsValid()) compute.SetBuffer(updatePositionKernel, "ObstaclesBuffer_Wall", obstacleBuffer);
                if (obstacleColorsBuffer != null && obstacleColorsBuffer.IsValid()) compute.SetBuffer(updatePositionKernel, "ObstacleColorsBuffer_Wall", obstacleColorsBuffer);
            }
            if (compute != null) compute.SetInt("numObstacles_Wall", _gpuObstacleDataList.Count);
        }

        void OnDestroy()
        {
            ComputeHelper.Release(currentsBuffer, currentVerticesBuffer);
            ReleaseParticleBuffers();
            ReleaseObstacleBuffers();
            if (_sharedUnlitMaterial != null && lineRendererMaterial == null)
            {
                if (Application.isEditor && !Application.isPlaying) DestroyImmediate(_sharedUnlitMaterial);
                else Destroy(_sharedUnlitMaterial);
            }
            if (obstacles != null)
            {
                foreach (var obstacleGO in obstacles.Where(o => o != null))
                {
                    var lr = obstacleGO.GetComponent<LineRenderer>();
                    if (lr != null) Destroy(lr);
                }
            }
        }
        public bool AreColorsClose(Color color1, Color color2, float tolerance, bool compareAlpha = false)
        {
            color2.a = 1.0f;
            return color1.Equals(color2);
        }
    }
}