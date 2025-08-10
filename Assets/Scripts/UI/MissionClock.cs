using System;
using DG.Tweening;
using KBCore.Refs;
using TMPro;
using UnityEngine;

public class MissionClock : ValidatedMonoBehaviour
{
    [SerializeField] private TMP_Text _missionClockText;
    [SerializeField] private TMP_Text _missionOverText;
    [SerializeField] private TMP_Text _missionRestartText;
    
    [SerializeField, Child] private AudioSource _audioSource;
    [SerializeField] private AudioClip _countdownClip;
    [SerializeField] private AudioClip _missionOverClip;

    private MissionTracker _missionTracker;
    private Sequence _currentTimerSequence;
    
    private bool _updateTimeDisplay = true;

    private void Awake()
    {
        _missionTracker = FindFirstObjectByType<MissionTracker>();
    }

    private void Start()
    {
        _missionClockText.gameObject.SetActive(true);
        _missionOverText.gameObject.SetActive(false);
        _missionRestartText.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _missionTracker.OnMissionOver += OnMissionOverHandler;
        _missionTracker.OnMissionGraded += OnMissionGradedHandler;
        _missionTracker.OnSecondPassed += OnSecondPassedHandler;
    }

    private void OnMissionGradedHandler(int _)
    {
        _missionClockText.gameObject.SetActive(true);
        _missionRestartText.gameObject.SetActive(true);
        _missionOverText.gameObject.SetActive(false);
        
        _missionClockText.alpha = 1f;
        _updateTimeDisplay = true;
        
    }

    private void OnSecondPassedHandler(int _)
    {
        _currentTimerSequence?.Kill();
        _currentTimerSequence = DOTween.Sequence()
            .Append(_missionClockText.DOFade(1, 0f))
            .Append(_missionClockText.DOFade(0.5f, 1f));

        if (_missionTracker.missionIsGraded)
        {
            if (_missionTracker.missionRestartTimeLeft is <= 10 and > 0)
            {
                _audioSource.PlayOneShot(_countdownClip);
            }
        }
        
        else
        {
            if (_missionTracker.missionRuntimeLeft is <= 10 and > 0)
            {
                _audioSource.PlayOneShot(_countdownClip);
            }
        }
    }

    private void OnDisable()
    {
        _missionTracker.OnMissionOver -= OnMissionOverHandler;
    }

    private void Update()
    {
        if (_updateTimeDisplay)
        {
            DisplayTime(!_missionTracker.missionIsGraded
                ? _missionTracker.missionRuntimeLeft
                : _missionTracker.missionRestartTimeLeft);
        }
    }

    private void OnMissionOverHandler()
    {
        _updateTimeDisplay = false;
        
        _currentTimerSequence?.Kill();
        
        BlinkOutLongSequence(_missionClockText).OnComplete(delegate
        {
            _missionOverText.gameObject.SetActive(true);
            _missionClockText.gameObject.SetActive(false);

            BlinkInLongSequence(_missionOverText);
        });
        
        _audioSource.PlayOneShot(_missionOverClip, 2f);
    }

    private static Sequence BlinkOutLongSequence(TMP_Text text)
    {
        return DOTween.Sequence()
            .Append(text.DOFade(0f, 0.1f))
            .Append(text.DOFade(1f, 0.1f))
            .Append(text.DOFade(0f, 0.1f))
            .Append(text.DOFade(1f, 0.1f))
            .Append(text.DOFade(0f, 0.1f));
    }

    private static Sequence BlinkInLongSequence(TMP_Text text)
    {
        return DOTween.Sequence()
            .Append(text.DOFade(1f, 0.1f))
            .Append(text.DOFade(0f, 0.1f))
            .Append(text.DOFade(1f, 0.1f))
            .Append(text.DOFade(0f, 0.1f))
            .Append(text.DOFade(1f, 0.1f));
    }

    private void DisplayTime(float time)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(time);
        _missionClockText.text = $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
    }
}