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
        ComputeBuffer removedParticlesBuffer;
        ComputeBuffer particleTypeBufferCopy;

        ComputeBuffer sortTarget_Position;
        ComputeBuffer sortTarget_PredicitedPosition;
        ComputeBuffer sortTarget_Velocity;
        ComputeBuffer sortTarget_ParticleType;

        public ComputeBuffer obstacleBuffer;
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
        const int copyParticleTypeKernel = 10;
        const int clearRemovedParticlesBuffer = 11;
        const int copyFloatKernel = 12;
        const int copyFloat2Kernel = 13;
        const int copyInt2Kernel = 14;
        const int copyInt4Kernel = 15;
        const int copyParticleDataKernel = 16;

        bool isPaused;
        Spawner2D_Wall.ParticleSpawnData initialSpawnData;
        bool pauseNextFrame;
        public int numParticles { get; private set; }
        private bool isProcessingRemovals = false;

        [Header("Obstacles")]
        public List<GameObject> obstacles = new List<GameObject>();

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

        List<Vector2> _gpuVerticesData = new List<Vector2>();
        List<ObstacleData> _gpuObstacleDataList = new List<ObstacleData>();

        private float autoUpdateInterval = 0.5f;
        private float nextAutoUpdateTime;

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
            GameObject.FindFirstObjectByType<XMLSettings>().GetComponent<XMLSettings>().XMLReload(1);
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

            int initialCapacity = Mathf.Max(1, maxTotalParticles);
            CreateParticleBuffers(initialCapacity);
            compactionInfoBuffer = ComputeHelper.CreateStructuredBuffer<int2>(1);

            if (numParticles > 0)
            {
                SetInitialBufferData(initialSpawnData);
            }

            spatialHash = new SpatialHash_Wall(initialCapacity);

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

            sortTarget_DataBuffer = ComputeHelper.CreateStructuredBuffer<ParticleData>(safeCapacity);

            particleTypeBufferCopy = ComputeHelper.CreateStructuredBuffer<int2>(safeCapacity);
            removedParticlesBuffer = ComputeHelper.CreateStructuredBuffer<float4>(safeCapacity);
        }

        void ReleaseParticleBuffers()
        {
            ComputeHelper.Release(positionBuffer, predictedPositionBuffer, velocityBuffer, densityBuffer,
                gravityScaleBuffer, particleTypeBuffer, particleTypeBufferCopy, sortTarget_DataBuffer,
                compactionInfoBuffer, removedParticlesBuffer);
            positionBuffer = null; predictedPositionBuffer = null; velocityBuffer = null; densityBuffer = null;
            gravityScaleBuffer = null; particleTypeBuffer = null; particleTypeBufferCopy = null; sortTarget_DataBuffer = null;
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
            ComputeHelper.SetBuffer(compute, densityBuffer, "Source_Densities_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "Source_GravityScales_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "Source_ParticleType_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices_Wall", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_DataBuffer, "SortTarget_Data_Wall", reorderKernel);

            // Copyback: Reads SortTarget buffers (via CopySource aliases), writes to main buffers
            ComputeHelper.SetBuffer(compute, sortTarget_DataBuffer, "CopySource_Data_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Densities_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "GravityScales_Wall", copybackKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypeBuffer_Wall", copybackKernel);

            ComputeHelper.SetBuffer(compute, compactionInfoBuffer, "CompactionInfoBuffer_Wall", resetCompactionCounterKernel, compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, removedParticlesBuffer, "RemovedParticlesBuffer_Wall", resetCompactionCounterKernel, compactAndMoveKernel, clearRemovedParticlesBuffer, updatePositionKernel);

            ComputeHelper.SetBuffer(compute, positionBuffer, "Source_Positions_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "Source_PredictedPositions_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Source_Velocities_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Source_Densities_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "Source_GravityScales_Wall", compactAndMoveKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBufferCopy, "Source_ParticleType_Wall", compactAndMoveKernel);

            // Set RW write targets
            ComputeHelper.SetBuffer(compute, sortTarget_DataBuffer, "SortTarget_Data_Wall", compactAndMoveKernel);

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

                if (particleTypeBuffer != null && particleTypeBufferCopy != null && particleTypeBuffer.count == particleTypeBufferCopy.count && numParticles > 0)
                {
                    compute.SetBuffer(copyParticleTypeKernel, "OriginalParticleTypeBuffer_Source_Wall", particleTypeBuffer);
                    compute.SetBuffer(copyParticleTypeKernel, "CopiedParticleTypeBuffer_Destination_Wall", particleTypeBufferCopy);
                    ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copyParticleTypeKernel);
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
                    var removedParticlesData = completedRequest.request.GetData<float4>();
                    if (fluidSim_Floor != null)
                    {
                        fluidSim_Floor.SpawnParticles(removedParticlesData.ToList());
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
                            request = AsyncGPUReadback.Request(removedParticlesBuffer, removedCount * Marshal.SizeOf<float4>(), 0)
                        };

                        // Add it to the queue to be processed in a future frame
                        _pendingRemovalRequests.Enqueue(newPendingRequest);
                    }
                }
                UpdateComputeShaderDynamicParams();
                isProcessingRemovals = false;
            });
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
                    Debug.LogError($"GPUSideResizeAndAppend does not support type: {typeName}");
                    return oldBuffer; // Return the original buffer to avoid errors
            }

            string sourceName = $"Source_{typeName}_Wall";
            string destName = $"Destination_{typeName}_Wall";
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

        void HandleAddingNewParticles(Spawner2D_Wall.ParticleSpawnData newParticleData)
        {
            int newSpawnCount = newParticleData.positions.Length;
            if (newSpawnCount == 0) return;

            int oldNumParticles = numParticles;
            int totalNewParticles = oldNumParticles + newSpawnCount;

            // Ensure we don't exceed the buffer's maximum capacity
            if (totalNewParticles > maxTotalParticles)
            {
                newSpawnCount = maxTotalParticles - oldNumParticles;
                if (newSpawnCount <= 0) return;

                System.Array.Resize(ref newParticleData.positions, newSpawnCount);
                System.Array.Resize(ref newParticleData.velocities, newSpawnCount);
                System.Array.Resize(ref newParticleData.particleTypes, newSpawnCount);
            }

            numParticles += newSpawnCount;

            // --- EFFICIENT DATA UPLOAD ---
            positionBuffer.SetData(newParticleData.positions, 0, oldNumParticles, newSpawnCount);
            predictedPositionBuffer.SetData(newParticleData.positions, 0, oldNumParticles, newSpawnCount);
            velocityBuffer.SetData(newParticleData.velocities, 0, oldNumParticles, newSpawnCount);
            particleTypeBuffer.SetData(newParticleData.particleTypes, 0, oldNumParticles, newSpawnCount);

            // Create and set default data for the other buffers
            float[] newGravityScales = new float[newSpawnCount];
            float2[] newDensityData = new float2[newSpawnCount]; // Defaults to {0,0}

            for (int i = 0; i < newSpawnCount; i++)
            {
                newGravityScales[i] = 1f;
            }

            gravityScaleBuffer.SetData(newGravityScales, 0, oldNumParticles, newSpawnCount);
            densityBuffer.SetData(newDensityData, 0, oldNumParticles, newSpawnCount);

            // The particleTypeBufferCopy and sortTarget_DataBuffer are intermediate buffers used for
            // sorting and compaction. They are already full-sized, so no action is needed here.

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
            spatialHash.Run(numParticles);
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
                if (go.activeInHierarchy && (go.CompareTag("Obstacle_Wall") || go.CompareTag("Ventil_Wall")))
                {
                    if (go.GetComponent<PolygonCollider2D>() != null)
                    {
                        obstaclesToSort.Add(go);
                    }
                }
            }

            obstacles = obstaclesToSort.OrderBy(o => o.CompareTag("Ventil_Wall") ? 0 : 1)
                                       .ThenBy(o => o.GetInstanceID())
                                       .ToList();

            // --- 2. Populate CPU lists with correct data for the GPU ---
            _gpuVerticesData.Clear();
            _gpuObstacleDataList.Clear();

            int currentVertexStartIndex = 0;
            foreach (var obstacleGO in obstacles)
            {
                PolygonCollider2D polyCol = obstacleGO.GetComponent<PolygonCollider2D>();
                if (polyCol == null || polyCol.points.Length < 2) continue;

                var localPoints = polyCol.points;
                int vertexCountForThisObstacle = localPoints.Length;

                // Add this obstacle's vertices (transformed to world space) to the master list
                for (int i = 0; i < vertexCountForThisObstacle; ++i)
                {
                    // Get local point from collider and perform a SINGLE transform to world space.
                    _gpuVerticesData.Add(obstacleGO.transform.TransformPoint(localPoints[i] + polyCol.offset));
                }

                int obsType = obstacleGO.CompareTag("Ventil_Wall") ? 2 : 1;

                _gpuObstacleDataList.Add(new ObstacleData
                {
                    centre = obstacleGO.transform.TransformPoint(polyCol.offset),
                    vertexStart = currentVertexStartIndex,
                    vertexCount = vertexCountForThisObstacle,
                    lineWidth = 0.1f, // You can expose this as a public field if you wish
                    obstacleType = obsType
                });
                currentVertexStartIndex += vertexCountForThisObstacle;
            }

            // --- 3. Create, Populate, and Bind the GPU Buffers ---
            ComputeHelper.CreateStructuredBuffer(ref vertexBuffer, _gpuVerticesData);
            ComputeHelper.CreateStructuredBuffer(ref obstacleBuffer, _gpuObstacleDataList);

            compute.SetBuffer(updatePositionKernel, "VerticesBuffer_Wall", vertexBuffer);
            compute.SetBuffer(updatePositionKernel, "ObstaclesBuffer_Wall", obstacleBuffer);
            compute.SetInt("numObstacles_Wall", _gpuObstacleDataList.Count);
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