// Shader for rendering instanced 2D particles with saturation boost on color mixing.
// Assumes particle data (Position, Velocity, Collision Indices) is provided via Structured Buffers.
// Assumes obstacle base colors (with potentially lower saturation) are provided via Structured Buffer.
// Current date/time: Sunday, April 6, 2025, 3:15 PM CEST (Hagenberg, Austria time)
Shader "Instanced/Particle2D_SaturationBoost_Final" {
    Properties {
        // Exposed property for controlling the saturation boost amount when colors mix
        // Range(1.0, 3.0): 1.0 = no boost, > 1.0 = boost saturation.
        _SaturationBoost ("Saturation Boost on Mix", Range(1.0, 3.0)) = 1.5

        // Texture for mapping particle speed (normalized 0-1) to a base color gradient
        _ColourMap ("Speed Colour Map (RGB)", 2D) = "white" {}

        // Other uniforms like scale and velocityMax are expected to be set via script
        // Add properties for them here if you want material control as well.
        // _Scale ("Scale", Float) = 1.0
        // _VelocityMax ("Max Velocity", Float) = 10.0
    }
    SubShader {
        // Standard transparent rendering setup
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha // Alpha blending for smooth particles
        ZWrite Off // Disable depth writing for transparency

        Pass {
            CGPROGRAM

            // Define vertex and fragment shaders, target appropriate shader model
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5 // Required for StructuredBuffers

            #include "UnityCG.cginc" // Include standard Unity shader functions

            // --- Input Buffers (Populated by Compute Shader / Script) ---
            StructuredBuffer<float2> Positions2D;   // Particle positions (X, Y)
            StructuredBuffer<float2> Velocities;    // Particle velocities (for speed color)
            StructuredBuffer<int4> CollisionBuffer; // Indices of nearby obstacles (-1/-2 if none/other)
            StructuredBuffer<float4> ObstacleColors;// Base RGBA colors of obstacles (RGB assumed less saturated)
            StructuredBuffer<int2> ParticleTypeBuffer;

            // --- Uniforms (Set by Script or Material) ---
            float scale;                // Particle scale factor (usually set via script)
            Texture2D<float4> ColourMap;
            SamplerState linear_clamp_sampler;
            float velocityMax;          // Max velocity for normalizing speed (usually set via script)
            float _SaturationBoost;     // Factor to boost saturation on mixing (from Properties)
            float3 mixableColors[12];
            int numberOfColors;

            // --- Structs ---
            // Data passed from Vertex to Fragment shader
            struct v2f {
                float4 pos : SV_POSITION;   // Clip space position (mandatory)
                float2 uv : TEXCOORD0;      // UV coordinates of the quad (for alpha mask)
                float3 colour : TEXCOORD1;  // Final calculated color for the particle vertex
            };

            // --- Helper Functions: RGB <-> HSV Conversion ---
            // These functions allow manipulation of saturation directly.

            // Converts RGB [0,1] to HSV {H[0,1], S[0,1], V[0,1]}
            float3 RgbToHsv(float3 c) {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y); // range = max(c) - min(c)
                float e = 1.0e-10; // Epsilon to prevent division by zero
                // H: Based on which component is max/min
                // S: range / max(c) (or 0 if max(c) is 0)
                // V: max(c)
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            // Converts HSV {H[0,1], S[0,1], V[0,1]} to RGB [0,1]
            float3 HsvToRgb(float3 c) {
                // c.x = H, c.y = S, c.z = V
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                // Calculate components based on hue sector
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                // Lerp between grey (Value * (1,1,1)) and fully saturated color based on Saturation
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }
            // --- End Helper Functions ---

            float3 saturateColourFurther(float3 colour)
            {
                // Get the boost factor from the material property (ensure it's >= 1.0)
                float boostFactor = max(1.0, _SaturationBoost);

                // Convert the additively mixed color to HSV
                float3 hsv = RgbToHsv(colour);
                // Increase the Saturation value, clamping between 0 and 1
                hsv.y = saturate(hsv.y * boostFactor);
                // Convert back to RGB and update finalColour

                return HsvToRgb(hsv);
            }

            float3 multiplyColourSaturation(float3 colour, float factor)
            {
                float3 hsv = RgbToHsv(colour);

                hsv.y = hsv.y * factor;
                hsv.y = saturate(hsv.y);
                
                return HsvToRgb(hsv);
            }

            float3 setColourSaturation(float3 colour, float saturation)
            {
                // Convert the color to HSV
                float3 hsv = RgbToHsv(colour);
                // Set the saturation to the specified value, clamping between 0 and 1
                hsv.y = saturate(saturation);
                // Convert back to RGB
                return HsvToRgb(hsv);
            }

            float3 multiplyColourLuminance(float3 colour, float factor)
            {
                float3 hsv = RgbToHsv(colour);

                hsv.z = hsv.z * factor; // Boost luminance

                return HsvToRgb(hsv);
            }

            // --- Vertex Shader ---
            // Calculates final vertex color and position for each particle instance.
            v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
            {
                v2f o; // Output structure

                // 1. Calculate base color from speed using the ColourMap texture
                // Determine speed and normalize it
                float speed = length(Velocities[instanceID]);
                float speedT = saturate(speed / velocityMax); // Normalized speed [0,1]
                // Sample the color map (gradient texture) based on normalized speed
                // Using tex2Dlod for explicit Mip level 0 sampling
                float3 baseColour = ColourMap.SampleLevel(linear_clamp_sampler, float2(speedT, 0.5), 0).rgb; // Assuming V=0.5 is middle of texture
                int particleType = ParticleTypeBuffer[instanceID][0];

                static const float COMPARE_EPSILON = 0.001f;

                int particleTypeToUse = particleType-1;

                // 2. Accumulate color influence from nearby obstacles stored in CollisionBuffer
                int4 obstacleIndices = CollisionBuffer[instanceID];
                float3 obstacleColorSum = float3(0, 0, 0); // Sum of influencing obstacle colors
                int obstacleCount = 0; // Number of influencing obstacles
                float3 uniqueColors[4]; // Max 4 unique colors from neighbors

                for (int i = 0; i < 4; i++)
                {
                    int obsIndex = obstacleIndices[i];
                    if (obsIndex >= 0)
                    {
                        float3 obsColor = ObstacleColors[obsIndex].rgb;
                        bool alreadyFound = false;
                    
                        // Check if we've already added this exact color
                        for (int j = 0; j < obstacleCount; j++)
                        {
                            if (distance(obsColor, uniqueColors[j]) < 0.001)
                            {
                                alreadyFound = true;
                                break;
                            }
                        }
                        // If it's a new, unique color, add it to our list.
                        if (!alreadyFound && obstacleCount < 4)
                        {
                            uniqueColors[obstacleCount] = obsColor;
                            obstacleColorSum += obsColor;
                            obstacleCount++;
                        }
                    }
                }

                float3 finalColour = baseColour; // Start with base speed color
                float additiveStrength = 0.7; // TUNABLE: Adjust how strongly obstacle colors influence (e.g., 0.4 to 1.0)

                // 3. Determine final color using ADDITIVE blending and Saturation Boost
                if (obstacleCount > 0 && particleType > 0) // If at least one obstacle is influencing the particle
                {
                    float3 particleColor = mixableColors[particleTypeToUse].rgb;
                    float3 actualObstacleColor = obstacleColorSum / obstacleCount;

                    bool mixableColor = false;
                    float3 exactColor = float3(-1, -1, -1);
                    for (int i = 0; i < numberOfColors; i++)
                    {
                        float3 diff = abs(mixableColors[i].rgb - actualObstacleColor);
                        if(all(diff < COMPARE_EPSILON))
                        {
                            mixableColor = true;
                            if (i == particleTypeToUse)
                            {
                                exactColor = actualObstacleColor; 
                                break;
                            }
                        }
                    }
                    
                    //if(all(abs(actualObstacleColor - float3(0.1, 0.4, 1.0)) < COMPARE_EPSILON)) {actualObstacleColor = saturate(actualObstacleColor * additiveStrength * 2.75);} //Blue
                    //if(all(abs(actualObstacleColor - float3(0.6, 0.25, 0.55)) < COMPARE_EPSILON)) {actualObstacleColor = saturate(actualObstacleColor * additiveStrength * 4);} //Violet
                    //if(all(abs(actualObstacleColor - float3(0.8, 0.175, 0.325)) < COMPARE_EPSILON)) {actualObstacleColor = saturate(actualObstacleColor * additiveStrength * 4);} //Red-Violet
                    //if(all(abs(actualObstacleColor - float3(0.35, 0.325, 0.775)) < COMPARE_EPSILON)) {actualObstacleColor = saturate(actualObstacleColor * additiveStrength * 4);} //Blue-Violet 
                    
                    float3 diff = abs(exactColor - float3(-1, -1, -1));
                    if(all(diff > COMPARE_EPSILON))
                    {
                        finalColour = setColourSaturation(actualObstacleColor, 1);
                        finalColour = multiplyColourLuminance(finalColour, 2);
                    }
                    else if(obstacleCount > 1 && mixableColor) // TODO: not working yet
                    { 
                        finalColour = setColourSaturation(actualObstacleColor, 1);
                        finalColour = multiplyColourLuminance(finalColour, 2.5);
                    }
                    else
                    {
                        finalColour = multiplyColourLuminance(particleColor, 0.75f);
                        finalColour = setColourSaturation(finalColour, 0.75f); //Uncomment to not display other mixed colors
                        //finalColour = setColourSaturation(actualObstacleColor, 0.75f); //Uncomment to also allow other mixed colors
                        //finalColour = saturateColourFurther(finalColour);
                    }
                }
                else if (particleType > 0)
                {
                    float3 playerColour = mixableColors[particleTypeToUse].rgb;

                    //if(all(abs(playerColour - float3(0.1, 0.4, 1.0)) < COMPARE_EPSILON)) {playerColour = saturate(playerColour * additiveStrength * 2);} //Blue
                    //if(all(abs(playerColour - float3(0.6, 0.25, 0.55)) < COMPARE_EPSILON)) {playerColour = saturate(playerColour * additiveStrength * 3);} //Violet
                    //if(all(abs(playerColour - float3(0.8, 0.175, 0.325)) < COMPARE_EPSILON)) {playerColour = saturate(playerColour * additiveStrength * 3);} //Red-Violet
                    //if(all(abs(playerColour - float3(0.35, 0.325, 0.775)) < COMPARE_EPSILON)) {playerColour = saturate(playerColour * additiveStrength * 3);} //Blue-Violet 

                    //finalColour = saturate(playerColour * additiveStrength);
                    finalColour = playerColour;
                    //finalColour = saturateColourFurther(finalColour);
                }

                // 4. Calculate the world position and final clip space position for this vertex
                // Assumes input v.vertex defines a unit quad centered at (0,0)
                // Positions2D provides the center of the particle in world/simulation space (assuming Z=0)
                float3 centreWorld = float3(Positions2D[instanceID].x, Positions2D[instanceID].y, 0);
                // Ensure scale is positive and non-zero
                float nonZeroScale = max(abs(scale), 0.001f);
                // Calculate the vertex position in world space by scaling and offsetting from the center
                // Use unity_ObjectToWorld in case the particle system itself has a transform
                float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, float4(v.vertex.xyz * nonZeroScale, 0)).xyz;
                // Transform from world space to clip space for the GPU rasterizer
                o.pos = mul(UNITY_MATRIX_VP, float4(worldVertPos, 1.0));

                // 5. Assign other outputs to be interpolated for the fragment shader
                o.uv = v.texcoord; // Pass the quad's UV coordinates
                o.colour = finalColour; // Pass the final calculated vertex color

                return o; // Return the data for interpolation
            }


            // --- Fragment Shader ---
            // Calculates the final color and alpha for each pixel of the particle quad.
            float4 frag (v2f i) : SV_Target
            {
                // Use UV coordinates to calculate distance from the particle center
                // Remap UV from [0,1] range to [-1,+1] range relative to center (0.5, 0.5)
                float2 centreOffset = (i.uv.xy - 0.5) * 2.0;
                // Calculate Euclidean distance from the center (0 at center, 1 at edges of quad)
                float dist = sqrt(dot(centreOffset, centreOffset));

                // Use screen-space derivatives of the distance for anti-aliasing
                // fwidth() gives approx width of 1 pixel in terms of 'dist' units
                float delta = fwidth(dist);
                // Use smoothstep to create a soft alpha falloff near the edge (dist=1.0)
                // Alpha is 1.0 inside (dist < 1-delta), 0.0 outside (dist > 1+delta), smooth in between.
                float alpha = 1.0 - smoothstep(1.0 - delta, 1.0 + delta, dist);

                // Get the interpolated vertex color
                float3 colour = i.colour;
                // Output the final color combined with the calculated alpha
                return float4(colour, alpha);
            }

            ENDCG
        } // End Pass
    } // End SubShader
} // End Shader