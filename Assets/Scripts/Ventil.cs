using System;
using DG.Tweening;
using KBCore.Refs;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using UnityUtils;

public class Ventil : ValidatedMonoBehaviour
{
    public event Action<Ventil> VentilDestroyed;
    
    [Header("References")]
    [SerializeField, Child] private MeshRenderer[] _ventilMeshRenderers;
    [SerializeField, Child] private VisualEffect _oilSplashEffect;
    [SerializeField, Child] private CanvasGroup _deathIconCanvasGroup;
    [SerializeField] private Transform _modelTransform;
    
    [Header("Settings")]
    [SerializeField] private int _maxHealthPoints = 100;
    private int _currentHealthPoints;
    
    [HideInInspector] public bool IsNotAlive => _currentHealthPoints <= 0;

    private MaterialPropertyBlock _materialPropertyBlock;
    private static readonly int _SATURATION = Shader.PropertyToID("_Saturation");
    private static readonly int _LUMINANCE = Shader.PropertyToID("_Luminance");

    private Sequence _currentSpawnSequence;
    private Sequence _currentColorSequence;
    private Sequence _currentDamageSequence;
    
    private RectTransform _deathIconRectTransform;


    private void Awake()
    {
        _deathIconRectTransform = _deathIconCanvasGroup.GetComponent<RectTransform>();
        _materialPropertyBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        _modelTransform.localScale = Vector3.zero;
    }

    public void TakeDamage(int particlesReachedThisFrame, Color particleColor)
    {
        if (IsNotAlive) return;
        _currentHealthPoints -= particlesReachedThisFrame;

        if (_currentHealthPoints <= 0)
        {
            _currentDamageSequence?.Kill();
            DestroyedSequence();
            VentilDestroyed?.Invoke(this);
        }
        else
        {
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
            .Join(_deathIconRectTransform.DOShakeRotation(strength: 45f, duration:0.75f))
            .Append(_deathIconRectTransform.DOScale(0, 0.35f).SetEase(Ease.InBack))
            .OnComplete((() => gameObject.SetActive(false)));
    }

    public void SpawnSequence()
    {
        _currentHealthPoints = _maxHealthPoints;
        
        UpdateLuminanceByHealth();
        UpdateSaturationByHealth();
        gameObject.SetActive(true);
        
        _currentSpawnSequence?.Kill();
        _currentSpawnSequence = DOTween.Sequence()
            .Append(_modelTransform.DOScale(1, 0.5f).SetEase(Ease.OutBack));
        
        _deathIconCanvasGroup.alpha = 0f;
        
        Canvas canvas = _deathIconCanvasGroup.GetComponentInParent<Canvas>();
        canvas.transform.eulerAngles = new Vector3(0, 0, 0);
    }

    private void DamageSequence()
    {
        if (_currentDamageSequence != null && _currentDamageSequence.IsActive())
        {
            return;
        }

        _currentDamageSequence?.Kill();
        _currentDamageSequence = DOTween.Sequence()
            .Append(_modelTransform.DOScale(0.95f, 0.1f).SetEase(Ease.OutCubic))
            .Append(_modelTransform.DOScale(1, 0.1f).SetEase(Ease.OutCubic));
    }

    private void OnDestroy()
    {
        _currentSpawnSequence?.Kill();
        _currentColorSequence?.Kill();
    }
}