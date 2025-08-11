using System;
using DG.Tweening;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
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
        public bool allowSpawning = true;
        [SerializeField] private ParticleType _particleType;

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
        private bool _isSpawning = false;

        private float _randomSpawnRate;
        private float _randomSpawnPeriod;
        private float _randomSpawnPauseDuration;

        private void Awake()
        {
            _fluidSim = GameObject.FindFirstObjectByType<FluidSim2D>();
            _barrelColorByParticleTypeBlock = new MaterialPropertyBlock();
            _volumetricSphereBlock = new MaterialPropertyBlock();
            AssignSpawnRegionByParticleTyp(_particleType);
        }

        private void Start()
        {
            RerollValues();
        }

        private void OnEnable()
        {
            StopSpawning();
        }

        private void OnDisable()
        {
            StopSpawning();
        }

        private void OnDestroy()
        {
            _currentSpawnSequence?.Kill();
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

            // Check if the condition to spawn is met
            bool shouldBeSpawning = _fluidSim.lastPlayerCount > 0;

            // If we should be spawning but we aren't, start the process
            if (shouldBeSpawning && allowSpawning)
            {
                if (_currentSpawnSequence == null || !_currentSpawnSequence.IsActive() ||
                    !_currentSpawnSequence.IsPlaying())
                {
                    _currentSpawnSequence?.Kill();
                    _currentSpawnSequence = SpawnSequence();
                    _isSpawning = true;
                }
            }
            // If we shouldn't be spawning but we are, stop the process
            else if (_isSpawning)
            {
                StopSpawning();
            }
        }

        private float GetDifficultyMultiplierByPlayerCount(float difficultyInfluence = 1f)
        {
            difficultyInfluence = Mathf.Clamp(difficultyInfluence, 0f, 1f);

            DifficultySettingsData _difficultySettingsData =
                DifficultySettingsManager.Instance.Settings;

            // This method can be used to adjust spawn rates based on player count
            return Mathf.Lerp(1f,
                Mathf.Clamp((float) _fluidSim.lastPlayerCount / _difficultySettingsData.basePlayerCount,
                    _difficultySettingsData.minDifficultyMultiplier,
                    _difficultySettingsData.maxDifficultyMultiplier), difficultyInfluence);
        }

        private void SetColorByParticleType(ParticleType type)
        {
            _currentColor = GetColorByParticleType(type);
            
            if (_currentColor == _colorPalette[5])
            {
                _currentColor = ColorPalette.actualGreen;
            }

            _barrelColorByParticleTypeBlock.SetColor(_BASE_COLOR, _currentColor);
            //_barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR, _currentColor);
            _volumetricSphereBlock.SetColor(_COLOR, _currentColor);
        }

        private Color GetColorByParticleType(ParticleType type)
        {
            List<int> mixableColors = _fluidSim.mixableColors;
            int colorIdx = mixableColors[(int) type - 1];

            if ((int) type <= 0 || (int) type > mixableColors.Count || colorIdx < 0)
            {
                return _fluidSim.colorSymbolizingNoPlayer; // Default color
            }

            return _colorPalette[colorIdx];
        }

        private Sequence SpawnSequence()
        {
            return DOTween.Sequence()
                .AppendCallback(RerollValues)
                .AppendInterval(_randomSpawnPauseDuration)
                .Append(WarningSequence().SetLoops(_warningBlinkRepetitions, LoopType.Restart))
                .AppendCallback((delegate
                {
                    _fireAudioSource.PlayOneShot(_explosionSound);
                    _fireAudioSource.PlayOneShot(_fireSound);
                    _warningAudioSource.pitch =
                        Random.Range(1f - _warningSoundPitchDelta, 1f + _warningSoundPitchDelta);
                    _warningAudioSource.PlayOneShot(_oilDripSound);

                    _explosionEffect.Play();
                    _fireEffect.Play();
                    _currentSpawnRegion.particlesPerSecond = _randomSpawnRate;
                    _barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR,
                        _currentColor * _emissionIntensityWhileSpawningParticles);
                }))
                .AppendInterval(_randomSpawnPeriod)
                .AppendCallback((delegate
                {
                    _fireAudioSource.Stop();
                    _warningAudioSource.Stop();
                    _fireEffect.Stop();
                    _currentSpawnRegion.particlesPerSecond = 0f;
                    _barrelColorByParticleTypeBlock.SetVector(_EMISSION_COLOR, _currentColor * 0f);
                }));
        }

        public void StopSpawning()
        {
            _isSpawning = false;
            _currentSpawnSequence?.Kill();
            _currentSpawnSequence = null;
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
                    _warningAudioSource.pitch =
                        Random.Range(1f - _warningSoundPitchDelta, 1f + _warningSoundPitchDelta);
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

        private void RerollValues()
        {
            _randomSpawnRate = Random.Range(_minSpawnRate, _maxSpawnRate) * GetDifficultyMultiplierByPlayerCount();
            _randomSpawnPeriod = Random.Range(_minSpawnPeriod, _maxSpawnPeriod) *
                                 GetDifficultyMultiplierByPlayerCount(0.5f);
                _randomSpawnPauseDuration =
                Random.Range(_minSpawnPauseDuration, _maxSpawnPauseDuration);
        }
    }
}