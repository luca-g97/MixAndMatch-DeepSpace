using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class GradeStar : MonoBehaviour
{
    [SerializeField] private Image _starFillImage;
    [SerializeField] private Image _starOutlineImage;
    [SerializeField] private float _starRevealStartScale = 3f;
    [SerializeField] private float _starRevealDuration = 0.5f;

    private RectTransform _starTransform;
    
    private Sequence _currentRevealSequence;
    private Sequence _currentSetupSequence;

    private void Awake()
    {
        _starTransform = _starFillImage.GetComponent<RectTransform>();
    }

    private void Start()
    {
        _starOutlineImage.color = new Color(_starOutlineImage.color.r, _starOutlineImage.color.g, _starOutlineImage.color.b, 0f);
        _starFillImage.color = new Color(_starFillImage.color.r, _starFillImage.color.g, _starFillImage.color.b, 0f);
    }

    private void OnDestroy()
    {
        _currentRevealSequence?.Kill();
        _currentSetupSequence?.Kill();
    }

    public Sequence StarSetupSequence()
    {
        _currentSetupSequence?.Kill();
        return _currentSetupSequence = DOTween.Sequence()
            .Append(_starOutlineImage.DOFade(1f, _starRevealDuration).SetEase(Ease.OutQuint));
    }

    public Sequence StarRevealSequence()
    {
        _starTransform.localScale = Vector3.one * _starRevealStartScale;

        _currentRevealSequence?.Kill();
        return _currentRevealSequence = DOTween.Sequence()
            .Append(_starTransform.DOScale(Vector3.one, _starRevealDuration).SetEase(Ease.OutCubic))
            .Join(_starFillImage.DOFade(1f, _starRevealDuration).SetEase(Ease.OutQuint));
    }
}
