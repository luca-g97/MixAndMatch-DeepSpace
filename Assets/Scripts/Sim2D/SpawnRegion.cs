using UnityEngine;

[System.Serializable]
public class SpawnRegion
{
    [HideInInspector] public string name;
    public ParticleType particleType;
    public Vector2 position;
    public Vector2 size;
    [Tooltip("For initial particle burst. Particles per unit area.")]
    public float spawnDensity;
    [Tooltip("For continuous spawning. Particles per second.")]
    public float particlesPerSecond;
        
    public Color debugCol;
    internal float spawnAccumulator;
}
