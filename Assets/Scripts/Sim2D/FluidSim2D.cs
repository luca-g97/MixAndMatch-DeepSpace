using Seb.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    public class FluidSim2D : MonoBehaviour
    {
        public event System.Action SimulationStepCompleted;

        [Header("Simulation Settings")]
        public float timeScale = 1;
        public float maxTimestepFPS = 60;
        public int iterationsPerFrame;
        public float gravity;
        [Range(0, 1)] public float collisionDamping = 0.95f;
        public float smoothingRadius = 2;
        public float targetDensity;
        public float pressureMultiplier;
        public float nearPressureMultiplier;
        public float viscosityStrength;
        public Vector2 boundsSize;

        [Header("Interaction Settings")]
        public float interactionRadius;
        public float interactionStrength;

        [Header("Mouse Gravity Settings")]
        public float mouseGravityStrength = 10f;
        public float mouseGravityRadius = 5f;
        public bool invertMouseGravity = false;

        [Header("References")]
        public ComputeShader compute;
        public Spawner2D spawner2D;

        // Buffers
        public ComputeBuffer positionBuffer { get; private set; }
        ComputeBuffer predictedPositionBuffer;
        public ComputeBuffer velocityBuffer { get; private set; }
        public ComputeBuffer densityBuffer { get; private set; }
        public ComputeBuffer gravityScaleBuffer { get; private set; }

        public ComputeBuffer obstacleBuffer;
        public ComputeBuffer collisionBuffer;

        ComputeBuffer sortTarget_Position;
        ComputeBuffer sortTarget_PredicitedPosition;
        ComputeBuffer sortTarget_Velocity;

        SpatialHash spatialHash;

        // Kernels
        const int externalForcesKernel = 0;
        const int spatialHashKernel = 1;
        const int reorderKernel = 2;
        const int copybackKernel = 3;
        const int densityKernel = 4;
        const int pressureKernel = 5;
        const int viscosityKernel = 6;
        const int updatePositionKernel = 7;

        // State
        bool isPaused;
        Spawner2D.ParticleSpawnData spawnData;
        bool pauseNextFrame;
        public int numParticles { get; private set; }

        [Header("Obstacles")]
        public List<GameObject> obstacles;
        public ComputeBuffer obstacleColorsBuffer;
        private List<Color> obstacleColorsList = new List<Color>();
        [Min(0)] public float areaToColorAroundObstacles = 0.1f;
        [Min(0)] public float coloredAreaAroundObstaclesDivider = 0.05f;

        private MaterialPropertyBlock _propBlock;
        private Material _sharedUnlitMaterial;

        [StructLayout(LayoutKind.Explicit, Size = 20)]
        public struct ObstacleData
        {
            [FieldOffset(0)] public Vector2 centre;
            [FieldOffset(8)] public int vertexStart;
            [FieldOffset(12)] public int vertexCount;
            [FieldOffset(16)] public float lineWidth;
        }

        // Add vertex buffer
        ComputeBuffer vertexBuffer;
        List<Vector2> allVertices = new List<Vector2>();

        [Header("Obstacle Visualization")]
        public Color obstacleLineColor = Color.green;
        public float obstacleLineWidth = 0.1f;
        public Material lineRendererMaterial; // Assign a material in inspector

        private float autoUpdateInterval = 0.5f;
        private float nextAutoUpdateTime;

        void Start()
        {
            Debug.Log("Controls: Space = Play/Pause, R = Reset, LMB = Attract, RMB = Repel");
            Init();
        }

        void Init()
        {
            Time.fixedDeltaTime = 1 / 60f;
            spawnData = spawner2D.GetSpawnData();
            numParticles = spawnData.positions.Length;
            spatialHash = new SpatialHash(numParticles);

            CreateBuffers();
            SetInitialBufferData(spawnData);

            // Create the property block and shared fallback material once
            _propBlock = new MaterialPropertyBlock();
            // Ensure we have a fallback material if lineRendererMaterial is not set
            if (Shader.Find("Unlit/Color") != null)
                _sharedUnlitMaterial = new Material(Shader.Find("Unlit/Color"));
            else // Basic fallback if Unlit/Color isn't found (less likely)
                _sharedUnlitMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));

            UpdateObstacleBuffer();
            InitComputeShader();
        }

        void CreateBuffers()
        {
            // Main particle buffers
            positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            gravityScaleBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
            collisionBuffer = ComputeHelper.CreateStructuredBuffer<int4>(numParticles);

            // Sorting buffers
            sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

            // Obstacle buffers (initialize with minimum size of 1)
            vertexBuffer = ComputeHelper.CreateStructuredBuffer(new Vector2[1]);
            obstacleBuffer = ComputeHelper.CreateStructuredBuffer(new ObstacleData[1]);
            obstacleColorsBuffer = ComputeHelper.CreateStructuredBuffer(new Color[1]);
        }

        void InitComputeShader()
        {
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityKernel, pressureKernel, viscosityKernel);
            ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "GravityScales", externalForcesKernel);

            ComputeHelper.SetBuffer(compute, obstacleBuffer, "obstaclesBuffer", updatePositionKernel);
            ComputeHelper.SetBuffer(compute, collisionBuffer, "CollisionBuffer", updatePositionKernel);

            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);

            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities", reorderKernel, copybackKernel);

            compute.SetInt("numParticles", numParticles);
            compute.SetBuffer(updatePositionKernel, "verticesBuffer", vertexBuffer);
            compute.SetBuffer(updatePositionKernel, "obstaclesBuffer", obstacleBuffer);

            UpdateObstacleBuffer();
        }

        void Update()
        {
            if (!isPaused)
            {
                float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity;
                float dt = Mathf.Min(Time.deltaTime * timeScale, maxDeltaTime);
                RunSimulationFrame(dt);
            }

            if (pauseNextFrame)
            {
                isPaused = true;
                pauseNextFrame = false;
            }

            HandleInput();

            // Update auto-detected Players periodically
            if (Time.time >= nextAutoUpdateTime)
            {
                UpdateAutoPlayers();
                UpdateObstacleBuffer();
                nextAutoUpdateTime = Time.time + autoUpdateInterval;
            }
            else
            {
                UpdateObstacleBuffer();
            }
        }

        void UpdateAutoPlayers()
        {
            // Find all current Player GameObjects
            var allGameObjects = FindObjectsOfType<GameObject>();
            HashSet<GameObject> currentPlayers = new HashSet<GameObject>();
            foreach (GameObject go in allGameObjects)
            {
                if (go.name.Contains("Player") && go.activeInHierarchy)
                {
                    currentPlayers.Add(go);
                }
            }

            // Add new Players to obstacles if not already present
            foreach (GameObject player in currentPlayers)
            {
                if (!obstacles.Contains(player))
                {
                    obstacles.Add(player);
                }
            }

            // Collect Players in obstacles that are no longer present
            List<GameObject> toRemove = new List<GameObject>();
            foreach (GameObject obstacle in obstacles)
            {
                if (obstacle != null && obstacle.name.Contains("Player"))
                {
                    if (!currentPlayers.Contains(obstacle))
                    {
                        toRemove.Add(obstacle);
                    }
                }
                else if (obstacle == null)
                {
                    toRemove.Add(obstacle);
                }
            }

            // Remove the obsolete Players and clean up LineRenderers
            foreach (GameObject obstacle in toRemove)
            {
                obstacles.Remove(obstacle);
                if (obstacle != null)
                {
                    LineRenderer lr = obstacle.GetComponent<LineRenderer>();
                    if (lr != null)
                    {
                        Destroy(lr);
                    }
                }
            }
        }

        void UpdateObstacleBuffer()
        {
            allVertices.Clear();
            obstacleColorsList.Clear();
            List<ObstacleData> obstacleDataList = new List<ObstacleData>();
            int vertexCounter = 0;

            // Clear existing LineRenderers
            foreach (var obstacle in obstacles.Where(o => o != null).ToList())
            {
                var lr = obstacle.GetComponent<LineRenderer>();
                if (lr != null) DestroyImmediate(lr);
            }

            var players = obstacles.Where(o => o != null && o.activeInHierarchy && o.name.Contains("Player")).ToList();
            Dictionary<GameObject, Color> playerColors = new Dictionary<GameObject, Color>();
            float goldenRatio = 0.61803398875f;

            float baseSaturation = 1f; // TUNABLE: Lower saturation for base colors (e.g., 0.5-0.7)
            float baseValue = 1f;      // TUNABLE: Base value/brightness (e.g., 0.7-0.9)

            for (int i = 0; i < players.Count; i++)
            {
                float hue = (i * goldenRatio) % 1f;
                Color playerColor = Color.HSVToRGB(hue, baseSaturation, baseValue);
                playerColor.a = 1.0f; // Ensure alpha is 1.0
                playerColors[players[i]] = playerColor; // Assign based on current list index 'i'
            }

            int obstacleIndex = 0;
            for (int i = 0; i < obstacles.Count; i++)
            {
                GameObject obstacle = obstacles[i];
                if (!obstacle || !obstacle.activeInHierarchy) continue;

                PolygonCollider2D polyCol = obstacle.GetComponent<PolygonCollider2D>();
                if (!polyCol || polyCol.points.Length < 3) continue;

                // Add vertices
                var points = polyCol.points;
                foreach (var point in points)
                {
                    allVertices.Add(obstacle.transform.TransformPoint(point));
                }

                // Create obstacle data
                obstacleDataList.Add(new ObstacleData
                {
                    centre = polyCol.bounds.center,
                    vertexStart = vertexCounter,
                    vertexCount = points.Length,
                    lineWidth = obstacleLineWidth
                });
                vertexCounter += points.Length;

                // Create LineRenderer
                LineRenderer lr = obstacle.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = true;
                lr.positionCount = points.Length;

                // 1. Assign a SHARED material. Do NOT use .material = new Material(...)
                Material sharedMatToUse = lineRendererMaterial != null ? lineRendererMaterial : _sharedUnlitMaterial;
                if (sharedMatToUse != null) // Ensure we have a material to assign
                {
                    lr.sharedMaterial = sharedMatToUse;
                }
                else
                {
                    Debug.LogError("Cannot assign material to LineRenderer, both lineRendererMaterial and fallback are null!");
                    continue; // Skip this obstacle if no material available
                }

                // Color assignment - optimized for maximum distinction
                Color obstacleColor;
                if (obstacle.name.Contains("Player") && playerColors.ContainsKey(obstacle))
                {
                    // Use pre-calculated distinct color
                    obstacleColor = playerColors[obstacle];
                }
                else
                {
                    // Original rainbow pattern for obstacles (less saturated)
                    float hue = obstacles.Count > 1 ?
                        (float)obstacleIndex / (obstacles.Count - 1) :
                        0.5f;
                    obstacleColor = Color.HSVToRGB(hue, 0.7f, 0.8f);
                }

                // Store color for particle collisions
                obstacleColorsList.Add(obstacleColor);

                // Get the current block data first (good practice in case other properties are set)
                lr.GetPropertyBlock(_propBlock);
                // Set the color property. "_Color" is standard for most shaders.
                // If your 'lineRendererMaterial' uses a different property name for the main color, change "_Color" here.
                _propBlock.SetColor("_Color", obstacleColor);
                // Apply the modified block back to the renderer
                lr.SetPropertyBlock(_propBlock);

                // Width setting
                lr.widthCurve = AnimationCurve.Constant(0, 1, obstacleLineWidth);

                // Set positions
                Vector3[] worldPoints = points.Select(p => obstacle.transform.TransformPoint(p)).ToArray();
                lr.SetPositions(worldPoints);

                obstacleIndex++;
            }

            // Buffer safeguards with minimum size of 1
            Vector2[] verticesArray = allVertices.Count > 0 ? allVertices.ToArray() : new Vector2[] { Vector2.zero };
            ObstacleData[] obstacleArray = obstacleDataList.Count > 0 ? obstacleDataList.ToArray() : new ObstacleData[] { new ObstacleData() };
            Color[] colorArray = obstacleColorsList.Count > 0 ? obstacleColorsList.ToArray() : new Color[] { Color.white };

            // Create or update buffers
            ComputeHelper.CreateStructuredBuffer(ref vertexBuffer, verticesArray);
            ComputeHelper.CreateStructuredBuffer(ref obstacleBuffer, obstacleArray);
            ComputeHelper.CreateStructuredBuffer(ref obstacleColorsBuffer, colorArray);

            // Update compute shader references
            compute.SetBuffer(updatePositionKernel, "verticesBuffer", vertexBuffer);
            compute.SetBuffer(updatePositionKernel, "obstaclesBuffer", obstacleBuffer);
            compute.SetInt("numObstacles", obstacleDataList.Count);
        }

        void UpdateSettings(float deltaTime)
        {
            compute.SetFloat("deltaTime", deltaTime);
            compute.SetFloat("gravity", gravity);
            compute.SetFloat("collisionDamping", collisionDamping);
            compute.SetFloat("smoothingRadius", smoothingRadius);
            compute.SetFloat("targetDensity", targetDensity);
            compute.SetFloat("pressureMultiplier", pressureMultiplier);
            compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
            compute.SetFloat("viscosityStrength", viscosityStrength);
            compute.SetVector("boundsSize", boundsSize);

            compute.SetFloat("Poly6ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
            compute.SetFloat("SpikyPow3ScalingFactor", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
            compute.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
            compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI));
            compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI));

            // Mouse interaction settings
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            bool isPullInteraction = Input.GetMouseButton(0);
            bool isPushInteraction = Input.GetMouseButton(1);
            float currInteractStrength = 0;
            if (isPushInteraction || isPullInteraction)
            {
                currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;
            }

            compute.SetFloat("areaToColorAroundObstacles", areaToColorAroundObstacles);
            compute.SetFloat("coloredAreaAroundObstaclesDivider", coloredAreaAroundObstaclesDivider);

            compute.SetFloat("mouseGravityStrength", mouseGravityStrength);
            compute.SetFloat("mouseGravityRadius", mouseGravityRadius);
            compute.SetInt("invertMouseGravity", invertMouseGravity ? 1 : 0);
            compute.SetVector("mousePosition", mousePos);

            compute.SetVector("interactionInputPoint", mousePos);
            compute.SetFloat("interactionInputStrength", currInteractStrength);
            compute.SetFloat("interactionInputRadius", interactionRadius);

            bool gKeyPressed = Input.GetKey(KeyCode.G);
            compute.SetInt("gKeyPressed", gKeyPressed ? 1 : 0);
        }

        void SetInitialBufferData(Spawner2D.ParticleSpawnData spawnData)
        {
            // Copy position data
            float2[] allPoints = new float2[spawnData.positions.Length];
            System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

            positionBuffer.SetData(allPoints);
            predictedPositionBuffer.SetData(allPoints);
            velocityBuffer.SetData(spawnData.velocities);

            // Initialize gravity scales (default to 1)
            float[] gravityScales = new float[numParticles];
            for (int i = 0; i < numParticles; i++)
            {
                gravityScales[i] = 1f;
            }
            gravityScaleBuffer.SetData(gravityScales);

            // Initialize collision buffer to -1 (no collision)
            int4[] collisionData = new int4[numParticles];
            for (int i = 0; i < collisionData.Length; i++)
            {
                collisionData[i] = new int4(-1, -1, -1, -1);
            }
            collisionBuffer.SetData(collisionData);
        }

        void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                isPaused = !isPaused;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                isPaused = false;
                pauseNextFrame = true;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                isPaused = true;
                SetInitialBufferData(spawnData);
                RunSimulationStep();
                SetInitialBufferData(spawnData);

                // Initialize collision buffer to -1 (no collision)
                int4[] collisionData = new int4[numParticles];
                for (int i = 0; i < collisionData.Length; i++)
                {
                    collisionData[i] = new int4(-1, -1, -1, -1);
                }
                collisionBuffer.SetData(collisionData);
            }
        }

        void RunSimulationFrame(float frameTime)
        {
            float timeStep = frameTime / iterationsPerFrame;
            UpdateSettings(timeStep);

            for (int i = 0; i < iterationsPerFrame; i++)
            {
                RunSimulationStep();
                SimulationStepCompleted?.Invoke();
            }
        }

        void RunSimulationStep()
        {
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: externalForcesKernel);
            RunSpatial();
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: densityKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updatePositionKernel);
        }

        void RunSpatial()
        {
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: spatialHashKernel);
            spatialHash.Run();
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackKernel);
        }

        void OnDestroy()
        {
            ComputeHelper.Release(
                positionBuffer, predictedPositionBuffer, velocityBuffer,
                densityBuffer, gravityScaleBuffer, collisionBuffer,
                sortTarget_Position, sortTarget_Velocity, sortTarget_PredicitedPosition,
                vertexBuffer, obstacleBuffer, obstacleColorsBuffer
            );

            spatialHash?.Release();

            // Clean up the cached shared material
            if (_sharedUnlitMaterial != null)
            {
                if (Application.isEditor && !Application.isPlaying)
                    DestroyImmediate(_sharedUnlitMaterial);
                else
                    Destroy(_sharedUnlitMaterial);
            }

            // Clean up LineRenderers
            foreach (var obstacle in obstacles.Where(o => o != null))
            {
                var lr = obstacle.GetComponent<LineRenderer>();
                if (lr != null) Destroy(lr);
            }
        }
    }
}