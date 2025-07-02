using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DG.Tweening;
using UnityEngine.VFX;
using Random = UnityEngine.Random;


namespace Seb.Fluid2D.Simulation
{
    public class OilBarrel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FluidSim2D _fluidSim;
        [SerializeField] private Spawner2D_Wall _spawner;
        [SerializeField] private MeshRenderer _barrelMeshRenderer;
        [SerializeField] private MeshRenderer _volumetricSphereMeshRenderer;

        [Header("Oil Spawn Settings")]
        [SerializeField] private ParticleType _particleType;

        [SerializeField] private float _preDelayMin = 0.5f;
        [SerializeField] private float _preDelayMax = 5f;
        [SerializeField] private float _minSpawnRate = 15f;
        [SerializeField] private float _maxSpawnRate = 25f;
        [SerializeField] private float _minSpawnPeriod = 1f;
        [SerializeField] private float _maxSpawnPeriod = 5f;
        [SerializeField] private float _minSpawnPauseDuration = 1f;
        [SerializeField] private float _maxSpawnPauseDuration = 5f;

        [SerializeField] private float _warningBlinkSpeed = 0.5f;
        [SerializeField] private int _warningBlinkRepetitions = 3;
        [SerializeField] private float _emissionIntensityWhileSpawningParticles = 3f;

        [Header("VFX")]
        [SerializeField] private VisualEffect _explosionEffect;
        [SerializeField] private VisualEffect _fireEffect;

        [Header("Audio")]
        [SerializeField] private AudioSource _fireAudioSource;
        [SerializeField] private AudioSource _warningAudioSource;
        [SerializeField] private AudioClip _warningSound;
        [SerializeField] private AudioClip _explosionSound;
        [SerializeField] private AudioClip _fireSound;
        [SerializeField] private AudioClip _oilDripSound;
        [SerializeField] private float _warningSoundPitchDelta;

        private static readonly int _COLOR = Shader.PropertyToID("_Color");
        private static readonly int _BASE_COLOR = Shader.PropertyToID("_BaseColor");
        private static readonly int _EMISSION_COLOR = Shader.PropertyToID("_EmissionColor");
        private static List<Color> _colorPalette = ColorPalette.colorPalette;
        private SpawnRegion _currentSpawnRegion;
        private Color _currentColor;

        private MaterialPropertyBlock _barrelColorByParticleTypeBlock;
        private MaterialPropertyBlock _volumetricSphereBlock;

        private Sequence _currentSpawnSequence;

        private void Awake()
        {
            _fluidSim = GameObject.FindFirstObjectByType<FluidSim2D>();
            _barrelColorByParticleTypeBlock = new MaterialPropertyBlock();
            _volumetricSphereBlock = new MaterialPropertyBlock();
            AssignSpawnRegionByParticleTyp(_particleType);
        }

        private void OnEnable()
        {
            StopSpawning();
            StartSpawning();
        }

        private void OnDisable()
        {
            StopSpawning();
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
            SetColorByParticleType(_particleType);
            _barrelMeshRenderer.SetPropertyBlock(_barrelColorByParticleTypeBlock);
            _volumetricSphereMeshRenderer.SetPropertyBlock(_volumetricSphereBlock);
        }

        private void SetColorByParticleType(ParticleType type)
        {
            _currentColor = GetColorByParticleType(type);

            _barrelColorByParticleTypeBlock.SetColor(_BASE_COLOR, _currentColor);
            //_barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR, _currentColor);
            _volumetricSphereBlock.SetColor(_COLOR, _currentColor);
        }

        private Color GetColorByParticleType(ParticleType type)
        {
            List<Color> mixableColors = _fluidSim.mixableColorsForShader;
            if ((int) type < 0 || (int) type >= mixableColors.Count)
            {
                Debug.LogError($"Invalid particle type: {type}");
                return Color.white; // Default color
            }

            return mixableColors[(int) type - 1];
        }

        private void SpawnSequence()
        {
            _currentSpawnSequence?.Kill();

            _currentSpawnSequence = DOTween.Sequence()
                .Append(WarningSequence().SetLoops(_warningBlinkRepetitions, LoopType.Restart))
                .AppendCallback((delegate
                {
                    _fireAudioSource.PlayOneShot(_explosionSound);
                    _fireAudioSource.PlayOneShot(_fireSound);
                    _warningAudioSource.pitch = Random.Range(1f - _warningSoundPitchDelta, 1f + _warningSoundPitchDelta);
                    _warningAudioSource.PlayOneShot(_oilDripSound);
                    
                    _explosionEffect.Play();
                    _fireEffect.Play();
                    _currentSpawnRegion.particlesPerSecond = Random.Range(_minSpawnRate, _maxSpawnRate);
                    _barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR,
                        _currentColor * _emissionIntensityWhileSpawningParticles);
                }))
                .AppendInterval(Random.Range(_minSpawnPeriod, _maxSpawnPeriod))
                .AppendCallback((delegate
                {
                    _fireAudioSource.Stop();
                    _warningAudioSource.Stop();
                    _fireEffect.Stop();
                    _currentSpawnRegion.particlesPerSecond = 0f;
                    _barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR, _currentColor * 0f);
                }))
                .AppendInterval(Random.Range(_minSpawnPauseDuration, _maxSpawnPauseDuration)).Pause();
        }

        private async void StartSpawning()
        {
            await Task.Delay((int) Random.Range(_preDelayMin * 1000, _preDelayMax * 1000));
            SpawnSequence();
            _currentSpawnSequence.Play().SetLoops(-1, LoopType.Restart);
        }

        private void StopSpawning()
        {
            _currentSpawnSequence?.Kill();
            _currentSpawnRegion.particlesPerSecond = 0f;
            _fireAudioSource.Stop();
            _fireEffect.Stop();
            _explosionEffect.Stop();
            _barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR, _currentColor * 0f);
            
        }

        private Sequence WarningSequence()
        {
            return DOTween.Sequence()
                .AppendCallback(delegate
                {
                    _warningAudioSource.pitch = Random.Range(1f - _warningSoundPitchDelta, 1f + _warningSoundPitchDelta);
                    _warningAudioSource.PlayOneShot(_warningSound); 
                    
                })
                .AppendCallback((() =>
                    _barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR,
                        _currentColor * _emissionIntensityWhileSpawningParticles)))
                .AppendInterval(_warningBlinkSpeed)
                .AppendCallback((() =>
                    _barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR, _currentColor * 0f)))
                .AppendInterval(_warningBlinkSpeed);
        }
    }
}