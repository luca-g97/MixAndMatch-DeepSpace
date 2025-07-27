using System;
using DG.Tweening;
using KBCore.Refs;
using UnityEngine;

public class AnimatedPanel : ValidatedMonoBehaviour
{
    [SerializeField, Self] private RectTransform _rectTransform;
    [SerializeField] private float _animationDuration = 0.5f;
    [SerializeField] private Ease _animateInEase = Ease.OutCubic;
    [SerializeField] private Ease _animateOutEase = Ease.InCubic;

    [SerializeField] private bool _animateWidth = false;
    [SerializeField] private bool _animateHeight = true;
    [SerializeField] private bool _hideOnStart = true;

    private Vector2 _defaultSizeDelta;

    private Sequence _currentAnimationSequence;

    private void Awake()
    {
        _defaultSizeDelta = _rectTransform.sizeDelta;
    }

    private void Start()
    {
        if (_hideOnStart)
            SetOut();
        else
            SetIn();
    }

    private void OnDestroy()
    {
        _currentAnimationSequence?.Kill();
    }

    public Sequence AnimateInSequence()
    {
        SetOut();
        _currentAnimationSequence?.Kill();

        return _currentAnimationSequence = DOTween.Sequence()
            .Append(_rectTransform.DOSizeDelta(_defaultSizeDelta, _animationDuration).SetEase(_animateInEase));
    }

    private void SetIn()
    {
        _currentAnimationSequence?.Kill();

        _rectTransform.sizeDelta = _defaultSizeDelta;
    }

    public Sequence AnimateOutSequence()
    {
        _currentAnimationSequence?.Kill();

        return _currentAnimationSequence = DOTween.Sequence()
            .Append(_rectTransform
                .DOSizeDelta(
                    new Vector2(_animateWidth ? 0 : _defaultSizeDelta.x, _animateHeight ? 0 : _defaultSizeDelta.y),
                    _animationDuration)
                .SetEase(_animateOutEase));
    }
    
    private void SetOut()
    {
        _currentAnimationSequence?.Kill();

        _rectTransform.sizeDelta = new Vector2(_animateWidth ? 0 : _defaultSizeDelta.x, _animateHeight ? 0 : _defaultSizeDelta.y);
    }
}