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

        ComputeBuffer vertexBuffer;
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

        bool isPaused;
        Spawner2D.ParticleSpawnData initialSpawnData;
        bool pauseNextFrame;
        public int numParticles { get; private set; }

        [Header("Obstacles")]
        public List<Color> mixableColors = new List<Color>();
        public List<GameObject> obstacles = new List<GameObject>();
        [Min(0)] public float areaToColorAroundObstacles = 0.1f;
        [Min(0)] public float coloredAreaAroundObstaclesDivider = 0.05f;

        private MaterialPropertyBlock _propBlock;
        private Material _sharedUnlitMaterial;

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

        List<Color> playerColorPalette = new List<Color> {
            new Color(0.9f, 0f, 0.4f), new Color(1f, 0.9f, 0f), new Color(0.0f, 0.4f, 0.7f),
            new Color(0.95f, 0.55f, 0f), new Color(0.6f, 0.75f, 0.1f), new Color(0.6f, 0.1f, 0.5f),
            new Color(1f, 0.75f, 0f), new Color(0.9f, 0.35f, 0f), new Color(0.9f, 0f, 0.5f),
            new Color(0.4f, 0.3f, 0.6f), new Color(0.05f, 0.7f, 0.6f), new Color(0.8f, 0.85f, 0f),
        };

        List<Vector2> _gpuVerticesData = new List<Vector2>();
        List<ObstacleData> _gpuObstacleDataList = new List<ObstacleData>();
        List<Color> _gpuObstacleColorsData = new List<Color>();

        Dictionary<GameObject, Color> playerColors = new Dictionary<GameObject, Color>();
        public int maxPlayerColors = 6;
        int lastPlayerCount = -1;

        [Header("Obstacle Visualization")]
        public Color obstacleLineColor = Color.white;
        public float obstacleLineWidth = 0.1f;
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
            particleTypeBuffer = ComputeHelper.CreateStructuredBuffer<int>(safeCapacity);

            sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            sortTarget_ParticleType = ComputeHelper.CreateStructuredBuffer<int>(safeCapacity);
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
            compute.SetFloat("coloredAreaAroundObstaclesDivider", coloredAreaAroundObstaclesDivider);
        }

        void BindComputeShaderBuffers()
        {
            if (compute == null) return;
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityKernel, pressureKernel, viscosityKernel);
            ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "GravityScales", externalForcesKernel);
            ComputeHelper.SetBuffer(compute, collisionBuffer, "CollisionBuffer", updatePositionKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypeBuffer", densityKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel, externalForcesKernel);
            if (spatialHash != null && spatialHash.SpatialIndices != null && spatialHash.SpatialOffsets != null && spatialHash.SpatialKeys != null)
            {
                ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel);
                ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
                ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
            }
            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleType, "SortTarget_ParticleType", reorderKernel, copybackKernel);
            if (vertexBuffer != null && vertexBuffer.IsValid()) ComputeHelper.SetBuffer(compute, vertexBuffer, "VerticesBuffer", updatePositionKernel);
            if (obstacleBuffer != null && obstacleBuffer.IsValid()) ComputeHelper.SetBuffer(compute, obstacleBuffer, "ObstaclesBuffer", updatePositionKernel);
            if (obstacleColorsBuffer != null && obstacleColorsBuffer.IsValid()) compute.SetBuffer(updatePositionKernel, "obstacleColorsBuffer", obstacleColorsBuffer);
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
            }

            if (pauseNextFrame) { isPaused = true; pauseNextFrame = false; }
            HandleInput();
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
            sortTarget_ParticleType = FallbackResizeAndAppendBuffer(sortTarget_ParticleType, oldNumParticles, new int[newSpawnCount]);
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

        void UpdateAutoPlayers()
        {
            if (!Application.isPlaying) return;
            var allGameObjectsInScene = FindObjectsOfType<GameObject>();
            HashSet<GameObject> currentPlayersInScene = new HashSet<GameObject>();
            HashSet<GameObject> currentObstaclesInScene = new HashSet<GameObject>();
            HashSet<GameObject> currentVentilsInScene = new HashSet<GameObject>();

            foreach (GameObject go in allGameObjectsInScene)
            {
                if (!go.activeInHierarchy) continue;
                if (go.name.Contains("PharusPlayer")) currentPlayersInScene.Add(go);
                else if (go.name.Contains("Obstacle")) currentObstaclesInScene.Add(go);
                else if (go.name.Contains("Ventil")) currentVentilsInScene.Add(go);
            }

            bool listActuallyChanged = false;
            List<GameObject> newMasterObstaclesList = new List<GameObject>();

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


            List<GameObject> sortedPlayersForColoring = obstacles.Where(o => _obstacleCache.ContainsKey(o) && o.name.Contains("PharusPlayer")).ToList();
            if (listActuallyChanged || sortedPlayersForColoring.Count != lastPlayerCount)
            {
                playerColors.Clear();
                int numPaletteColors = playerColorPalette.Count;
                if (numPaletteColors > 0 && maxPlayerColors > 0)
                {
                    for (int i = 0; i < sortedPlayersForColoring.Count; i++)
                    {
                        GameObject currentPlayer = sortedPlayersForColoring[i];
                        Color playerColor = playerColorPalette[i % Mathf.Min(maxPlayerColors, numPaletteColors)];
                        playerColor.a = 1.0f;
                        playerColors[currentPlayer] = playerColor;
                    }
                }
                _forceObstacleBufferUpdate = true;
            }
            lastPlayerCount = sortedPlayersForColoring.Count;

            mixableColors.Clear();
            int colorsToMix = Mathf.Min(sortedPlayersForColoring.Count * 2, playerColorPalette.Count);
            for (int i = 0; i < colorsToMix; i++) mixableColors.Add(playerColorPalette[i]);
            for (int i = mixableColors.Count; i < maxPlayerColors * 2; i++) mixableColors.Add(new Color(-1, -1, -1, -1));
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
                if (cachedInfo.transform.hasChanged) { anyObstacleTransformChanged = true; cachedInfo.transform.hasChanged = false; }

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
                if (obstacleGO.name.Contains("PharusPlayer")) obsType = 0; else if (obstacleGO.name.Contains("Ventil")) obsType = 2;
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
                if (obsType == 0 && playerColors.TryGetValue(obstacleGO, out Color pColor)) displayColor = pColor;
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
                if (obstacleColorsBuffer != null && obstacleColorsBuffer.IsValid()) compute.SetBuffer(updatePositionKernel, "obstacleColorsBuffer", obstacleColorsBuffer);
            }
            if (compute != null) compute.SetInt("numObstacles", _gpuObstacleDataList.Count);
        }

        void OnDestroy()
        {
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
        public bool AreColorsClose(Color color1, Color color2, float tolerance, bool compareAlpha = false) { return false; }
    }
}