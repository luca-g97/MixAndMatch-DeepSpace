using System;
using DG.Tweening;
using KBCore.Refs;
using UnityEngine;
using UnityEngine.Audio;

public class ReviewUI : ValidatedMonoBehaviour
{
    [SerializeField, Child] private AnimatedPanel[] _panels;
    [SerializeField, Child] private GradeDisplay _gradeDisplay;
    [SerializeField, Child] private StatLine[] _statLines;
    [SerializeField] private WriteText _factText;
    [SerializeField] private AudioSource _uiAudioSource;
    [SerializeField] private AudioClip _factRevealClip;

    private MissionTracker _missionTracker;

    private Sequence _currentRevealSequence;

    private void Awake()
    {
        _missionTracker = FindFirstObjectByType<MissionTracker>();
    }

    private void OnEnable()
    {
        _missionTracker.OnMissionGraded += OnMissionGraded;
    }
    
    private void OnDisable()
    {
        _missionTracker.OnMissionGraded -= OnMissionGraded;
    }

    private void OnMissionGraded(int starGrade)
    {
        _gradeDisplay.SetTextFromGrade(starGrade);
        _gradeDisplay.SetShineColorFromGrade(starGrade);

        _statLines[0].SetDescription("Total barrels of oil filtered");
        _statLines[0].SetValue(_missionTracker.oilBarrelsFiltered.ToString("D2"));

        _statLines[1].SetDescription("Most oil filtered");

        if (_missionTracker.mostOilFilteredColorIndex == -1)
        {
            _statLines[1].SetValue("None");
        }
        else
        {
            _statLines[1].SetValue(
                ColorPalette.colorNames[_missionTracker.mostOilFilteredColorIndex],
                ColorPalette.colorPalette[_missionTracker.mostOilFilteredColorIndex]);
        }

        _statLines[2].SetDescription("Most efficient collaboration");
        
        if (_missionTracker.mostEfficientCollaborationColorIndex == -1)
        {
            _statLines[2].SetValue("None");
        }
        else
        {

            Color color = ColorPalette.colorPalette[_missionTracker.mostEfficientCollaborationColorIndex];
            if (_missionTracker.mostEfficientCollaborationColorIndex == 5)
            {
                color = ColorPalette.actualGreen;
            }
            _statLines[2].SetValue(ColorPalette.colorNames[_missionTracker.mostEfficientCollaborationColorIndex],
                color);
        }
        
        _statLines[3].SetDescription("Corals died");
        _statLines[3].SetValue(_missionTracker.coralsDied.ToString("D2"));

        _statLines[4].SetDescription("Seals died");
        _statLines[4].SetValue(_missionTracker.sealsDied.ToString("D2"));

        RevealSequence(starGrade);
    }

    private void OnDestroy()
    {
        _currentRevealSequence?.Kill();
    }

    private void RevealSequence(int numberOfStars)
    {
        _currentRevealSequence?.Kill();
        _currentRevealSequence = DOTween.Sequence()
            .Append(_panels[0].AnimateInSequence())
            .Join(_gradeDisplay.GradeSetupSequence())
            .Append(_gradeDisplay.GradeRevealSequence(numberOfStars)) // Assuming 3 stars for the example
            .Append(_panels[1].AnimateInSequence())
            .Append(_statLines[0].StatRevealSequence())
            .Append(_statLines[1].StatRevealSequence())
            .Append(_statLines[2].StatRevealSequence())
            .Append(_statLines[3].StatRevealSequence())
            .Append(_statLines[4].StatRevealSequence())
            .Append(_panels[2].AnimateInSequence())
            .AppendCallback(delegate
            {
                _uiAudioSource.pitch = 1f;
                
                _uiAudioSource.PlayOneShot(_factRevealClip, 0.5f);
                _factText.Write();
            });
    }
}