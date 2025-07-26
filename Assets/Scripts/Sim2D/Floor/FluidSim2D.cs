using Seb.Helpers; // Assuming this namespace contains ComputeHelper and SpatialHash
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices; // For Marshal.SizeOf
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random; // Required for AsyncGPUReadback

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
        private FluidSim2D_Wall fluidSim_Wall;

        public ComputeBuffer positionBuffer { get; private set; }
        ComputeBuffer predictedPositionBuffer;
        public ComputeBuffer velocityBuffer { get; private set; }
        public ComputeBuffer densityBuffer { get; private set; }
        public ComputeBuffer gravityScaleBuffer { get; private set; }
        public ComputeBuffer collisionBuffer { get; private set; }
        public ComputeBuffer particleTypeBuffer { get; private set; }

        [StructLayout(LayoutKind.Sequential)]
        public struct ParticleData
        {
            public float2 position;
            public float2 predictedPosition;
            public float2 velocity;
            public int2 particleType;
            public int4 collision;
            public float2 density;
            public float gravityScale;
            public float padding; // To make the struct size 64 bytes for GPU alignment
        }
        ComputeBuffer sortTarget_DataBuffer;

        ComputeBuffer vertexBuffer;
        ComputeBuffer compactionInfoBuffer;
        ComputeBuffer collisionBufferCopy;
        ComputeBuffer removedParticlesBuffer;

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
        const int copyCollisionKernel = 10;
        const int clearRemovedParticlesBuffer = 11;
        const int copyFloatKernel = 12;
        const int copyFloat2Kernel = 13;
        const int copyInt2Kernel = 14;
        const int copyInt4Kernel = 15;
        const int copyParticleDataKernel = 16;

        bool isPaused;
        Spawner2D.ParticleSpawnData initialSpawnData;
        bool pauseNextFrame;
        public int numParticles { get; private set; }
        private bool isProcessingRemovals = false;

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
            public int obstacleType;
        }
        private Dictionary<GameObject, CachedObstacleInfo> _obstacleCache = new Dictionary<GameObject, CachedObstacleInfo>();

        private static List<Color> colorPalette = ColorPalette.colorPalette;
        private int2[] removedParticlesPerColor = new int2[colorPalette.Count];

        List<Vector2> _gpuVerticesData = new List<Vector2>();
        List<ObstacleData> _gpuObstacleDataList = new List<ObstacleData>();
        List<Color> _gpuObstacleColorsData = new List<Color>();

        Dictionary<GameObject, int> playerColors = new Dictionary<GameObject, int>();
        public List<int> mixableColors = new List<int>();
        public List<Color> mixableColorsForShader = new List<Color>();
        [Range(0, 6)] public int maxPlayerColors = 6;
        public Color colorSymbolizingNoPlayer = Color.white;
        public int lastPlayerCount = -1;

        [Header("Obstacle Visualization")]
        public Color obstacleLineColor = Color.white;
        public Color ventilLineColor = Color.green;
        public float obstacleLineWidth = 0.1f;
        public Material lineRendererMaterial;

        private float autoUpdateInterval = 0.5f;
        private float nextAutoUpdateTime;
        private bool _forceObstacleBufferUpdate = false;

        private struct PendingRemovalRequest
        {
            public AsyncGPUReadbackRequest request;
            public List<GameObject> obstaclesSnapshot;
            public List<int> mixableColorsSnapshot;
        }

        private Queue<PendingRemovalRequest> _pendingRemovalRequests = new Queue<PendingRemovalRequest>();

        void Start()
        {
            //Debug.Log("Controls: Space = Play/Pause, R = Reset, LMB = Attract, RMB = Repel, G + Mouse = Gravity Well");
            GameObject.FindFirstObjectByType<XMLSettings>().GetComponent<XMLSettings>().XMLReload(0);
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
            compactionInfoBuffer = ComputeHelper.CreateStructuredBuffer<int2>(1);

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

            sortTarget_DataBuffer = ComputeHelper.CreateStructuredBuffer<ParticleData>(safeCapacity);

            collisionBufferCopy = ComputeHelper.CreateStructuredBuffer<int4>(safeCapacity);
            removedParticlesBuffer = ComputeHelper.CreateStructuredBuffer<int2>(safeCapacity);
        }

        void ReleaseParticleBuffers()
        {
            ComputeHelper.Release(positionBuffer, predictedPositionBuffer, velocityBuffer, densityBuffer,
                gravityScaleBuffer, collisionBuffer, particleTypeBuffer, sortTarget_DataBuffer,
                compactionInfoBuffer, removedParticlesBuffer, collisionBufferCopy);
            positionBuffer = null; predictedPositionBuffer = null; velocityBuffer = null; densityBuffer = null;
            gravityScaleBuffer = null; collisionBuffer = null; particleTypeBuffer = null; sortTarget_DataBuffer = null;
            compactionInfoBuffer = null; removedParticlesBuffer = null; collisionBufferCopy = null;
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
            ComputeHelper.SetBuffer(compute, densityBuffer, "Source_Densities", reorderKernel);
            ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "Source_GravityScales", reorderKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "Source_ParticleType", reorderKernel);
            ComputeHelper.SetBuffer(compute, collisionBuffer, "Source_Collision", reorderKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_DataBuffer, "SortTarget_Data", reorderKernel);

            // Copyback: Reads SortTarget buffers (via CopySource aliases), writes to main buffers
            ComputeHelper.SetBuffer(compute, sortTarget_DataBuffer, "CopySource_Data", copybackKernel);
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", copybackKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", copybackKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", copybackKernel);
            ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "GravityScales", copybackKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypeBuffer", copybackKernel);
            ComputeHelper.SetBuffer(compute, collisionBuffer, "CollisionBuffer", copybackKernel);

            // --- KERNEL GROUP 3: PARTICLE REMOVAL / COMPACTION ---
            ComputeHelper.SetBuffer(compute, compactionInfoBuffer, "CompactionInfoBuffer", resetCompactionCounterKernel, compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, removedParticlesBuffer, "RemovedParticlesBuffer", resetCompactionCounterKernel, compactAndMoveKernel, clearRemovedParticlesBuffer);
            // Set read-only sources
            ComputeHelper.SetBuffer(compute, positionBuffer, "Source_Positions", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "Source_PredictedPositions", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Source_Velocities", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Source_Densities", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "Source_GravityScales", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "Source_ParticleType", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, collisionBufferCopy, "Source_Collision", compactAndMoveKernel);
            if (obstacleColorsBuffer != null) ComputeHelper.SetBuffer(compute, obstacleColorsBuffer, "ObstacleColorsBuffer", compactAndMoveKernel);
            // Set RW write targets
            ComputeHelper.SetBuffer(compute, sortTarget_DataBuffer, "SortTarget_Data", compactAndMoveKernel);

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

            if (Time.time >= nextAutoUpdateTime)
            {
                nextAutoUpdateTime = Time.time + autoUpdateInterval;
            }

            if (!fluidSim_Wall)
            {
                fluidSim_Wall = GameObject.FindAnyObjectByType<FluidSim2D_Wall>();
            }

            UpdateObstacleBuffer(_forceObstacleBufferUpdate);
            _forceObstacleBufferUpdate = false;

            if (!isPaused && numParticles > 0)
            {
                RunSimulationFrame(cappedSimDeltaTime);

                if (collisionBuffer != null && collisionBufferCopy != null && collisionBuffer.count == collisionBufferCopy.count && numParticles > 0)
                {
                    compute.SetBuffer(copyCollisionKernel, "OriginalCollisionBuffer_Source", collisionBuffer);
                    compute.SetBuffer(copyCollisionKernel, "CopiedCollisionBuffer_Destination", collisionBufferCopy);
                    ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copyCollisionKernel);
                }

                ProcessParticleRemovals();
            }

            if (pauseNextFrame) { isPaused = true; pauseNextFrame = false; }
            //HandleInput();
        }

        void ProcessParticleRemovals()
        {
            // --- Step 1: Process all completed requests from the front of the queue ---
            while (_pendingRemovalRequests.Count > 0 && _pendingRemovalRequests.Peek().request.done)
            {
                // Get the oldest pending request
                PendingRemovalRequest completedRequest = _pendingRemovalRequests.Dequeue();

                if (completedRequest.request.hasError)
                {
                    Debug.LogError("GPU readback error on removed particles buffer!");
                }
                else
                {
                    var removedParticlesData = completedRequest.request.GetData<int2>();
                    // Use the snapshots that were correctly bundled with this specific request
                    List<GameObject> obstaclesSnapshot = completedRequest.obstaclesSnapshot;
                    List<int> mixableColorsSnapshot = completedRequest.mixableColorsSnapshot;
                    Dictionary<GameObject, int[]> interactingObstacles = new Dictionary<GameObject, int[]>();

                    // Process the list of removed particles
                    for (int particleNr = 0; particleNr < removedParticlesData.Length; particleNr++)
                    {
                        int2 particleData = removedParticlesData[particleNr];
                        int particleType = particleData.x - 1;
                        int obstacleId = particleData.y;

                        if (obstacleId >= 0 && obstaclesSnapshot != null && obstacleId < obstaclesSnapshot.Count &&
                            mixableColorsSnapshot != null && particleType >= 0 && particleType < mixableColorsSnapshot.Count)
                        {
                            GameObject obstacle = obstaclesSnapshot[obstacleId];
                            if (obstacle == null) continue; // Obstacle might have been destroyed

                            int actualParticleColor = mixableColorsSnapshot[particleType];

                            if (!interactingObstacles.ContainsKey(obstacle))
                            {
                                interactingObstacles.Add(obstacle, new int[12]);
                            }

                            if (actualParticleColor >= 0)
                            {
                                interactingObstacles[obstacle][actualParticleColor]++;

                                if (obstacle.CompareTag("Player"))
                                {
                                    removedParticlesPerColor[actualParticleColor][0]++;
                                }
                                else if (obstacle.CompareTag("Ventil"))
                                {
                                    removedParticlesPerColor[actualParticleColor][1]++;
                                }
                            }
                        }
                    }

                    foreach (var entry in interactingObstacles)
                    {
                        GameObject obstacle = entry.Key;
                        AudioSource audioSource = obstacle.GetComponent<AudioSource>();
                        if (obstacle.CompareTag("Player"))
                        {
                            if (audioSource) audioSource.pitch = Random.Range(1f, 1.25f);
                            
                            PlayerEffects playerEffects = obstacle.GetComponentInChildren<PlayerEffects>();
                            
                            if (playerEffects != null)
                            {
                                for (int i = 0; i < 12; i++)
                                {
                                    if (entry.Value[i] > 0)
                                    {
                                        playerEffects.CollectOil(colorPalette[i]);
                                    }
                                }
                            }
                        }
                        else if (obstacle.CompareTag("Ventil"))
                        {
                            if (audioSource) audioSource.pitch = Random.Range(0.5f, 0.75f);

                            Ventil ventil = obstacle.GetComponent<Ventil>();
                            if (ventil != null)
                            {
                                for (int i = 0; i < 12; i++)
                                {
                                    if (entry.Value[i] > 0)
                                    {
                                        ventil.TakeDamage(entry.Value[i], colorPalette[i]);
                                    }
                                }
                            }
                        }

                        if (audioSource && audioSource.gameObject.activeInHierarchy && audioSource.enabled)
                        {
                            audioSource.Play();
                        }
                    }
                }
            }

            // --- Step 2: Run the compaction for the CURRENT frame ---
            if (numParticles == 0 || compute == null || isProcessingRemovals) return;
            isProcessingRemovals = true;

            ComputeHelper.Dispatch(compute, 1, 1, 1, kernelIndex: resetCompactionCounterKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: compactAndMoveKernel);

            AsyncGPUReadback.Request(compactionInfoBuffer, (request) =>
            {
                if (request.hasError)
                {
                    Debug.LogError("GPU readback error on compaction info buffer!");
                    isProcessingRemovals = false;
                    return;
                }

                var counters = request.GetData<int2>();
                if (counters.Length > 0)
                {
                    int keptCount = counters[0].x;
                    int removedCount = counters[0].y;

                    if (removedCount > 0)
                    {
                        // Ensure the buffer is still valid
                        if (removedParticlesBuffer == null || !removedParticlesBuffer.IsValid() || compute == null)
                        {
                            isProcessingRemovals = false;
                            return;
                        }

                        ComputeHelper.Dispatch(compute, keptCount, kernelIndex: copybackKernel);
                        numParticles = keptCount;

                        // Create a new request package with its context
                        var newPendingRequest = new PendingRemovalRequest
                        {
                            obstaclesSnapshot = new List<GameObject>(obstacles),
                            mixableColorsSnapshot = new List<int>(mixableColors),
                            request = AsyncGPUReadback.Request(removedParticlesBuffer, removedCount * Marshal.SizeOf<int2>(), 0)
                        };

                        // Add it to the queue to be processed in a future frame
                        _pendingRemovalRequests.Enqueue(newPendingRequest);
                    }
                }
                UpdateComputeShaderDynamicParams();
                isProcessingRemovals = false;
            });
        }

        public void SpawnParticles(List<float4> particlesToSpawn)
        {
            if (spawner2D != null && spawner2D.allowContinuousSpawning && numParticles < maxTotalParticles)
            {
                Spawner2D.ParticleSpawnData newParticleInfo = spawner2D.SpawnTransferedParticles(particlesToSpawn);
                if (newParticleInfo.positions != null && newParticleInfo.positions.Length > 0)
                {
                    HandleAddingNewParticles(newParticleInfo);
                }
            }
        }

        ComputeBuffer GPUSideResizeAndAppend<T>(ComputeBuffer oldBuffer, T[] newData) where T : struct
        {
            int oldCount = oldBuffer != null ? oldBuffer.count : 0;
            int newCount = newData?.Length ?? 0;
            int totalCount = oldCount + newCount;
            if (totalCount == 0) { oldBuffer?.Release(); return null; }

            // --- Logic to select the correct kernel and buffer names ---
            string typeName = typeof(T).Name;
            int kernel;

            switch (typeName)
            {
                case "Single":
                    typeName = "float";
                    kernel = copyFloatKernel;
                    break;
                case "float2":
                    kernel = copyFloat2Kernel;
                    break;
                case "int2":
                    kernel = copyInt2Kernel;
                    break;
                case "int4":
                    kernel = copyInt4Kernel;
                    break;
                case "ParticleData":
                    kernel = copyParticleDataKernel;
                    break;
                default:
                    Debug.LogError($"GPUSideResizeAndAppend_V2 does not support type: {typeName}");
                    return oldBuffer; // Return the original buffer to avoid errors
            }

            string sourceName = $"Source_{typeName}";
            string destName = $"Destination_{typeName}";
            // --- End of selection logic ---

            // 1. Create the final destination buffer.
            ComputeBuffer destinationBuffer = new ComputeBuffer(totalCount, Marshal.SizeOf(typeof(T)));

            // 2. If there's old data, perform a GPU-side copy.
            if (oldCount > 0)
            {
                compute.SetBuffer(kernel, sourceName, oldBuffer);
                compute.SetBuffer(kernel, destName, destinationBuffer);
                ComputeHelper.Dispatch(compute, oldCount, kernelIndex: kernel);
            }

            // 3. Append the new data directly using the fast SetData command.
            if (newCount > 0)
            {
                destinationBuffer.SetData(newData, 0, oldCount, newCount);
            }

            // 4. Clean up.
            oldBuffer?.Release();
            return destinationBuffer;
        }

        void HandleAddingNewParticles(Spawner2D.ParticleSpawnData newParticleData)
        {
            int newSpawnCount = newParticleData.positions.Length;
            if (newSpawnCount == 0) return;

            int oldNumParticles = numParticles;
            numParticles += newSpawnCount;

            // Resize all buffers on the GPU using the single generic V2 helper
            positionBuffer = GPUSideResizeAndAppend(positionBuffer, newParticleData.positions);
            predictedPositionBuffer = GPUSideResizeAndAppend(predictedPositionBuffer, newParticleData.positions);
            velocityBuffer = GPUSideResizeAndAppend(velocityBuffer, newParticleData.velocities);
            particleTypeBuffer = GPUSideResizeAndAppend(particleTypeBuffer, newParticleData.particleTypes);

            // Create default data for the new particles
            float[] newGravityScales = new float[newSpawnCount];
            int4[] newCollisionData = new int4[newSpawnCount];
            float2[] newDensityData = new float2[newSpawnCount];
            ParticleData[] newSortData = new ParticleData[newSpawnCount];

            for (int i = 0; i < newSpawnCount; i++)
            {
                newGravityScales[i] = 1f;
                newCollisionData[i] = new int4(-1, -1, -1, -1);
            }

            gravityScaleBuffer = GPUSideResizeAndAppend(gravityScaleBuffer, newGravityScales);
            collisionBuffer = GPUSideResizeAndAppend(collisionBuffer, newCollisionData);
            collisionBufferCopy = GPUSideResizeAndAppend(collisionBufferCopy, new int4[newSpawnCount]);
            densityBuffer = GPUSideResizeAndAppend(densityBuffer, newDensityData);
            sortTarget_DataBuffer = GPUSideResizeAndAppend(sortTarget_DataBuffer, newSortData);

            // The rest of the function
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
            if (numParticles == 0 || compute == null || (lastPlayerCount == 0)) return;
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
            spatialHash.Run();
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
                        maxVelocity = current.currentVelocity,
                        width = current.currentWidth,
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

        public void RegisterObstacle(GameObject obstacleGO)
        {
            if (!obstacles.Contains(obstacleGO))
            {
                obstacles.Add(obstacleGO);
                // A change occurred, so we need to re-evaluate the obstacle state
                // and tell the GPU buffers to update on the next frame.
                UpdateObstacleAndPlayerState();
            }
        }

        public void UnregisterObstacle(GameObject obstacleGO)
        {
            if (obstacles.Remove(obstacleGO) && obstacleGO != null)
            {
                _obstacleCache.Remove(obstacleGO);
                playerColors.Remove(obstacleGO);
                // A change occurred, so again, we update the state.
                UpdateObstacleAndPlayerState();
            }
        }

        private void UpdateObstacleAndPlayerState()
        {
            if (!Application.isPlaying) return;

            // A flag to track if any significant changes require deeper updates.
            bool listActuallyChanged = false;

            // --- Cache Management: Ensure all current obstacles are cached ---
            foreach (GameObject go in obstacles)
            {
                // If an obstacle isn't in our cache, add it.
                if (!_obstacleCache.ContainsKey(go))
                {
                    var info = new CachedObstacleInfo { transform = go.transform };
                    info.polyCol = go.GetComponent<PolygonCollider2D>();

                    if (!go.TryGetComponent<LineRenderer>(out info.lineRend))
                    {
                        info.lineRend = go.AddComponent<LineRenderer>();
                        info.lineRend.useWorldSpace = true;
                    }

                    if (info.lineRend != null)
                    {
                        info.lineRend.sharedMaterial = lineRendererMaterial != null ? lineRendererMaterial : _sharedUnlitMaterial;
                    }

                    // Assign obstacle type based on tag
                    if (go.CompareTag("Player")) info.obstacleType = 0;
                    else if (go.CompareTag("Ventil")) info.obstacleType = 2;
                    else info.obstacleType = 1; // Default obstacle

                    _obstacleCache[go] = info;
                    listActuallyChanged = true;
                }
            }

            obstacles.RemoveAll(item => item == null);

            // --- Sorting: Keep the obstacle list in a deterministic order ---
            var currentPlayersInScene = new HashSet<GameObject>(obstacles.Where(o => o.CompareTag("Player")));
            var currentObstaclesInScene = new HashSet<GameObject>(obstacles.Where(o => o.CompareTag("Obstacle")));
            var currentVentilsInScene = new HashSet<GameObject>(obstacles.Where(o => o.CompareTag("Ventil")));

            int GetPriority(GameObject go, HashSet<GameObject> players, HashSet<GameObject> staticObs, HashSet<GameObject> ventils)
            {
                if (players.Contains(go)) return 0;
                if (staticObs.Contains(go)) return 1;
                if (ventils.Contains(go)) return 2;
                return 3;
            }

            obstacles = obstacles
                .OrderBy(o => GetPriority(o, currentPlayersInScene, currentObstaclesInScene, currentVentilsInScene))
                .ThenBy(o => o.GetInstanceID())
                .ToList();


            // --- Color Assignment Logic ---
            List<GameObject> sortedPlayersForColoring = obstacles
                .Where(o => _obstacleCache.ContainsKey(o) && o.CompareTag("Player"))
                .ToList();

            if (listActuallyChanged || sortedPlayersForColoring.Count != lastPlayerCount)
            {
                Dictionary<GameObject, int> tempPlayerColors = new Dictionary<GameObject, int>();
                List<int> assignedIndices = new List<int>();
                int numPaletteColors = colorPalette.Count;
                int colorLimit = Mathf.Min(maxPlayerColors, numPaletteColors);

                List<KeyValuePair<GameObject, int>> existingPlayersWithOldColor = new List<KeyValuePair<GameObject, int>>();
                List<GameObject> newPlayersInSortedOrder = new List<GameObject>();

                foreach (GameObject player in sortedPlayersForColoring)
                {
                    if (playerColors.TryGetValue(player, out int oldColorIndex))
                    {
                        existingPlayersWithOldColor.Add(new KeyValuePair<GameObject, int>(player, oldColorIndex));
                    }
                    else
                    {
                        newPlayersInSortedOrder.Add(player);
                    }
                }

                existingPlayersWithOldColor.Sort((a, b) => a.Value.CompareTo(b.Value));

                foreach (var playerEntry in existingPlayersWithOldColor)
                {
                    GameObject player = playerEntry.Key;
                    int oldColorIndex = playerEntry.Value;
                    tempPlayerColors[player] = oldColorIndex % colorLimit;
                    assignedIndices.Add(oldColorIndex);
                }

                int nextColorIndex = lastPlayerCount;
                foreach (GameObject player in newPlayersInSortedOrder)
                {
                    bool uniqueSlotFound = false;
                    for (int k = 0; k < colorLimit; k++)
                    {
                        if (!assignedIndices.Contains(k))
                        {
                            tempPlayerColors[player] = k;
                            assignedIndices.Add(k);
                            uniqueSlotFound = true;
                            break;
                        }
                    }

                    if (!uniqueSlotFound)
                    {
                        tempPlayerColors[player] = nextColorIndex % maxPlayerColors;
                        nextColorIndex++;
                    }
                }

                playerColors = tempPlayerColors;

                foreach (GameObject player in playerColors.Keys)
                {
                    Color colorToUse = colorPalette[playerColors[player]];
                    player.GetComponentInChildren<PlayerColor>().UpdateColor(colorToUse);
                }

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
                        if ((i == 3 && assignedIndices.Contains(0) && assignedIndices.Contains(1) && (maxPlayerColors <= 3 || lastPlayerCount <= 3)) ||
                            (i == 4 && assignedIndices.Contains(0) && assignedIndices.Contains(2) && (maxPlayerColors <= 3 || lastPlayerCount <= 3)) ||
                            (i == 5 && assignedIndices.Contains(1) && assignedIndices.Contains(2) && (maxPlayerColors <= 3 || lastPlayerCount <= 3)) ||
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
                            mixableColors.Add(-1);
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
                            currentIndex = (currentIndex + 1) % assignedIndices.Count;
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

            // Finally, flag that the GPU obstacle buffers need to be rebuilt.
            _forceObstacleBufferUpdate = true;
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
                if (obstacleGO.CompareTag("Player"))
                {
                    obsType = 0;
                }
                else if (obstacleGO.CompareTag("Ventil"))
                {
                    obsType = 2;
                }

                _gpuObstacleDataList.Add(new ObstacleData
                {
                    centre = cachedInfo.transform.TransformPoint(polyCol.offset),
                    vertexStart = currentVertexStartIndex,
                    vertexCount = vertexCountForThisObstacle,
                    lineWidth = obstacleLineWidth,
                    obstacleType = obsType
                });
                currentVertexStartIndex += vertexCountForThisObstacle;

                Color displayColor = Color.white;
                if (obsType == 1) { displayColor = obstacleLineColor; }
                else if (obsType == 2) { displayColor = ventilLineColor; }

                if (obsType == 0 && playerColors.TryGetValue(obstacleGO, out int pColor)) displayColor = new Color(colorPalette[pColor].r, colorPalette[pColor].g, colorPalette[pColor].b, 1f); //colorPalette[pColor];
                _propBlock.SetColor("_BaseColor", displayColor); lr.SetPropertyBlock(_propBlock);
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
                if (obstacleColorsBuffer != null && obstacleColorsBuffer.IsValid())
                {
                    // This buffer is read by two different kernels, so we must update the binding for both.
                    compute.SetBuffer(updatePositionKernel, "ObstacleColorsBuffer", obstacleColorsBuffer);
                    compute.SetBuffer(compactAndMoveKernel, "ObstacleColorsBuffer", obstacleColorsBuffer);
                }
            }
            if (compute != null) compute.SetInt("numObstacles", _gpuObstacleDataList.Count);
        }

        void OnEnable()
        {
            // Re-initialize if buffers are null (which they will be after OnDisable)
            if (positionBuffer == null)
            {
                InitSimulation();
            }
        }

        void OnDisable()
        {
            // Release all compute buffers and managed resources here
            ComputeHelper.Release(currentsBuffer, currentVerticesBuffer);
            ReleaseParticleBuffers();
            ReleaseObstacleBuffers();
        }

        void OnDestroy()
        {
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