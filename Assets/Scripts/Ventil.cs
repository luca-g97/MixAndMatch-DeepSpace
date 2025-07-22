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
    private static readonly int _TINT = Shader.PropertyToID("_Tint");

    private Sequence _currentSpawnSequence;
    private Sequence _currentColorSequence;
    private Sequence _currentDamageSequence;
    
    private static List<Color> _colorPalette = ColorPalette.colorPalette;
    
    private RectTransform _deathIconRectTransform;


    private void Awake()
    {
        _deathIconRectTransform = _deathIconCanvasGroup.GetComponent<RectTransform>();
    }

    private void Start()
    {
        _materialPropertyBlock = new MaterialPropertyBlock();
        _modelTransform.localScale = Vector3.zero;
        SpawnSequence();
    }

    private void Update()
    {
        UpdateSaturationByHealth();

        foreach (MeshRenderer ventilMeshRenderer in _ventilMeshRenderers)
        {
            if (ventilMeshRenderer != null)
            {
                ventilMeshRenderer.SetPropertyBlock(_materialPropertyBlock);
            }
        }
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

    public void UpdateTintByParticleType(int type)
    {
        if (IsNotAlive) return;

        if (type < 0 || type >= _colorPalette.Count)
        {
            return;
        }

        Color color = _colorPalette[type];
        ColoringSequence(color);
    }

    private void UpdateSaturationByHealth()
    {
        float saturationByHealth = Helper.RemapRange(_currentHealthPoints, 0, _maxHealthPoints, 0f, 1f);
        _materialPropertyBlock.SetFloat(_SATURATION, saturationByHealth);
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
            .Append(_deathIconRectTransform.DOScale(0, 0.25f).SetEase(Ease.InBack))
            .OnComplete((() => gameObject.SetActive(false)));
    }

    public void SpawnSequence()
    {
        _currentHealthPoints = _maxHealthPoints;
        _currentSpawnSequence?.Kill();
        gameObject.SetActive(true);
        _currentSpawnSequence = DOTween.Sequence()
            .Append(_modelTransform.DOScale(1, 0.5f).SetEase(Ease.OutBack));
        
        _deathIconCanvasGroup.alpha = 0f;
        
        Canvas canvas = _deathIconCanvasGroup.GetComponentInParent<Canvas>();
        canvas.transform.eulerAngles = new Vector3(0, 0, 0);
    }

    private void ColoringSequence(Color color)
    {
        _currentColorSequence?.Kill();
        _currentColorSequence = DOTween.Sequence()
            .AppendCallback((() => _materialPropertyBlock.SetColor(_TINT, color)));
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