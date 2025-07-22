using System;
using DG.Tweening;
using UnityEngine;

public class PlayerGhost : MonoBehaviour
{
    [SerializeField] private GameObject _normalModel;
    [SerializeField] private GameObject _ghostModel;
    
    [SerializeField] private float _ghostMoveZ = 1f; // Distance to move the ghost model along Z axis
    
    private Sequence _currentSequence;
    
    private int _numberOfCollisions;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Ventil") || other.CompareTag("Obstacle"))
        {
            _numberOfCollisions++;
            // Switch to ghost model
            ShowGhostSequence();
        }
    }
    
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Ventil") || other.CompareTag("Obstacle"))
        {
            _numberOfCollisions--;

            if (_numberOfCollisions <= 0)
            {
                HideGhostSequence();
            }
        }
    }

    private void ShowGhostSequence()
    {
        _normalModel.SetActive(false);
        _ghostModel.SetActive(true);
        
        _ghostModel.transform.localPosition = _normalModel.transform.localPosition;
        
        _currentSequence?.Kill();
        _currentSequence = DOTween.Sequence()
            .Append(_ghostModel.transform.DOLocalMoveZ(_ghostMoveZ, 0.5f).SetEase(Ease.OutCubic));
    }
    
    private void HideGhostSequence()
    {
        _normalModel.SetActive(true);
        _ghostModel.SetActive(false);
        
        _normalModel.transform.localPosition = _ghostModel.transform.localPosition;
        
        _currentSequence?.Kill();
        _currentSequence = DOTween.Sequence()
            .Append(_normalModel.transform.DOLocalMoveZ(0f, 0.25f).SetEase(Ease.OutBack));

    }

    private void OnDestroy()
    {
        // Clean up the sequence if the object is destroyed
        _currentSequence?.Kill();
    }
}
