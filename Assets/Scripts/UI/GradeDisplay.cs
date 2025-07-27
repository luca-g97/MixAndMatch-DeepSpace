using System;
using System.Buffers;
using DG.Tweening;
using KBCore.Refs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GradeDisplay : ValidatedMonoBehaviour
{
    [SerializeField, Child] private TMP_Text _gradeDescriptionText;
    [SerializeField, Child] private GradeStar[] _gradeStars;
    [SerializeField] private RectTransform _shineTransform;
    [SerializeField] private Image _shineImage;

    private WriteText _descriptionWriteText;
    private Sequence _currentGradeSetupSequence;
    private Sequence _currentGradeRevealSequence;
    private Sequence _shineLoopSequence;
    private float _shineDefaultWidth;

    private void Awake()
    {
        _descriptionWriteText = _gradeDescriptionText.GetComponent<WriteText>();
        _shineDefaultWidth = _shineTransform.sizeDelta.x;
    }

    private void Start()
    {
        _shineTransform.sizeDelta = new Vector2(0f, _shineTransform.sizeDelta.y);
    }

    private void OnDestroy()
    {
        _currentGradeSetupSequence?.Kill();
        _currentGradeRevealSequence?.Kill();
    }

    public Sequence GradeSetupSequence()
    {
        _shineTransform.sizeDelta = new Vector2(0f, _shineTransform.sizeDelta.y);
        
        _currentGradeSetupSequence?.Kill();

        _currentGradeSetupSequence = DOTween.Sequence();

        for (int i = 0; i < _gradeStars.Length; i++)
        {
            GradeStar star = _gradeStars[i];
            _currentGradeSetupSequence.Insert(0.15f * i, star.StarSetupSequence());
        }

        return _currentGradeSetupSequence;
    }

    public Sequence GradeRevealSequence(int numberOfStars)
    {
        _currentGradeRevealSequence?.Kill();
        _currentGradeRevealSequence = DOTween.Sequence();

        _currentGradeRevealSequence.AppendCallback(_descriptionWriteText.Write);
        for (int i = 0; i < numberOfStars; i++)
        {
            _currentGradeRevealSequence.Insert(0.3f * i, _gradeStars[i].StarRevealSequence());
        }
        
        _currentGradeRevealSequence.Join(_shineTransform
            .DOSizeDelta(new Vector2(_shineDefaultWidth, _shineTransform.sizeDelta.y), 0.5f)
            .SetEase(Ease.OutBack));
        
        _currentGradeRevealSequence.OnComplete(StartShineLoop);
        
        return _currentGradeRevealSequence;
    }

    public void SetTextFromGrade(int starGrade)
    {
        _gradeDescriptionText.text = starGrade switch
        {
            1 => "Poor",
            2 => "Mediocre",
            3 => "Good",
            4 => "Great",
            5 => "Excellent",
            _ => throw new ArgumentOutOfRangeException(nameof(starGrade), "Invalid star grade value.")
        };
    }

    public void SetShineColorFromGrade(int starGrade)
    {
        _shineImage.color = starGrade switch
        {
            1 => Color.red,
            2 => Color.orange,
            3 => Color.yellow,
            4 => Color.green,
            5 => Color.cyan,
            _ => throw new ArgumentOutOfRangeException(nameof(starGrade), "Invalid star grade value.")
        };
    }

    private void StartShineLoop()
    {
        _shineLoopSequence?.Kill();

        _shineLoopSequence = DOTween.Sequence()
            .Append(_shineImage.DOFade(1f, 0.5f).SetEase(Ease.InOutSine))
            .Append(_shineImage.DOFade(0f, 0.5f).SetEase(Ease.InOutSine));
        
        _shineLoopSequence.SetLoops(-1, LoopType.Restart);
    }
}