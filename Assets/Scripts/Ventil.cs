using System;
using System.Collections;
using DG.Tweening;
using KBCore.Refs;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VFX;
using UnityUtils;
using Random = UnityEngine.Random;

public class Ventil : ValidatedMonoBehaviour
{
    public event Action<Ventil> VentilDestroyed;

    [Header("References")]
    [SerializeField, Child] private VisualEffect _oilSplashEffect;
    [SerializeField, Child] private Canvas _canvas;
    [SerializeField] private AudioSource _audioSource;
    [SerializeField, Self] private AudioSource _damageAudioSource;
    [SerializeField] private CanvasGroup _deathIconCanvasGroup;
    [SerializeField] private CanvasGroup _shieldIconCanvasGroup;
    [SerializeField] private Image _shieldFillImage;
    [SerializeField] private Transform _modelTransform;

    [Header("Settings")]
    [SerializeField] private float _timeInvulnerableAfterSpawn = 5f;
    [SerializeField] private int _maxHealthPoints = 100;

    [SerializeField] private AudioClip _spawnSound;
    [SerializeField] private AudioClip _destroyedSound;
    [SerializeField] private AudioClip _impactSound;
    [SerializeField] private AudioClip _dropShieldSound;
    
    private int _currentHealthPoints;

    [HideInInspector] public bool IsNotAlive => _currentHealthPoints <= 0;

    private MeshRenderer[] _ventilMeshRenderers;
    private MaterialPropertyBlock _materialPropertyBlock;
    private static readonly int _SATURATION = Shader.PropertyToID("_Saturation");
    private static readonly int _LUMINANCE = Shader.PropertyToID("_Luminance");

    private Sequence _currentSpawnSequence;
    private Sequence _currentColorSequence;
    private Sequence _currentDamageSequence;

    private RectTransform _deathIconRectTransform;
    private RectTransform _shieldIconRectTransform;
    private bool _isInvulnerable;
    private float _defaultModelScale;


    private void Awake()
    {
        _deathIconRectTransform = _deathIconCanvasGroup.GetComponent<RectTransform>();
        _shieldIconRectTransform = _shieldIconCanvasGroup.GetComponent<RectTransform>();
        _materialPropertyBlock = new MaterialPropertyBlock();
        _ventilMeshRenderers = _modelTransform.GetComponentsInChildren<MeshRenderer>();
        
        _defaultModelScale = _modelTransform.localScale.x;
    }

    private void Start()
    {
        _modelTransform.localScale = Vector3.zero;
    }

    public void TakeDamage(int particlesReachedThisFrame, Color particleColor)
    {
        if (IsNotAlive || _isInvulnerable) return;
        _currentHealthPoints -= particlesReachedThisFrame;

        if (_currentHealthPoints <= 0)
        {
            _currentDamageSequence?.Kill();
            _audioSource.PlayOneShot(_destroyedSound, 2f);
            _audioSource.PlayOneShot(_impactSound, 0.5f);
            DestroyedSequence();
            VentilDestroyed?.Invoke(this);
            _isInvulnerable = false;
        }
        else
        {
            _damageAudioSource.pitch = Random.Range(0.5f, 0.75f);
            _damageAudioSource.PlayOneShot(_damageAudioSource.clip);
            DamageSequence();
            UpdateLuminanceByHealth();
            UpdateSaturationByHealth();

            if (_oilSplashEffect)
            {
                _oilSplashEffect.SetVector4("Splash Color", particleColor);
                _oilSplashEffect.Play();
            }
        }
    }

    public void Kill()
    {
        TakeDamage(10000, Color.black);
    }

    private void UpdateSaturationByHealth()
    {
        float saturationByHealth = Helper.RemapRange(_currentHealthPoints, 0, _maxHealthPoints, 0f, 1f);
        _materialPropertyBlock ??= new MaterialPropertyBlock();
        _materialPropertyBlock.SetFloat(_SATURATION, saturationByHealth);

        foreach (MeshRenderer ventilMeshRenderer in _ventilMeshRenderers)
        {
            if (ventilMeshRenderer != null)
            {
                ventilMeshRenderer.SetPropertyBlock(_materialPropertyBlock);
            }
        }
    }

    private void UpdateLuminanceByHealth()
    {
        float brightnessByHealth = Helper.RemapRange(_currentHealthPoints, 0, _maxHealthPoints, 0.25f, 1f);
        _materialPropertyBlock ??= new MaterialPropertyBlock();
        _materialPropertyBlock.SetFloat(_LUMINANCE, brightnessByHealth);

        foreach (MeshRenderer ventilMeshRenderer in _ventilMeshRenderers)
        {
            if (ventilMeshRenderer != null)
            {
                ventilMeshRenderer.SetPropertyBlock(_materialPropertyBlock);
            }
        }
    }

    private void DestroyedSequence()
    {
        _deathIconCanvasGroup.alpha = 0f;
        _deathIconRectTransform.localScale = Vector3.one * 3f;

        _currentSpawnSequence?.Kill();
        _currentSpawnSequence = DOTween.Sequence()
            .Append(_modelTransform.DOScale(0, 0.5f).SetEase(Ease.InBack))
            .Join(_deathIconCanvasGroup.DOFade(1f, 0.5f).SetEase(Ease.OutCubic))
            .Join(_deathIconRectTransform.DOScale(1f, 0.5f).SetEase(Ease.OutCubic))
            .Join(_deathIconRectTransform.DOShakeRotation(strength: 45f, duration: 0.75f))
            .Append(_deathIconRectTransform.DOScale(0, 0.35f).SetEase(Ease.InBack))
            .OnComplete((() => gameObject.SetActive(false)));
    }

    public void SpawnSequence()
    {
        _currentHealthPoints = _maxHealthPoints;
        UpdateLuminanceByHealth();
        UpdateSaturationByHealth();
        gameObject.SetActive(true);
        _audioSource.PlayOneShot(_spawnSound);
        
        StartCoroutine(InvulnerabilityCoroutine());
        
        // UI
        _deathIconCanvasGroup.alpha = 0f;
        _canvas.transform.eulerAngles = new Vector3(0, 0, 0);
        _shieldFillImage.fillAmount = 1f;
        _shieldIconCanvasGroup.alpha = 0f;
        _shieldIconRectTransform.localScale = Vector3.one * 3f;
        _shieldIconRectTransform.localPosition = Vector3.zero;
        
        _currentSpawnSequence?.Kill();
        _currentSpawnSequence = DOTween.Sequence()
            .Append(_modelTransform.DOScale(_defaultModelScale, 0.5f).SetEase(Ease.OutBack))
            .Join(_shieldIconCanvasGroup.DOFade(1f, 0.5f).SetEase(Ease.OutCubic))
            .Join(_shieldIconCanvasGroup.transform.DOScale(1f, 0.5f).SetEase(Ease.OutCubic))
            .Append(_shieldFillImage.DOFillAmount(0f, _timeInvulnerableAfterSpawn).SetEase(Ease.Linear))
            .Append(_shieldIconRectTransform.DOShakeRotation(strength: 30f, duration: 0.5f, fadeOut: false))
            .Join(_shieldIconRectTransform.DOLocalMoveY(-500f, 0.5f).SetEase(Ease.InCubic))
            .Join(_shieldIconCanvasGroup.DOFade(0f, 0.5f).SetEase(Ease.InCubic));
    }

    private void DamageSequence()
    {
        if (_currentDamageSequence != null && _currentDamageSequence.IsActive())
        {
            return;
        }

        _currentDamageSequence?.Kill();
        _currentDamageSequence = DOTween.Sequence()
            .Append(_modelTransform.DOScale(_defaultModelScale * 0.95f, 0.1f).SetEase(Ease.OutCubic))
            .Append(_modelTransform.DOScale(_defaultModelScale, 0.1f).SetEase(Ease.OutCubic));
    }

    private void OnDestroy()
    {
        _currentSpawnSequence?.Kill();
        _currentColorSequence?.Kill();
    }

    private IEnumerator InvulnerabilityCoroutine()
    {
        _isInvulnerable = true;
        yield return new WaitForSeconds(_timeInvulnerableAfterSpawn);
        _isInvulnerable = false;
        _audioSource.PlayOneShot(_dropShieldSound);
    }
}