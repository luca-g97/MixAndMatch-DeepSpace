using System;
using System.Collections.Generic;
using UnityEngine;

public class OilBarrel: MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Spawner2D_Wall _spawner;
    [SerializeField] private MeshRenderer _barrelMeshRenderer;
    
    [Header("Settings")]
    [SerializeField] private ParticleType _particleType;
    [SerializeField] private float _emissionIntensityWhileSpawningParticles = 3f;
    
    private static readonly int _BASE_COLOR = Shader.PropertyToID("_BaseColor");
    private static readonly int _EMISSION_COLOR = Shader.PropertyToID("_EmissionColor");
    private static List<Color> _colorPalette = ColorPalette.colorPalette;
    private SpawnRegion _currentSpawnRegion;
    private MaterialPropertyBlock _barrelColorByParticleTypeBlock;
    private Color _currentColor;
    
    private void Start()
    {
        _barrelColorByParticleTypeBlock = new MaterialPropertyBlock();
        AssignSpawnRegionByParticleTyp(_particleType);
        SetColorByParticleType(_particleType);
    }
    
    private void AssignSpawnRegionByParticleTyp(ParticleType type)
    {
        foreach (SpawnRegion region in _spawner.spawnRegions)
        {
            if (region.particleType != type) continue;
            
            _currentSpawnRegion = region;
            return;
        }
        
        Debug.LogError($"No spawn region found for particle type: {type}");
    }

    private void Update()
    {
        UpdateEmissionIntensityBySpawnRate(_currentSpawnRegion.particlesPerSecond);
        
        _barrelMeshRenderer.SetPropertyBlock(_barrelColorByParticleTypeBlock);
    }
    
    private void SetColorByParticleType(ParticleType type)
    {
        _currentColor = GetColorByParticleType(type);
        
        _barrelColorByParticleTypeBlock.SetColor(_BASE_COLOR, _currentColor);
        _barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR, _currentColor);
    }
    
    private Color GetColorByParticleType(ParticleType type)
    {
        if ((int)type < 0 || (int)type >= _colorPalette.Count)
        {
            Debug.LogError($"Invalid particle type: {type}");
            return Color.white; // Default color
        }
        
        return _colorPalette[(int)type-1];
    }
    
    private void UpdateEmissionIntensityBySpawnRate(float spawnRate)
    {
        _barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR, _currentColor * 
            (spawnRate > 0 ? _emissionIntensityWhileSpawningParticles : 0f));
    }
}
