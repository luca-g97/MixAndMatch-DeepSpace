using Seb.Helpers; // Assuming this namespace contains ComputeHelper and SpatialHash
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices; // For Marshal.SizeOf
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering; // Required for AsyncGPUReadback

namespace Seb.Fluid2D.Simulation
{
    public class FluidSim2D : MonoBehaviour
    {
        public event System.Action SimulationStepCompleted;

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
        public Spawner2D spawner2D;

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
        ComputeBuffer sortTarget_Collision;

        ComputeBuffer vertexBuffer;
        ComputeBuffer compactionInfoBuffer;

        public ComputeBuffer obstacleBuffer { get; private set; }
        public ComputeBuffer obstacleColorsBuffer { get; private set; }

        SpatialHash spatialHash;

        const int externalForcesKernel = 0;
        const int spatialHashKernel = 1;
        const int reorderKernel = 2;
        const int copybackKernel = 3;
        const int densityKernel = 4;
        const int pressureKernel = 5;
        const int viscosityKernel = 6;
        const int updatePositionKernel = 7;
        const int resetCompactionCounterKernel = 8;
        const int compactAndMoveKernel = 9;

        bool isPaused;
        Spawner2D.ParticleSpawnData initialSpawnData;
        bool pauseNextFrame;
        public int numParticles { get; private set; }

        [Header("Obstacles")]
        public List<GameObject> obstacles = new List<GameObject>();
        [Min(0)] public float areaToColorAroundObstacles = 1.0f;
        [Min(0)] public float minDistanceToRemoveParticles = 0.2f;
        [Min(0)] public float coloredAreaAroundObstaclesDivider = 0.05f;

        private MaterialPropertyBlock _propBlock;
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
            // Primary Colors (Unchanged)
            /* 0: Red */      new Color(0.9f, 0.0f, 0.4f),
            /* 1: Yellow */   new Color(1.0f, 0.9f, 0.0f),
            /* 2: Blue */     new Color(0.0f, 0.4f, 0.7f),

            // Secondary Colors (Recalculated as Averages)
            /* 3: Orange (R+Y) */ new Color(0.95f, 0.45f, 0.2f),
            /* 4: Violet (R+B) */ new Color(0.45f, 0.2f, 0.55f),
            /* 5: Green (Y+B) */  new Color(0.5f, 0.65f, 0.35f),

            // Tertiary Colors (Recalculated as Averages)
            /* 6: Red-Orange (R+O) */    new Color(0.925f, 0.225f, 0.3f),
            /* 7: Yellow-Orange (Y+O) */ new Color(0.975f, 0.675f, 0.1f),
            /* 8: Red-Violet (R+V) */    new Color(0.675f, 0.1f, 0.475f),
            /* 9: Blue-Violet (B+V) */   new Color(0.225f, 0.3f, 0.625f),
            /* 10: Yellow-Green (Y+G) */ new Color(0.75f, 0.775f, 0.175f),
            /* 11: Blue-Green (B+G) */   new Color(0.25f, 0.525f, 0.525f)
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
                initialSpawnData = new Spawner2D.ParticleSpawnData(0);
                numParticles = 0;
            }

            int initialCapacity = Mathf.Max(1, numParticles);
            CreateParticleBuffers(initialCapacity);
            compactionInfoBuffer = ComputeHelper.CreateStructuredBuffer<uint>(1);

            if (numParticles > 0)
            {
                SetInitialBufferData(initialSpawnData);
            }

            spatialHash = new SpatialHash(initialCapacity);
            _propBlock = new MaterialPropertyBlock();

            if (lineRendererMaterial == null)
            {
                if (Shader.Find("Unlit/Color") != null)
                    _sharedUnlitMaterial = new Material(Shader.Find("Unlit/Color"));
                else
                    _sharedUnlitMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
            }

            // Initialize current buffers with a minimal size to ensure they always exist.
            currentsBuffer = ComputeHelper.CreateStructuredBuffer<CurrentData>(1);
            currentVerticesBuffer = ComputeHelper.CreateStructuredBuffer<Vector2>(1);

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
            sortTarget_Collision = ComputeHelper.CreateStructuredBuffer<int4>(safeCapacity);
        }

        void ReleaseParticleBuffers()
        {
            ComputeHelper.Release(positionBuffer, predictedPositionBuffer, velocityBuffer, densityBuffer,
                gravityScaleBuffer, collisionBuffer, particleTypeBuffer, sortTarget_Position,
                sortTarget_PredicitedPosition, sortTarget_Velocity, sortTarget_ParticleType,
                compactionInfoBuffer, sortTarget_Collision);
            positionBuffer = null; predictedPositionBuffer = null; velocityBuffer = null; densityBuffer = null;
            gravityScaleBuffer = null; collisionBuffer = null; particleTypeBuffer = null; sortTarget_Position = null;
            sortTarget_PredicitedPosition = null; sortTarget_Velocity = null; sortTarget_ParticleType = null;
            compactionInfoBuffer = null; sortTarget_Collision = null;
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
            compute.SetFloat("gravity", gravity);
            compute.SetFloat("collisionDamping", collisionDamping);
            compute.SetFloat("smoothingRadius", smoothingRadius);
            compute.SetVector("boundsSize", boundsSize);
            compute.SetFloat("yOffset", yOffset);
            compute.SetFloat("waterTargetDensity", waterTargetDensity);
            compute.SetFloat("waterPressureMultiplier", waterPressureMultiplier);
            compute.SetFloat("waterNearPressureMultiplier", waterNearPressureMultiplier);
            compute.SetFloat("waterViscosityStrength", waterViscosityStrength);
            compute.SetFloat("oilTargetDensity", oilTargetDensity);
            compute.SetFloat("oilPressureMultiplier", oilPressureMultiplier);
            compute.SetFloat("oilNearPressureMultiplier", oilNearPressureMultiplier);
            compute.SetFloat("oilViscosityStrength", oilViscosityStrength);
            float r = smoothingRadius;
            if (r > 0)
            {
                compute.SetFloat("Poly6ScalingFactor", 4f / (Mathf.PI * Mathf.Pow(r, 8)));
                compute.SetFloat("SpikyPow3ScalingFactor", 10f / (Mathf.PI * Mathf.Pow(r, 5)));
                compute.SetFloat("SpikyPow2ScalingFactor", 6f / (Mathf.PI * Mathf.Pow(r, 4)));
                compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30f / (Mathf.Pow(r, 5) * Mathf.PI));
                compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12f / (Mathf.Pow(r, 4) * Mathf.PI));
            }
            else { Debug.LogWarning("Smoothing radius is zero or negative."); }
            compute.SetFloat("areaToColorAroundObstacles", areaToColorAroundObstacles);
            compute.SetFloat("minDistanceToRemoveParticles", minDistanceToRemoveParticles);
            compute.SetFloat("coloredAreaAroundObstaclesDivider", coloredAreaAroundObstaclesDivider);

            // The Compute Shader needs the MASTER palette for its removal logic.
            if (colorPalette != null && colorPalette.Count > 0)
            {
                Vector4[] paletteForShader = colorPalette.Select(c => (Vector4)c).ToArray();
                compute.SetVectorArray("colorPalette", paletteForShader);
                compute.SetInt("colorPaletteSize", paletteForShader.Length);
            }
            else
            {
                compute.SetInt("colorPaletteSize", 0);
            }
        }

        void BindComputeShaderBuffers()
        {
            if (compute == null) return;

            // --- KERNEL GROUP 1: MAIN SIMULATION ---
            // These kernels read and write to the main particle buffers.
            int[] mainSimKernels = { externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, updatePositionKernel };
            foreach (var kernel in mainSimKernels)
            {
                ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", kernel);
                ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", kernel);
                ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", kernel);
                ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", kernel);
                ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "GravityScales", kernel);
                ComputeHelper.SetBuffer(compute, collisionBuffer, "CollisionBuffer", kernel);
                ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypeBuffer", kernel);

                if (spatialHash != null)
                {
                    ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", kernel);
                    ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", kernel);
                    ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", kernel);
                }
            }

            // --- KERNEL GROUP 2: SPATIAL HASH REORDERING ---
            // Reorder: Reads main buffers (via Source aliases), writes to SortTarget buffers
            ComputeHelper.SetBuffer(compute, positionBuffer, "Source_Positions", reorderKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "Source_PredictedPositions", reorderKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Source_Velocities", reorderKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "Source_ParticleType", reorderKernel);
            ComputeHelper.SetBuffer(compute, collisionBuffer, "Source_Collision", reorderKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", reorderKernel);
            // Set RW write targets
            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleType, "SortTarget_ParticleType", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Collision, "SortTarget_Collision", reorderKernel);

            // Copyback: Reads SortTarget buffers (via CopySource aliases), writes to main buffers
            ComputeHelper.SetBuffer(compute, sortTarget_Position, "CopySource_Positions", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "CopySource_PredictedPositions", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "CopySource_Velocities", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleType, "CopySource_ParticleType", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Collision, "CopySource_Collision", copybackKernel);
            // Set RW write targets
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", copybackKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", copybackKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypeBuffer", copybackKernel);
            ComputeHelper.SetBuffer(compute, collisionBuffer, "CollisionBuffer", copybackKernel);

            // --- KERNEL GROUP 3: PARTICLE REMOVAL / COMPACTION ---
            ComputeHelper.SetBuffer(compute, compactionInfoBuffer, "CompactionInfoBuffer", resetCompactionCounterKernel, compactAndMoveKernel);
            // Set read-only sources
            ComputeHelper.SetBuffer(compute, positionBuffer, "Source_Positions", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "Source_PredictedPositions", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Source_Velocities", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "Source_ParticleType", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, collisionBuffer, "Source_Collision", compactAndMoveKernel);
            if (obstacleColorsBuffer != null) ComputeHelper.SetBuffer(compute, obstacleColorsBuffer, "ObstacleColorsBuffer", compactAndMoveKernel);
            // Set RW write targets
            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleType, "SortTarget_ParticleType", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Collision, "SortTarget_Collision", compactAndMoveKernel);

            // --- KERNEL GROUP 4: OBSTACLES & MISC ---
            if (vertexBuffer != null) ComputeHelper.SetBuffer(compute, vertexBuffer, "VerticesBuffer", updatePositionKernel);
            if (obstacleBuffer != null) ComputeHelper.SetBuffer(compute, obstacleBuffer, "ObstaclesBuffer", updatePositionKernel);
            if (obstacleColorsBuffer != null) ComputeHelper.SetBuffer(compute, obstacleColorsBuffer, "ObstacleColorsBuffer", updatePositionKernel);
            if (currentsBuffer != null) compute.SetBuffer(updatePositionKernel, "CurrentsBuffer", currentsBuffer);
            if (currentVerticesBuffer != null) compute.SetBuffer(updatePositionKernel, "CurrentVerticesBuffer", currentVerticesBuffer);
        }

        void UpdateComputeShaderDynamicParams()
        {
            if (compute == null) return;
            compute.SetInt("numParticles", numParticles);
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            bool isPullInteraction = Input.GetMouseButton(0);
            bool isPushInteraction = Input.GetMouseButton(1);
            float currentInteractionStrength = 0;
            if (isPullInteraction) currentInteractionStrength = interactionStrength;
            else if (isPushInteraction) currentInteractionStrength = -interactionStrength;
            compute.SetVector("interactionInputPoint", mousePos);
            compute.SetFloat("interactionInputStrength", currentInteractionStrength);
            compute.SetFloat("interactionInputRadius", interactionRadius);
            compute.SetFloat("mouseGravityStrength", mouseGravityStrength);
            compute.SetFloat("mouseGravityRadius", mouseGravityRadius);
            compute.SetInt("invertMouseGravity", invertMouseGravity ? 1 : 0);
            compute.SetVector("mousePosition", mousePos);
            compute.SetInt("gKeyPressed", Input.GetKey(KeyCode.G) ? 1 : 0);
            compute.SetInt("numObstacles", _gpuObstacleDataList.Count);

            // Send the dynamic list of mixable colors to the shader every frame.
            if (mixableColorsForShader != null && mixableColorsForShader.Count > 0)
            {
                Vector4[] mixableColorsShader = mixableColorsForShader.Select(c => (Vector4)c).ToArray();
                compute.SetVectorArray("mixableColors", mixableColorsShader);
                compute.SetInt("mixableColorsSize", mixableColorsShader.Length);
            }
            else
            {
                compute.SetInt("mixableColorsSize", 0);
            }
        }

        void SetInitialBufferData(Spawner2D.ParticleSpawnData spawnData)
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
                Spawner2D.ParticleSpawnData newParticleInfo = spawner2D.GetNewlySpawnedParticles(currentFrameTime, numParticles, maxTotalParticles);
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
                ProcessParticleRemovalsGPU();
            }

            if (pauseNextFrame) { isPaused = true; pauseNextFrame = false; }
            HandleInput();
        }

        void ProcessParticleRemovalsGPU()
        {
            if (numParticles == 0 || compute == null || !particleTypeBuffer.IsValid()) return;

            // 1. Reset the atomic counter on the GPU to 0.
            ComputeHelper.Dispatch(compute, 1, 1, 1, kernelIndex: resetCompactionCounterKernel);

            // 2. Run the compaction kernel.
            // Each thread checks its particle. If it survives, it increments the counter
            // and copies its data to the correct slot in the SortTarget buffers.
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: compactAndMoveKernel);

            // 3. Read back the final count of surviving particles.
            // This is a fast, small readback of a single integer.
            uint[] newCountData = new uint[1];
            compactionInfoBuffer.GetData(newCountData);
            int newNumParticles = (int)newCountData[0];

            // If all particles were removed
            if (newNumParticles <= 0)
            {
                numParticles = 0;
                // Optionally clear/recreate buffers if you want to free memory,
                // but just setting the count to 0 is sufficient and fast.
                Debug.Log("All particles removed.");
            }
            else
            {
                numParticles = newNumParticles;
                // 4. Copy the compacted data from SortTarget buffers back to the main buffers.
                // We can reuse the existing copyback kernel for this.
                ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackKernel);
            }

            // 5. Update the particle count on the shader for the next frame.
            UpdateComputeShaderDynamicParams();
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

        void HandleAddingNewParticles(Spawner2D.ParticleSpawnData newParticleData)
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
            spatialHash = new SpatialHash(numParticles);

            BindComputeShaderBuffers();
            UpdateComputeShaderDynamicParams();
        }

        void RunSimulationFrame(float deltaTimeForFrame)
        {
            if (numParticles == 0 || compute == null) return;
            float timeStepPerIteration = deltaTimeForFrame / Mathf.Max(1, iterationsPerFrame);
            compute.SetFloat("deltaTime", timeStepPerIteration);
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
                    spatialHash = new SpatialHash(capacity);
                    numParticles = newNumParticles;
                    if (numParticles > 0) SetInitialBufferData(initialSpawnData);
                    UpdateAutoPlayers();
                    UpdateObstacleBuffer(true);
                }
                else
                {
                    ReleaseParticleBuffers(); ReleaseObstacleBuffers(); _obstacleCache.Clear();
                    _gpuObstacleDataList.Clear(); _gpuVerticesData.Clear(); _gpuObstacleColorsData.Clear();
                    CreateParticleBuffers(1); spatialHash = new SpatialHash(1); numParticles = 0;
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

            // If there are currents, update the data in the existing buffers.
            if (currentDataList.Count > 0)
            {
                // Resize buffers if needed (this helper likely handles this)
                ComputeHelper.CreateStructuredBuffer(ref currentVerticesBuffer, currentVertices);
                ComputeHelper.CreateStructuredBuffer(ref currentsBuffer, currentDataList);

                compute.SetBuffer(updatePositionKernel, "CurrentsBuffer", currentsBuffer);
                compute.SetBuffer(updatePositionKernel, "CurrentVerticesBuffer", currentVerticesBuffer);
            }

            // Always set the count. If zero, the shader will simply not run the currents loop.
            compute.SetInt("numCurrents", currentDataList.Count);
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

            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
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

                if (obsType == 0 && playerColors.TryGetValue(obstacleGO, out int pColor)) displayColor = new Color(colorPalette[pColor].r, colorPalette[pColor].g, colorPalette[pColor].b, 1.0f); //colorPalette[pColor];
                _propBlock.SetColor("_Color", displayColor); lr.SetPropertyBlock(_propBlock);
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
                if (vertexBuffer != null && vertexBuffer.IsValid()) compute.SetBuffer(updatePositionKernel, "VerticesBuffer", vertexBuffer);
                if (obstacleBuffer != null && obstacleBuffer.IsValid()) compute.SetBuffer(updatePositionKernel, "ObstaclesBuffer", obstacleBuffer);
                if (obstacleColorsBuffer != null && obstacleColorsBuffer.IsValid()) compute.SetBuffer(updatePositionKernel, "ObstacleColorsBuffer", obstacleColorsBuffer);
            }
            if (compute != null) compute.SetInt("numObstacles", _gpuObstacleDataList.Count);
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
    }
}