using System;
using DG.Tweening;
using GogoGaga.OptimizedRopesAndCables; // keep if you use that rope package
using UnityEngine;

public class RopeInstance : MonoBehaviour
{
    [SerializeField] private Rope _rope;
    [SerializeField] private RopeMesh _ropeMesh;
    [SerializeField] private MeshRenderer _ropeMeshRenderer;

    private MaterialPropertyBlock _ropeColorBlock;
    private static readonly int _BASE_COLOR = Shader.PropertyToID("_BaseColor");

    private PlayerRopeAnchor _owner;
    private PlayerRopeAnchor _other;
    private Sequence _currentRopeSequence;

    /// <summary>
    /// Called by RopeManager after instantiation.
    /// The prefab will be parented to owner.transform when instantiated, but we also try to zero local position.
    /// </summary>
    public void Initialize(PlayerRopeAnchor owner, PlayerRopeAnchor other, Color ropeColor)
    {
        _owner = owner;
        _other = other;

        if (_ropeMeshRenderer != null)
        {
            _ropeColorBlock = new MaterialPropertyBlock();
            _ropeColorBlock.SetColor(_BASE_COLOR, ropeColor);
            _ropeMeshRenderer.SetPropertyBlock(_ropeColorBlock);
        }

        // ensure prefab is parented to owner and zeroed locally (instantiation parent already set in manager)
        transform.SetParent(owner.transform, true);
        transform.localPosition = Vector3.zero;

        if (_rope != null)
        {
            // Attach the rope's endpoint to the other player's transform, then animate
            _rope.EndPoint.SetParent(other.transform, true);
            EnableRope();
        }
        else
        {
            Debug.LogWarning("RopeInstance: _rope is null. Make sure prefab has a Rope component or implement a fallback.");
        }
    }

    private void EnableRope()
    {
        if (_rope != null) _rope.enabled = true;
        if (_ropeMesh != null) _ropeMesh.enabled = true;
        if (_ropeMeshRenderer != null) _ropeMeshRenderer.enabled = true;

        _currentRopeSequence?.Kill();
        if (_rope != null)
        {
            _currentRopeSequence = DOTween.Sequence()
                .Append(_rope.EndPoint.DOLocalMove(Vector3.zero, 0.25f).SetEase(Ease.InCubic));
        }
    }

    /// <summary>
    /// Animate retraction and destroy the instance.
    /// RopeManager calls this when the pair disconnects.
    /// </summary>
    public void DestroyRope()
    {
        if (_rope != null)
        {
            _currentRopeSequence?.Kill();

            // Animate back to origin (world-space) then destroy
            if (_rope.EndPoint != null)
            {
                _currentRopeSequence = DOTween.Sequence()
                    .Append(_rope.EndPoint.DOMove(transform.position, 0.25f).SetEase(Ease.InCubic))
                    .OnComplete(() => Destroy(gameObject));
            }
            else
            {
                Destroy(gameObject);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        _currentRopeSequence?.Kill();
    }
}
