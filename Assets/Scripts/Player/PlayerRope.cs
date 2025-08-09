using System.Collections.Generic;
using DG.Tweening;
using GogoGaga.OptimizedRopesAndCables;
using KBCore.Refs;
using UnityEngine;

public class PlayerRope : ValidatedMonoBehaviour
{
    [SerializeField, Child] private Rope _rope;
    [SerializeField, Child] private RopeMesh _ropeMesh;
    [SerializeField, Child] private MeshRenderer _ropeMeshRenderer;

    private readonly List<PlayerRope> _myRopesToOthers = new();
    
    private Sequence _currentRopeSequence;

    private void Start()
    {
        DisableRope();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.TryGetComponent(out PlayerRope otherPlayerRope))
            return;

        // Only one of the two players should create the rope
        if (GetInstanceID() > otherPlayerRope.GetInstanceID())
            return;

        // Prevent duplicates
        if (_myRopesToOthers.Contains(otherPlayerRope))
            return;

        _myRopesToOthers.Add(otherPlayerRope);

        EnableRope(otherPlayerRope.transform);
        
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.TryGetComponent(out PlayerRope otherPlayerRope))
            return;

        if (_myRopesToOthers.Remove(otherPlayerRope))
        {
            

            DisableRope();
        }
    }

    private void OnDisable()
    {
        // Ensure rope is reset if player is disabled
        _myRopesToOthers.Clear();
        DisableRope();
    }

    private void EnableRope(Transform endPoint)
    {
        if (!_rope.enabled)
        {
            _rope.enabled = true;
            _ropeMesh.enabled = true;
            _ropeMeshRenderer.enabled = true;
            
            _rope.EndPoint.SetParent(endPoint, true);
            
            _currentRopeSequence?.Kill();
            _currentRopeSequence = ShootRopeSequence();
        }
    }

    private void DisableRope()
    {
        if (_rope.enabled)
        {
            _rope.EndPoint.SetParent(transform, true);
            
            _currentRopeSequence?.Kill();
            _currentRopeSequence = ShootRopeSequence().OnKill(() => 
            {
                _rope.enabled = false;
                _ropeMesh.enabled = false;
                _ropeMeshRenderer.enabled = false;
            });
        }
    }

    private Sequence ShootRopeSequence()
    {
        _currentRopeSequence?.Kill();
        return DOTween.Sequence()
            .Append(_rope.EndPoint.DOLocalMove(Vector3.zero, 0.5f).SetEase(Ease.InCubic));
    }
}
