using Seb.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Simulation
{
    public class FluidSim2D_Wall : MonoBehaviour
    {
        public event System.Action SimulationStepCompleted;

        [Header("Simulation Settings")]
        public float timeScale = 1;
        public float maxTimestepFPS = 60;
        public int iterationsPerFrame;
        public float gravity;
        [Range(0, 1)] public float collisionDamping = 0.95f;
        public float smoothingRadius = 2;
        public Vector2 boundsSize;
        public float yOffset;

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
        public ComputeBuffer particleTypeBuffer;

        ComputeBuffer sortTarget_Position;
        ComputeBuffer sortTarget_PredicitedPosition;
        ComputeBuffer sortTarget_Velocity;
        ComputeBuffer sortTarget_ParticleType;

        SpatialHash_Wall spatialHash;

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
        public List<Color> mixableColors;
        public List<GameObject> obstacles;
        public ComputeBuffer obstacleColorsBuffer;
        [Min(0)] public float areaToColorAroundObstacles = 0.1f;
        [Min(0)] public float coloredAreaAroundObstaclesDivider = 0.05f;

        private MaterialPropertyBlock _propBlock_Wall;
        private Material _sharedUnlitMaterial;

        [StructLayout(LayoutKind.Explicit, Size = 40)]
        public struct ObstacleData
        {
            [FieldOffset(0)] public Vector2 centre;
            [FieldOffset(8)] public int vertexStart;
            [FieldOffset(12)] public int vertexCount;
            [FieldOffset(16)] public float lineWidth;
            [FieldOffset(20)] public int obstacleType;
            [FieldOffset(24)] public int4 obstacleColorToMix;
        }

        List<Color> playerColorPalette = new List<Color> {
            new Color(0.9f, 0f, 0.4f),    // Red Primary
            new Color(1f, 0.9f, 0f),        // Yellow Primary
            new Color(0.0f, 0.4f, 0.7f),      // Blue Primary

            // Secondary Colors
            new Color(0.95f, 0.55f, 0f),      // Orange
            new Color(0.6f, 0.75f, 0.1f),    // LimeGreen
            new Color(0.6f, 0.1f, 0.5f),    // Violet

            // Tertiary Colors
            new Color(1f, 0.75f, 0f),     // Yellow-Orange
            new Color(0.9f, 0.35f, 0f),      // Red-Orange
            new Color(0.9f, 0f, 0.5f),    // Red-Violet
            new Color(0.4f, 0.3f, 0.6f),  // Blue-Violet
            new Color(0.05f, 0.7f, 0.6f),    // Blue-LimeGreen
            new Color(0.8f, 0.85f, 0f),      // Yellow-Green
        };

        // Add vertex buffer
        ComputeBuffer vertexBuffer;
        List<Vector2> allVertices = new List<Vector2>();
        List<ObstacleData> obstacleDataList = new List<ObstacleData>();
        private List<Color> obstacleColorsList = new List<Color>();
        Dictionary<GameObject, Color> playerColors = new Dictionary<GameObject, Color>();
        System.Random rand = new System.Random();
        public int maxPlayerColors = 6;
        int lastPlayerCount = -1;

        [Header("Obstacle Visualization")]
        public Color obstacleLineColor = Color.white;
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
            spatialHash = new SpatialHash_Wall(numParticles);

            CreateBuffers();
            SetInitialBufferData(spawnData);

            // Create the property block and shared fallback material once
            _propBlock_Wall = new MaterialPropertyBlock();
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
            particleTypeBuffer = ComputeHelper.CreateStructuredBuffer<int>(numParticles);

            // Sorting buffers
            sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_ParticleType = ComputeHelper.CreateStructuredBuffer<int>(numParticles);

            // Obstacle buffers (initialize with minimum size of 1)
            vertexBuffer = ComputeHelper.CreateStructuredBuffer(new Vector2[1]);
            obstacleBuffer = ComputeHelper.CreateStructuredBuffer(new ObstacleData[1]);
            obstacleColorsBuffer = ComputeHelper.CreateStructuredBuffer(new Color[1]);
        }

        void InitComputeShader()
        {
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions_Wall", externalForcesKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions_Wall", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities_Wall", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Densities_Wall", densityKernel, pressureKernel, viscosityKernel);
            ComputeHelper.SetBuffer(compute, gravityScaleBuffer, "GravityScales_Wall", externalForcesKernel);

            ComputeHelper.SetBuffer(compute, obstacleBuffer, "ObstaclesBuffer_Wall", updatePositionKernel);
            ComputeHelper.SetBuffer(compute, collisionBuffer, "CollisionBuffer_Wall", updatePositionKernel);
            ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypeBuffer_Wall", densityKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel);

            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices_Wall", spatialHashKernel, reorderKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets_Wall", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys_Wall", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);

            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions_Wall", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions_Wall", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities_Wall", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleType, "SortTarget_ParticleType_Wall", reorderKernel, copybackKernel);

            compute.SetInt("numParticles_Wall", numParticles);
            compute.SetBuffer(updatePositionKernel, "VerticesBuffer_Wall", vertexBuffer);
            compute.SetBuffer(updatePositionKernel, "ObstaclesBuffer_Wall", obstacleBuffer);

            UpdateObstacleBuffer();
        }

        public bool AreColorsClose(Color color1, Color color2, float tolerance, bool compareAlpha = false)
        {
            float rDiff = color1.r - color2.r;
            float gDiff = color1.g - color2.g;
            float bDiff = color1.b - color2.b;
            float aDiff = compareAlpha ? (color1.a - color2.a) : 0f;

            float sqrMagnitude = (rDiff * rDiff) + (gDiff * gDiff) + (bDiff * bDiff) + (aDiff * aDiff);
            return sqrMagnitude < tolerance * tolerance;
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
            HashSet<GameObject> currentObstacles = new HashSet<GameObject>();
            HashSet<GameObject> currentVentils = new HashSet<GameObject>();
            foreach (GameObject go in allGameObjects)
            {
                if (go.name.Contains("PharusPlayer") && go.activeInHierarchy) { currentPlayers.Add(go); }
                else if (go.name.Contains("Obstacle") && go.activeInHierarchy) { currentObstacles.Add(go); }
                else if (go.name.Contains("Ventil") && go.activeInHierarchy) { currentVentils.Add(go); }

            }

            // Add new Obstacles to obstacles if not already present
            foreach (GameObject obstacle in currentObstacles)
            {
                if (!obstacles.Contains(obstacle))
                {
                    obstacles.Add(obstacle);
                }
            }

            // Add new Ventils to obstacles if not already present
            foreach (GameObject ventil in currentVentils)
            {
                if (!obstacles.Contains(ventil))
                {
                    obstacles.Add(ventil);
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

            // Define a function to get the sort priority
            int GetPriority(GameObject go)
            {
                if (currentPlayers.Contains(go)) return 0;
                if (currentObstacles.Contains(go)) return 1;
                if (currentVentils.Contains(go)) return 2;
                return 3; // Other
            }

            // Order the list using LINQ
            var sortedObstacles = obstacles
                .OrderBy(go => GetPriority(go))
                .ThenBy(go => go.GetInstanceID()) // Optional secondary sort key (e.g., instance ID or name)
                .ToList();

            // Replace the old list with the newly sorted one
            obstacles = sortedObstacles;

            // Collect Players in obstacles that are no longer present
            List<GameObject> toRemove = new List<GameObject>();
            foreach (GameObject obstacle in obstacles)
            {
                if (obstacle != null && obstacle.name.Contains("PharusPlayer"))
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

        int4 RecolorVentil()
        {
            int[] playersToMixArray = new int[4] { -1, -1, -1, -1 }; // Initialize with -1

            // 1. Find the indices of all obstacles that are "PharusPlayer"
            List<int> availablePlayerIndices = new List<int>();
            for (int tempObstacle = 0; tempObstacle < obstacles.Count; tempObstacle++)
            {
                // Check for null before accessing name
                if (obstacles[tempObstacle] != null && obstacles[tempObstacle].name.Contains("PharusPlayer"))
                {
                    availablePlayerIndices.Add(tempObstacle); // Store the index
                }
            }

            // 2. Shuffle the list of available player indices randomly (Fisher-Yates Algorithm)
            for (int player = availablePlayerIndices.Count - 1; player > 0; player--)
            {
                int j = rand.Next(player + 1); // Random index from 0 up to i (inclusive)
                                               // Swap indices at i and j
                int tempIndex = availablePlayerIndices[player];
                availablePlayerIndices[player] = availablePlayerIndices[j];
                availablePlayerIndices[j] = tempIndex;
            }

            // 3. Determine how many unique players we can actually select (max 4)
            int countToSelect = System.Math.Min(3, availablePlayerIndices.Count / 4);

            // 4. Create the result list (or array) and populate it
            List<int> playersToMixList = new List<int>(4);

            if (availablePlayerIndices.Count > 0)
            {
                for (int tempIndex = 0; tempIndex <= countToSelect; tempIndex++)
                {
                    int selectedIndex = availablePlayerIndices[tempIndex];
                    playersToMixList.Add(selectedIndex); // Add to list
                    playersToMixArray[tempIndex] = selectedIndex; // Assign to array slot
                }

                playersToMixArray = playersToMixArray
                    .OrderBy(index => index == -1 ? 1 : 0) // Places -1 after valid indices (0 vs 1)
                    .ThenBy(index => index)               // Sorts valid indices numerically
                    .ToArray();                           // Convert back to array
            }
            return new int4(playersToMixArray[0], playersToMixArray[1], playersToMixArray[2], playersToMixArray[3]);
        }

        void UpdateObstacleBuffer()
        {
            var players = obstacles.Where(o => o != null && o.activeInHierarchy && o.name.Contains("PharusPlayer")).ToList();
            bool playerCountChanged = players.Count != lastPlayerCount;

            if (playerCountChanged)
            {
                playerColors.Clear(); // Clear existing player colors

                int numPaletteColors = playerColorPalette.Count;

                if (numPaletteColors > 0) // Proceed only if the palette has colors
                {
                    // Loop through the current list of players
                    for (int i = 0; i < players.Count; i++)
                    {
                        // Assign colors by cycling through the predefined palette using the modulo operator
                        // This ensures colors repeat if there are more players than palette colors.
                        Color playerColor = playerColorPalette[i % maxPlayerColors];

                        playerColor.a = 1.0f; // Ensure the color is fully opaque

                        // Assign the selected color to the specific player GameObject in the dictionary
                        playerColors[players[i]] = playerColor;
                    }
                }

                mixableColors.Clear();

                if (players.Count >= 3)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        mixableColors.Add(playerColorPalette[i]);
                    }
                }
                ;

                if (players.Count >= 4) { mixableColors.Add(playerColorPalette[6]); mixableColors.Add(playerColorPalette[7]); }
                if (players.Count >= 5) { mixableColors.Add(playerColorPalette[8]); mixableColors.Add(playerColorPalette[9]); }
                if (players.Count > 5) { mixableColors.Add(playerColorPalette[10]); mixableColors.Add(playerColorPalette[11]); }
                ;


                for (int i = players.Count * 2; i < maxPlayerColors * 2; i++)
                {
                    mixableColors.Add(new Color(-1, -1, -1));
                }
            }

            allVertices.Clear();
            obstacleDataList.Clear();
            obstacleColorsList.Clear();

            if (_propBlock_Wall == null) _propBlock_Wall = new MaterialPropertyBlock(); // Ensure block exists

            int vertexCounter = 0;

            for (int i = 0; i < obstacles.Count; i++)
            {
                GameObject obstacle = obstacles[i];
                if (!obstacle || !obstacle.activeInHierarchy) continue;
                PolygonCollider2D polyCol = obstacle.GetComponent<PolygonCollider2D>();
                if (!polyCol || polyCol.points.Length < 3) continue;

                // --- Modified LineRenderer Handling: Get or Add, Don't Destroy ---
                // This preserves the component and its state (like color set via PropertyBlock) across frames.
                if (!obstacle.TryGetComponent<LineRenderer>(out LineRenderer lr))
                {
                    lr = obstacle.AddComponent<LineRenderer>();
                    // Configure properties only needed when adding the component initially
                    lr.useWorldSpace = true;
                    lr.loop = true;
                    // Assign material when adding
                    Material sharedMatToUseOnAdd = lineRendererMaterial != null ? lineRendererMaterial : _sharedUnlitMaterial;
                    if (sharedMatToUseOnAdd != null) { lr.sharedMaterial = sharedMatToUseOnAdd; }
                    else { Debug.LogError($"No material for new LR on {obstacle.name}", obstacle); }
                }

                // Ensure the correct material is assigned (it might change)
                Material sharedMatToUse = lineRendererMaterial != null ? lineRendererMaterial : _sharedUnlitMaterial;
                if (sharedMatToUse != null)
                {
                    // Assign only if needed to avoid unnecessary changes
                    if (lr.sharedMaterial != sharedMatToUse) lr.sharedMaterial = sharedMatToUse;
                }
                else
                {
                    Debug.LogError($"No material available for LR on {obstacle.name}", obstacle);
                    // Maybe continue here if material is essential?
                }

                // Update position count in case collider shape changed
                var points = polyCol.points;
                lr.positionCount = points.Length;
                // --- End Modified LineRenderer Handling ---

                // Add vertices
                int currentVertexStart = vertexCounter;
                foreach (var point in points) { allVertices.Add(obstacle.transform.TransformPoint(point)); }
                vertexCounter += points.Length;

                // Determine Obstacle Type
                int obstacleType = -1;
                if (obstacle.name.Contains("PharusPlayer")) { obstacleType = 0; }
                else if (obstacle.name.Contains("Obstacle")) { obstacleType = 1; }
                else if (obstacle.name.Contains("Ventil")) { obstacleType = 2; }

                // Determine potential influencing players for Ventils (needed for ObstacleData)
                int4 playersToMixIndices = new int4(-1, -1, -1, -1);
                if (obstacleType == 2) { playersToMixIndices = RecolorVentil(); }

                // Create Obstacle Data
                ObstacleData currentObstacleData = new ObstacleData
                {
                    centre = polyCol.bounds.center, // Or calculate centroid if needed
                    vertexStart = currentVertexStart,
                    vertexCount = points.Length,
                    lineWidth = obstacleLineWidth,
                    obstacleType = obstacleType,
                    obstacleColorToMix = playersToMixIndices
                };
                obstacleDataList.Add(currentObstacleData);


                // --- Calculate Final Obstacle Color AND Apply to LineRenderer ---
                Color colorForBufferList; // Color to store in obstacleColorsList for the compute buffer

                if (obstacleType == 0) // Player
                {
                    // Get player color
                    if (!playerColors.TryGetValue(obstacle, out colorForBufferList))
                    {
                        colorForBufferList = Color.magenta; // Fallback
                        Debug.LogWarning($"Player color not found for {obstacle.name}", obstacle);
                    }
                    // Apply color to LR using Property Block
                    _propBlock_Wall.SetColor("_Color", colorForBufferList);
                    lr.SetPropertyBlock(_propBlock_Wall);
                }
                else if (obstacleType == 2) // Ventil
                {
                    // Only RECALCULATE the color if the player count changed
                    if (playerCountChanged)
                    {
                        // --- Perform color mixing calculation ---
                        Color colorSumFromPlayers = Color.clear;
                        int influencingPlayerCount = 0;
                        int4 currentMixIndices = playersToMixIndices; // Use indices determined above
                        for (int j = 0; j < 4; j++)
                        {
                            int playerListIndex = currentMixIndices[j];
                            if (playerListIndex >= 0 && playerListIndex < players.Count)
                            {
                                GameObject influencingPlayer = players[playerListIndex];
                                if (playerColors.TryGetValue(influencingPlayer, out Color basePlayerColor))
                                {
                                    colorSumFromPlayers += basePlayerColor; influencingPlayerCount++;
                                }
                            }
                        }
                        Color mixedColor;
                        if (influencingPlayerCount > 0)
                        {
                            float additiveStrength = 1.0f; mixedColor = colorSumFromPlayers * additiveStrength;
                            mixedColor.r = Mathf.Clamp01(mixedColor.r); mixedColor.g = Mathf.Clamp01(mixedColor.g); mixedColor.b = Mathf.Clamp01(mixedColor.b); mixedColor.a = 1.0f;
                            if (influencingPlayerCount > 1) { float boostFactor = 1.5f; Color.RGBToHSV(mixedColor, out float H, out float S, out float V); S = Mathf.Clamp01(S * boostFactor); mixedColor = Color.HSVToRGB(H, S, V); mixedColor.a = 1.0f; }
                        }
                        else { mixedColor = Color.grey; } // Default color if no influence
                                                          // --- End recalculation ---

                        colorForBufferList = mixedColor; // Use the newly calculated color for the buffer
                                                         // Apply the new color to the LR via Property Block
                        _propBlock_Wall.SetColor("_Color", colorForBufferList);
                        lr.SetPropertyBlock(_propBlock_Wall);
                    }
                    else
                    {
                        // Player count did NOT change.
                        // *** DO NOT apply color to the Line Renderer here. *** It retains its previous color.
                        // We still need a color value for the obstacleColorsList buffer.
                        // Read the *current* color back from the Line Renderer's property block.
                        lr.GetPropertyBlock(_propBlock_Wall); // Populate _propBlock with current LR values
                                                              // Check if the property exists before getting it
                        if (_propBlock_Wall.HasColor("_Color"))
                        {
                            colorForBufferList = _propBlock_Wall.GetColor("_Color");
                        }
                        else
                        {
                            // Fallback if color wasn't set previously or property name mismatch
                            colorForBufferList = Color.grey; // Default color for buffer if readback fails
                            Debug.LogWarning($"Could not read _Color property from LR on {obstacle.name}. Buffer may be inaccurate.", obstacle);
                        }
                    }
                }
                else // Obstacle (Type 1) or Unknown (Type -1)
                {
                    colorForBufferList = Color.white; // Default color
                                                      // Apply color to LR using Property Block
                    _propBlock_Wall.SetColor("_Color", colorForBufferList);
                    lr.SetPropertyBlock(_propBlock_Wall);
                }
                // --- End Color Calculation/Application ---

                // Add the determined color (calculated or read back) to the list for the compute buffer
                obstacleColorsList.Add(colorForBufferList);

                // --- Apply other properties to LineRenderer (like width and positions) ---
                lr.widthCurve = AnimationCurve.Constant(0, 1, obstacleLineWidth);
                Vector3[] worldPoints = points.Select(p => obstacle.transform.TransformPoint(p)).ToArray();
                lr.SetPositions(worldPoints); // Update vertex positions
            }

            // Update state for the next frame
            lastPlayerCount = players.Count;

            // --- Prepare and Update Compute Buffers ---
            Vector2[] verticesArray = allVertices.Count > 0 ? allVertices.ToArray() : new Vector2[] { Vector2.zero };
            ObstacleData[] obstacleArray = obstacleDataList.Count > 0 ? obstacleDataList.ToArray() : new ObstacleData[] { new ObstacleData() };
            Color[] colorArray = obstacleColorsList.Count > 0 ? obstacleColorsList.ToArray() : new Color[] { Color.white };

            ComputeHelper.CreateStructuredBuffer(ref vertexBuffer, verticesArray);
            ComputeHelper.CreateStructuredBuffer(ref obstacleBuffer, obstacleArray);
            ComputeHelper.CreateStructuredBuffer(ref obstacleColorsBuffer, colorArray);

            if (compute != null)
            {
                compute.SetBuffer(updatePositionKernel, "VerticesBuffer_Wall", vertexBuffer);
                compute.SetBuffer(updatePositionKernel, "ObstaclesBuffer_Wall", obstacleBuffer);
                compute.SetInt("numObstacles_Wall", obstacleDataList.Count);
                compute.SetBuffer(updatePositionKernel, "obstacleColorsBuffer", obstacleColorsBuffer); // If needed
            }
            // --- End Buffer Update ---
        }

        void UpdateSettings(float deltaTime)
        {
            compute.SetFloat("deltaTime_Wall", deltaTime);
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

            compute.SetFloat("Poly6ScalingFactor_Wall", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
            compute.SetFloat("SpikyPow3ScalingFactor_Wall", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
            compute.SetFloat("SpikyPow2ScalingFactor_Wall", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
            compute.SetFloat("SpikyPow3DerivativeScalingFactor_Wall", 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI));
            compute.SetFloat("SpikyPow2DerivativeScalingFactor_Wall", 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI));

            // Mouse interaction settings
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            bool isPullInteraction = Input.GetMouseButton(0);
            bool isPushInteraction = Input.GetMouseButton(1);
            float currInteractStrength = 0;
            if (isPushInteraction || isPullInteraction)
            {
                currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;
            }

            compute.SetFloat("areaToColorAroundObstacles_Wall", areaToColorAroundObstacles);
            compute.SetFloat("coloredAreaAroundObstaclesDivider_Wall", coloredAreaAroundObstaclesDivider);

            compute.SetFloat("mouseGravityStrength_Wall", mouseGravityStrength);
            compute.SetFloat("mouseGravityRadius_Wall", mouseGravityRadius);
            compute.SetInt("invertMouseGravity_Wall", invertMouseGravity ? 1 : 0);
            compute.SetVector("mousePosition_Wall", mousePos);

            compute.SetVector("interactionInputPoint_Wall", mousePos);
            compute.SetFloat("interactionInputStrength_Wall", currInteractStrength);
            compute.SetFloat("interactionInputRadius_Wall", interactionRadius);

            bool gKeyPressed = Input.GetKey(KeyCode.G);
            compute.SetInt("gKeyPressed_Wall", gKeyPressed ? 1 : 0);
        }

        void SetInitialBufferData(Spawner2D.ParticleSpawnData spawnData)
        {
            // Copy position data
            float2[] allPoints = new float2[spawnData.positions.Length];
            System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

            positionBuffer.SetData(allPoints);
            predictedPositionBuffer.SetData(allPoints);
            velocityBuffer.SetData(spawnData.velocities);
            particleTypeBuffer.SetData(spawnData.particleTypes);

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
            AsyncGPUReadback.WaitAllRequests(); // Sync before next simulation
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
                densityBuffer, gravityScaleBuffer, collisionBuffer, particleTypeBuffer,
                sortTarget_Position, sortTarget_Velocity, sortTarget_PredicitedPosition,
                sortTarget_ParticleType, vertexBuffer, obstacleBuffer, obstacleColorsBuffer
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