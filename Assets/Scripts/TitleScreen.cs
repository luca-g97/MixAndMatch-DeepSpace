using System;
using Assets.Tracking_Example.Scripts;
using Assets.UnityPharusAPI.Managers;
using DG.Tweening;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityPharusAPI.TransmissionFrameworks.Tracklink;

public class TitleScreen : MonoBehaviour
{
    [SerializeField] private RectTransform _ctaTextTransform01;
    [SerializeField] private RectTransform _ctaTextTransform02;
    [SerializeField] private RectTransform _titleTransform;
    [SerializeField] private RectTransform _titleMirrorTransform;
    [SerializeField] private Image _ctaShineImage;
    [SerializeField] private CanvasGroup _ctaTextCanvasGroup;

    [SerializeField] private float _ctaTextYDelta = 5f;
    [SerializeField] private float _ctaUpDuration = 0.5f;
    [SerializeField] private float _ctaDownDuration = 0.5f;
    [SerializeField] private float _ctaPreDelayDuration = 1f;
    [SerializeField] private float _ctaPauseDuration = 0.5f;
    [SerializeField] private float _ctaStayDuration = 2.5f;

    [SerializeField] private float _titleShowHideDuration = 0.5f;
    [SerializeField] private float _titleHiddenOffsetY = -100f;
    [SerializeField] private float _titleMirrorHiddenOffsetY = 100f;

    private float _ctaTextDefaultY;
    private float _titleDefaultY;
    private float _titleMirrorDefaultY;

    private Sequence _ctaLoopSequence;
    private Sequence _titleShowSequence;
    private Tween _ctaShowTween;
    private Sequence _ctaShineLoopSequence;

    private bool _isTitleVisible;
    private ATracklinkPlayerManager _tracklinkPlayerManager;

    private void Awake()
    {
        _ctaTextDefaultY = _ctaTextTransform01.anchoredPosition.y;
        _titleDefaultY = _titleTransform.anchoredPosition.y;
        _titleMirrorDefaultY = _titleMirrorTransform.anchoredPosition.y;

        _tracklinkPlayerManager = FindAnyObjectByType<PIELabTracklinkPlayerManager>(FindObjectsInactive.Exclude);
    }

    private void OnEnable()
    {
        UnityPharusEventProcessor.TrackAdded += OnTrackAdded;
        UnityPharusEventProcessor.TrackRemoved += OnTrackRemoved;
    }
    
    private void OnDisable()
    {
        UnityPharusEventProcessor.TrackAdded -= OnTrackAdded;
        UnityPharusEventProcessor.TrackRemoved -= OnTrackRemoved;
    }

    private void OnDestroy()
    {
        _ctaLoopSequence?.Kill();
        _ctaShineLoopSequence?.Kill();
        _titleShowSequence?.Kill();
        _ctaShowTween?.Kill();
        
        UnityPharusEventProcessor.TrackAdded -= OnTrackAdded;
        UnityPharusEventProcessor.TrackRemoved -= OnTrackRemoved;
    }

    private void Start()
    {
        if (_tracklinkPlayerManager.PlayerList.Count > 0)
        {
            HideTitle();
            _isTitleVisible = false;
        }
        else
        {
            ShowTitle();
            _isTitleVisible = true;
        }
    }

    private void OnTrackRemoved(object sender, UnityPharusEventProcessor.PharusEventTrackArgs e)
    {
        if (_tracklinkPlayerManager.PlayerList.Count == 0)
        {
            if (!_isTitleVisible)
            {
                ShowTitle();
                _isTitleVisible = true;
            }
        }
    }

    private void OnTrackAdded(object sender, UnityPharusEventProcessor.PharusEventTrackArgs e)
    {
        if (_tracklinkPlayerManager.PlayerList.Count > 0)
        {
            if (_isTitleVisible)
            {
                HideTitle();
                _isTitleVisible = false;
            }
        }
    }

    private void StartCtaLoop()
    {
        _ctaLoopSequence?.Kill();
        _ctaTextTransform01.anchoredPosition = new Vector2(_ctaTextTransform01.anchoredPosition.x, _ctaTextDefaultY);
        _ctaTextTransform02.anchoredPosition = new Vector2(_ctaTextTransform02.anchoredPosition.x, _ctaTextDefaultY);
        
        _ctaLoopSequence = CtaSequence();
        _ctaLoopSequence.SetLoops(-1, LoopType.Restart).Play();
        
        _ctaShineLoopSequence?.Kill();
        _ctaShineLoopSequence = CtaShineSequence();
        _ctaShineLoopSequence.SetLoops(-1, LoopType.Restart).Play();
    }
    
    private void StopCtaLoop()
    {
        _ctaLoopSequence?.Kill();
        _ctaLoopSequence = null;
        
        _ctaShineLoopSequence?.Kill();
        _ctaShineLoopSequence = null;
    }
    
    private void ShowTitle()
    {
        _titleShowSequence?.Kill();
        _titleShowSequence = ShowTitleSequence();
        
        _ctaShowTween?.Kill();
        _ctaShowTween = _ctaTextCanvasGroup.DOFade(1f, _titleShowHideDuration).SetEase(Ease.OutCubic);
        StartCtaLoop();
    }
    
    private void HideTitle()
    {
        _titleShowSequence?.Kill();
        _titleShowSequence = HideTitleSequence();
        
        _ctaShowTween?.Kill();
        _ctaShowTween = _ctaTextCanvasGroup.DOFade(0f, _titleShowHideDuration).SetEase(Ease.InCubic);
        
        StopCtaLoop();
    }

    private Sequence CtaSequence()
    {
        Sequence sequence = DOTween.Sequence()
            .AppendInterval(_ctaPreDelayDuration)
            .Append(_ctaTextTransform01.DOAnchorPosY(_ctaTextDefaultY + _ctaTextYDelta, _ctaUpDuration).SetEase(Ease.OutQuint))
            .Insert(_ctaPreDelayDuration + _ctaPauseDuration, _ctaTextTransform02.DOAnchorPosY(_ctaTextDefaultY + _ctaTextYDelta, _ctaUpDuration).SetEase(Ease.OutQuint))
            .AppendInterval(_ctaStayDuration)
            .Append(_ctaTextTransform01.DOAnchorPosY(_ctaTextDefaultY, _ctaDownDuration).SetEase(Ease.InQuint))
            .Join(_ctaTextTransform02.DOAnchorPosY(_ctaTextDefaultY, _ctaDownDuration).SetEase(Ease.InQuint))
            .Pause();

        return sequence;
    }

    private Sequence CtaShineSequence() 
    {
        Sequence sequence = DOTween.Sequence()
            .Append(_ctaShineImage.DOFade(1f, _ctaUpDuration))
            .Append(_ctaShineImage.DOFade(0f, _ctaDownDuration))
            .Pause();

        return sequence;
    }

    private Sequence ShowTitleSequence()
    {
        Sequence sequence = DOTween.Sequence()
            .Append(_titleTransform.DOAnchorPosY(_titleDefaultY, _titleShowHideDuration).SetEase(Ease.OutQuint))
            .Join(_titleMirrorTransform.DOAnchorPosY(_titleMirrorDefaultY, _titleShowHideDuration)
                .SetEase(Ease.OutQuint));

        return sequence;
    }
    
    private Sequence HideTitleSequence()
    {
        Sequence sequence = DOTween.Sequence()
            .Append(_titleTransform.DOAnchorPosY(_titleDefaultY + _titleHiddenOffsetY, _titleShowHideDuration)
                .SetEase(Ease.InQuint))
            .Join(_titleMirrorTransform.DOAnchorPosY(_titleMirrorDefaultY + _titleMirrorHiddenOffsetY,
                _titleShowHideDuration).SetEase(Ease.InQuint));

        return sequence;
    }
}