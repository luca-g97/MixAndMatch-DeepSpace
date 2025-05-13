using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
    public class ParticleDisplay2D_Wall : MonoBehaviour
    {
        public FluidSim2D_Wall sim;
        public Mesh mesh;
        public Shader shader;
        public float scale;
        public Gradient colourMap;
        public int gradientResolution;
        public float velocityDisplayMax;

        Material material;
        ComputeBuffer argsBuffer;
        Bounds bounds;
        Texture2D gradientTexture;
        bool needsUpdate = true;

        void Start()
        {
            material = new Material(shader);
        }

        void LateUpdate()
        {
            if (shader != null)
            {
                UpdateSettings();
                Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer);
            }
        }

        void UpdateSettings()
        {

            material.SetBuffer("Positions2D_Wall", sim.positionBuffer);
            material.SetBuffer("Velocities_Wall", sim.velocityBuffer);
            material.SetBuffer("DensityData_Wall", sim.densityBuffer);

            material.SetBuffer("CollisionBuffer_Wall", sim.collisionBuffer);
            material.SetBuffer("ObstacleColors_Wall", sim.obstacleColorsBuffer);
            material.SetBuffer("ParticleTypeBuffer_Wall", sim.particleTypeBuffer);

            material.SetColorArray("mixableColors_Wall", sim.mixableColors);

            ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, sim.positionBuffer.count);
            bounds = new Bounds(Vector3.zero, Vector3.one * 10000);

            if (needsUpdate)
            {
                needsUpdate = false;
                TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
                material.SetTexture("ColourMap_Wall", gradientTexture);

                material.SetFloat("scale_Wall", scale);
                material.SetFloat("velocityMax_Wall", velocityDisplayMax);
            }
        }

        public static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
        {
            if (texture == null)
            {
                texture = new Texture2D(width, 1);
            }
            else if (texture.width != width)
            {
                texture.Reinitialize(width, 1);
            }

            if (gradient == null)
            {
                gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.black, 1) },
                    new GradientAlphaKey[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
                );
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = filterMode;

            Color[] cols = new Color[width];
            for (int i = 0; i < cols.Length; i++)
            {
                float t = i / (cols.Length - 1f);
                cols[i] = gradient.Evaluate(t);
            }

            texture.SetPixels(cols);
            texture.Apply();
        }

        void OnValidate()
        {
            needsUpdate = true;
        }

        void OnDestroy()
        {
            ComputeHelper.Release(argsBuffer);
        }
    }
}