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

        private FluidSim2D fluidSim_Floor;

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

        [Header("References")]
        public ComputeShader compute;
        public Spawner2D_Wall spawner2D;

        public ComputeBuffer positionBuffer { get; private set; }
        ComputeBuffer predictedPositionBuffer;
        public ComputeBuffer velocityBuffer { get; private set; }
        public ComputeBuffer densityBuffer { get; private set; }
        public ComputeBuffer gravityScaleBuffer { get; private set; }
        public ComputeBuffer particleTypeBuffer { get; private set; }

        ComputeBuffer sortTarget_Position;
        ComputeBuffer sortTarget_PredicitedPosition;
        ComputeBuffer sortTarget_Velocity;
        ComputeBuffer sortTarget_ParticleType;

        ComputeBuffer vertexBuffer;
        ComputeBuffer compactionInfoBuffer;
        ComputeBuffer removedParticlesBuffer;

        public ComputeBuffer obstacleBuffer { get; private set; }
        SpatialHash_Wall spatialHash;

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
        const int clearRemovedParticlesBuffer = 10;

        bool isPaused;
        Spawner2D_Wall.ParticleSpawnData initialSpawnData;
        bool pauseNextFrame;
        public int numParticles { get; private set; }
        private bool isProcessingRemovals = false;

        [Header("Obstacles")]

        public List<GameObject> obstacles = new List<GameObject>();

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

        private static List<Color> colorPalette = ColorPalette.colorPalette;

        List<Vector2> _gpuVerticesData = new List<Vector2>();
        List<ObstacleData> _gpuObstacleDataList = new List<ObstacleData>();

        [Header("Obstacle Visualization")]
        public Color obstacleLineColor = Color.white;
        public Color ventilLineColor = Color.green;
        [Min(0)] public float obstacleLineWidth = 0.1f;
        public Material lineRendererMaterial;

        private float autoUpdateInterval = 0.5f;
        private float nextAutoUpdateTime;
        private bool _forceObstacleBufferUpdate = false;

        void Start()
        {
            //Debug.Log("Controls: Space = Play/Pause, R = Reset, LMB = Attract, RMB = Repel, G + Mouse = Gravity Well");
            InitSimulation();
        }

        void Awake()
        {
            fluidSim_Floor = GameObject.FindFirstObjectByType<FluidSim2D>();
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
            compactionInfoBuffer = ComputeHelper.CreateStructuredBuffer<int2>(1);
            removedParticlesBuffer = ComputeHelper.CreateStructuredBuffer<float4>(1);

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

            // Initialize current buffers with a minimal size to ensure they always exist.
            currentsBuffer = ComputeHelper.CreateStructuredBuffer<CurrentData>(1);
            currentVerticesBuffer = ComputeHelper.CreateStructuredBuffer<Vector2>(1);

            SetupObstacleBuffers();
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
            particleTypeBuffer = ComputeHelper.CreateStructuredBuffer<int2>(safeCapacity);

            sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
            sortTarget_ParticleType = ComputeHelper.CreateStructuredBuffer<int2>(safeCapacity);
        }

        void ReleaseParticleBuffers()
        {
            ComputeHelper.Release(positionBuffer, predictedPositionBuffer, velocityBuffer, densityBuffer,
                gravityScaleBuffer, particleTypeBuffer, sortTarget_Position,
                sortTarget_PredicitedPosition, sortTarget_Velocity, sortTarget_ParticleType,
                compactionInfoBuffer, removedParticlesBuffer);
            positionBuffer = null; predictedPositionBuffer = null; velocityBuffer = null; densityBuffer = null;
            gravityScaleBuffer = null; particleTypeBuffer = null; sortTarget_Position = null;
            sortTarget_PredicitedPosition = null; sortTarget_Velocity = null; sortTarget_ParticleType = null;
            compactionInfoBuffer = null; removedParticlesBuffer = null;

            spatialHash?.Release();
            spatialHash = null;
        }

        void ReleaseObstacleBuffers()
        {
            ComputeHelper.Release(vertexBuffer, obstacleBuffer);
            vertexBuffer = null; obstacleBuffer = null;
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
            // The Compute Shader needs the MASTER palette for its removal logic.
            if (colorPalette != null && colorPalette.Count > 0)
            {
                Vector4[] paletteForShader = colorPalette.Select(c => (Vector4)c).ToArray();
                compute.SetVectorArray("colorPalette_Wall", paletteForShader);
                compute.SetInt("colorPaletteSize_Wall", paletteForShader.Length);
            }
            else
            {
                compute.SetInt("colorPaletteSize_Wall", 0);
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
                ComputeHelper.SetBuffer(compute, positionBuffer, "Positions_Wall", kernel);
                ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions_Wall", kernel);
                ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities_Wall", kernel);
                ComputeHelper.SetBuffer(compute, densityBuffer, "Densities_Wall", kernel);
                ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "GravityScales_Wall", kernel);
                ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypeBuffer_Wall", kernel);

                if (spatialHash != null)
                {
                    ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices_Wall", kernel);
                    ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets_Wall", kernel);
                    ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys_Wall", kernel);
                }
            }

            // --- KERNEL GROUP 2: SPATIAL HASH REORDERING ---
            // Reorder: Reads main buffers (via Source aliases), writes to SortTarget buffers
            ComputeHelper.SetBuffer(compute, positionBuffer, "Source_Positions_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "Source_PredictedPositions_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Source_Velocities_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "Source_ParticleType_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices_Wall", reorderKernel);
            // Set RW write targets
            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleType, "SortTarget_ParticleType_Wall", reorderKernel);

            // Copyback: Reads SortTarget buffers (via CopySource aliases), writes to main buffers
            ComputeHelper.SetBuffer(compute, sortTarget_Position, "CopySource_Positions_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "CopySource_PredictedPositions_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "CopySource_Velocities_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleType, "CopySource_ParticleType_Wall", copybackKernel);
            // Set RW write targets
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypeBuffer_Wall", copybackKernel, compactAndMoveKernel);

            ComputeHelper.SetBuffer(compute, compactionInfoBuffer, "CompactionInfoBuffer_Wall", resetCompactionCounterKernel, compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, removedParticlesBuffer, "RemovedParticlesBuffer_Wall", resetCompactionCounterKernel, compactAndMoveKernel, clearRemovedParticlesBuffer, updatePositionKernel);

            ComputeHelper.SetBuffer(compute, positionBuffer, "Source_Positions_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "Source_PredictedPositions_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Source_Velocities_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "Source_ParticleType_Wall", compactAndMoveKernel);

            // Set RW write targets
            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleType, "SortTarget_ParticleType_Wall", compactAndMoveKernel);

            // --- KERNEL GROUP 4: OBSTACLES & MISC ---
            if (vertexBuffer != null) ComputeHelper.SetBuffer(compute, vertexBuffer, "VerticesBuffer_Wall", updatePositionKernel);
            if (obstacleBuffer != null) ComputeHelper.SetBuffer(compute, obstacleBuffer, "ObstaclesBuffer_Wall", updatePositionKernel);
            if (currentsBuffer != null) compute.SetBuffer(updatePositionKernel, "CurrentsBuffer_Wall", currentsBuffer);
            if (currentVerticesBuffer != null) compute.SetBuffer(updatePositionKernel, "CurrentVerticesBuffer_Wall", currentVerticesBuffer);
        }

        void UpdateComputeShaderDynamicParams()
        {
            if (compute == null) return;
            compute.SetInt("numParticles_Wall", numParticles);
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            bool isPullInteraction = Input.GetMouseButton(0);
            bool isPushInteraction = Input.GetMouseButton(1);
            compute.SetVector("interactionInputPoint_Wall", mousePos);
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

            for (int i = 0; i < numParticles; i++)
            {
                defaultGravityScales[i] = 1f;
            }

            gravityScaleBuffer.SetData(defaultGravityScales);
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
                nextAutoUpdateTime = Time.time + autoUpdateInterval;
            }

            if (!isPaused && numParticles > 0)
            {
                RunSimulationFrame(cappedSimDeltaTime);

                ProcessParticleRemovalsGPU();
                ProcessParticleRemovalsCPU();
            }

            if (pauseNextFrame) { isPaused = true; pauseNextFrame = false; }
            HandleInput();
        }

        void ProcessParticleRemovalsGPU()
        {
            if (numParticles == 0 || compute == null || !particleTypeBuffer.IsValid()) return;

            //First reset the counter than check all particles and copy only the existing ones back
            ComputeHelper.Dispatch(compute, 1, 1, 1, kernelIndex: resetCompactionCounterKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: compactAndMoveKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackKernel);

            UpdateComputeShaderDynamicParams();
        }

        void ProcessParticleRemovalsCPU()
        {
            if (numParticles == 0 || compute == null || isProcessingRemovals)
            {
                return;
            }
            isProcessingRemovals = true;

            //Create a snapshot to ensure the data has not changed meanwhile
            List<int> mixableColorsSnapshot = fluidSim_Floor.mixableColors;

            // 1. Dispatch kernels to classify particles and populate the counter buffer.
            ComputeHelper.Dispatch(compute, 1, 1, 1, kernelIndex: resetCompactionCounterKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: compactAndMoveKernel);

            // 2. Request the counter data ONCE.
            AsyncGPUReadback.Request(compactionInfoBuffer, (request) =>
            {
                if (request.hasError)
                {
                    Debug.LogError("GPU readback error!");
                }
                else
                {
                    var counters = request.GetData<int2>();

                    if (counters.Length > 0)
                    {
                        int keptCount = counters[0].x;
                        int removedCount = counters[0].y;

                        numParticles = keptCount;

                        if (removedCount > 0)
                        {
                            // Now request the list of removed particle indices
                            AsyncGPUReadback.Request(removedParticlesBuffer, removedCount * Marshal.SizeOf<float4>(), 0, (listRequest) =>
                            {
                                if (listRequest.hasError) return;
                                var removedParticlesData = listRequest.GetData<float4>();

                                fluidSim_Floor.SpawnParticles(removedParticlesData.ToList());
                            });
                        }
                    }
                }

                isProcessingRemovals = false;
            });
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
            if (numParticles == 0 || compute == null || (fluidSim_Floor.lastPlayerCount == 0)) return;
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
                    _gpuObstacleDataList.Clear();
                    _gpuVerticesData.Clear();

                    int capacity = Mathf.Max(1, newNumParticles);
                    CreateParticleBuffers(capacity);
                    spatialHash = new SpatialHash_Wall(capacity);
                    numParticles = newNumParticles;
                    if (numParticles > 0) SetInitialBufferData(initialSpawnData);
                    SetupObstacleBuffers();
                }
                else
                {
                    ReleaseParticleBuffers(); ReleaseObstacleBuffers();
                    _gpuObstacleDataList.Clear(); _gpuVerticesData.Clear();
                    CreateParticleBuffers(1); spatialHash = new SpatialHash_Wall(1); numParticles = 0;
                    SetupObstacleBuffers();
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

                compute.SetBuffer(updatePositionKernel, "CurrentsBuffer_Wall", currentsBuffer);
                compute.SetBuffer(updatePositionKernel, "CurrentVerticesBuffer_Wall", currentVerticesBuffer);
            }

            // Always set the count. If zero, the shader will simply not run the currents loop.
            compute.SetInt("numCurrents_Wall", currentDataList.Count);
        }

        void SetupObstacleBuffers()
        {
            if (!Application.isPlaying || compute == null) return;

            // --- 1. Find and Sort Obstacles from the Scene ---
            var allGameObjectsInScene = FindObjectsOfType<GameObject>();
            var obstaclesToSort = new List<GameObject>();

            foreach (GameObject go in allGameObjectsInScene)
            {
                // Add active objects with the correct tags to our list
                if (go.activeInHierarchy && (go.CompareTag("Obstacle") || go.CompareTag("Ventil")))
                {
                    obstaclesToSort.Add(go);
                }
            }

            // Sort the list: "Obstacle" types first, then "Ventil" types
            obstacles = obstaclesToSort.OrderBy(o => o.CompareTag("Obstacle") ? 0 : 1)
                                       .ThenBy(o => o.GetInstanceID())
                                       .ToList();


            // --- 2. Populate CPU-side lists with data for the GPU ---
            _gpuVerticesData.Clear();
            _gpuObstacleDataList.Clear();

            foreach (var go in obstacles)
            {
                var lr = go.GetComponent<LineRenderer>();
                PolygonCollider2D polygonCollider = go.GetComponent<PolygonCollider2D>();

                // Check if we need to set up the LineRenderer
                // This is true if it doesn't exist, has no points, or if the collider exists and has points
                if (lr == null || lr.positionCount < 2 && polygonCollider != null)
                {
                    // If the LineRenderer doesn't exist, add it.
                    if (lr == null)
                    {
                        lr = go.AddComponent<LineRenderer>();
                        lr.useWorldSpace = true; // Set this on creation
                    }

                    // Now, force the LineRenderer to match the PolygonCollider
                    if (polygonCollider != null && polygonCollider.pathCount > 0)
                    {
                        // Get the points from the collider (these are in local space)
                        Vector2[] localPoints = polygonCollider.GetPath(0);

                        // Prepare the LineRenderer
                        lr.positionCount = localPoints.Length;
                        lr.loop = true; // Close the shape to match the collider

                        // Create an array for the world-space points
                        Vector3[] worldPoints = new Vector3[localPoints.Length];

                        // Convert each local point to a world point
                        for (int i = 0; i < localPoints.Length; i++)
                        {
                            worldPoints[i] = go.transform.TransformPoint(localPoints[i]);
                        }

                        // Assign the final world-space points to the LineRenderer
                        lr.SetPositions(worldPoints);
                    }
                }

                // Determine the obstacle type for the shader based on its tag
                int obstacleType = -1;
                if (go.CompareTag("Obstacle")) obstacleType = 1; // Physical obstacle
                else if (go.CompareTag("Ventil")) obstacleType = 2; // Removal zone

                // If it's a valid type, process it
                if (obstacleType != -1)
                {
                    // Get the raw vertex positions from the LineRenderer
                    var vertices = new Vector3[lr.positionCount];
                    lr.GetPositions(vertices);

                    // Create the obstacle data struct for the GPU
                    var obstacleData = new ObstacleData
                    {
                        vertexStart = _gpuVerticesData.Count, // The starting index in the master vertex list
                        vertexCount = lr.positionCount,
                        lineWidth = lr.startWidth,
                        obstacleType = obstacleType,
                        centre = go.transform.position
                    };
                    _gpuObstacleDataList.Add(obstacleData);

                    // Add this obstacle's vertices (in world space) to the master list
                    for (int i = 0; i < lr.positionCount; i++)
                    {
                        _gpuVerticesData.Add(lr.transform.TransformPoint(vertices[i]));
                    }
                }
            }


            // --- 3. Create, Populate, and Bind the GPU Buffers ---

            // Release any old buffers first to prevent memory leaks
            ComputeHelper.Release(vertexBuffer, obstacleBuffer);

            // Create the vertex buffer
            // Use a minimum size of 1 to prevent errors if no obstacles are found
            int vertexBufferSize = Mathf.Max(1, _gpuVerticesData.Count);
            vertexBuffer = ComputeHelper.CreateStructuredBuffer<Vector2>(vertexBufferSize);
            if (_gpuVerticesData.Count > 0)
            {
                vertexBuffer.SetData(_gpuVerticesData);
            }

            // Create the obstacle data buffer
            int obstacleBufferSize = Mathf.Max(1, _gpuObstacleDataList.Count);
            obstacleBuffer = ComputeHelper.CreateStructuredBuffer<ObstacleData>(obstacleBufferSize);
            if (_gpuObstacleDataList.Count > 0)
            {
                obstacleBuffer.SetData(_gpuObstacleDataList);
            }

            // Bind the new buffers and set the count for the shader
            compute.SetBuffer(updatePositionKernel, "VerticesBuffer_Wall", vertexBuffer);
            compute.SetBuffer(updatePositionKernel, "ObstaclesBuffer_Wall", obstacleBuffer);
            Debug.Log("Vertices: " + _gpuVerticesData.Count + ", Obstacles: " + _gpuObstacleDataList.Count);
            compute.SetInt("numObstacles_Wall", _gpuObstacleDataList.Count);
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