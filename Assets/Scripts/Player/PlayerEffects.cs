using System;
using System.Collections.Generic;
using DG.Tweening;
using DG.Tweening.Plugins;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VFX;

public class PlayerEffects : MonoBehaviour
{
    [SerializeField] private Transform _modelTransform;
    [SerializeField] private VisualEffect _oilSplashEffect;
    [SerializeField] private Image _lightCircleImage;
    
    private Sequence _currentCollectSequence;
    private float _defaultModelScale;
    private float _defaultLightCircleAlpha;

    private List<Color> _colorPalette = ColorPalette.colorPalette;

    private void Awake()
    {
        _defaultModelScale = _modelTransform.localScale.x;
        _defaultLightCircleAlpha = _lightCircleImage.color.a;
    }

    private void CollectSequence()
    {
        if (_currentCollectSequence != null && _currentCollectSequence.IsActive())
        {
            return;
        }

        _currentCollectSequence?.Kill();
        _currentCollectSequence = DOTween.Sequence()
            .Append(_modelTransform.DOScale(_defaultModelScale * 1.25f, 0.1f).SetEase(Ease.OutCubic))
            .Join(_lightCircleImage.DOFade(_defaultLightCircleAlpha * 2, 0.1f).SetEase(Ease.OutCubic))
            .Append(_modelTransform.DOScale(_defaultModelScale, 0.1f).SetEase(Ease.OutCubic))
            .Join(_lightCircleImage.DOFade(_defaultLightCircleAlpha, 0.1f).SetEase(Ease.OutCubic));
    }

    public void CollectOil(Color particleColor)
    {
        if (_oilSplashEffect)
        {
            if (particleColor == _colorPalette[5])
            {
                particleColor = ColorPalette.actualGreen;
            }
            
            _oilSplashEffect.SetVector4("Splash Color", particleColor);
            _oilSplashEffect.Play();
        }
        
        CollectSequence();
    }

    private void OnDestroy()
    {
        _currentCollectSequence?.Kill();
    }
}
