using System;
using System.Collections;
using DG.Tweening;
using KBCore.Refs;
using TMPro;
using UnityEngine;

public class StatLine : MonoBehaviour
{
    [SerializeField] private RectTransform _valueTransform;
    [SerializeField] private RectTransform _descriptionTransform;
    
    [SerializeField] private float _valueRevealStartScale = 3f;
    [SerializeField] private float _valueRevealDuration = 0.5f;
    [SerializeField] private float _durationPadding = 0.25f;
    [SerializeField] private AudioClip _revealSound;
    [SerializeField] private AudioSource _uiAudioSource;
    
    private CanvasGroup _valueCanvasGroup;
    private TMP_Text _descriptionText;
    private TMP_Text _valueText;
    private WriteText _descriptionWriteText;

    private Coroutine _currentTextCoroutine;
    private Sequence _currentRevealSequence;
    
    private void Awake()
    {
        _valueCanvasGroup = _valueTransform.GetComponent<CanvasGroup>();
        _descriptionText = _descriptionTransform.GetComponent<TMP_Text>();
        _valueText = _valueTransform.GetComponentInChildren<TMP_Text>();
        _descriptionWriteText = _descriptionTransform.GetComponent<WriteText>();
    }

    private void OnDestroy()
    {
        _currentRevealSequence?.Kill();
    }

    private void Start()
    {
        _valueCanvasGroup.alpha = 0f;

        _descriptionText.maxVisibleCharacters = 0;
        _descriptionText.ForceMeshUpdate();
    }

    public Sequence StatRevealSequence()
    {
        _valueCanvasGroup.alpha = 0f;
        _valueTransform.localScale = Vector3.one * _valueRevealStartScale;
        
        _currentRevealSequence?.Kill();

        return _currentRevealSequence = DOTween.Sequence()
            .AppendCallback(delegate
            {
                _uiAudioSource.pitch = 1f;
                _uiAudioSource.PlayOneShot(_revealSound);
            })
            .Append(_valueCanvasGroup.DOFade(1f, _valueRevealDuration).SetEase(Ease.InQuint))
            .Join(_valueTransform.DOScale(Vector3.one, _valueRevealDuration).SetEase(Ease.InCubic))
            .AppendCallback(_descriptionWriteText.Write)
            .AppendInterval(_durationPadding);
    }
    
    public void SetDescription(string description)
    {
        _descriptionText.text = description;
    }
    
    public void SetValue(string value, Color valueColor = default)
    {

        if (valueColor == default) valueColor = Color.white;
        
        _valueText.text = value;
        _valueText.color = valueColor;
    }

  
}
