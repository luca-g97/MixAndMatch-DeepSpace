using System;
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
    public PlayerColor playerColor;

    private readonly List<PlayerRope> _myRopesToOthers = new();
    private MaterialPropertyBlock _ropeColorBlock;

    private Sequence _currentRopeSequence;
    private static readonly int _BASE_COLOR = Shader.PropertyToID("_BaseColor");

    public bool IsRopeEnabled => _rope.enabled;

    private void Awake()
    {
        playerColor = GetComponentInParent<PlayerColor>();
    }

    private void Start()
    {
        _ropeColorBlock = new MaterialPropertyBlock();
        DisableRope();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.TryGetComponent(out PlayerRope otherPlayerRope))
            return;

        if (playerColor.currentColor == otherPlayerRope.playerColor.currentColor)
        {
            // If the colors are the same, we don't need to create a rope
            // This is to prevent creating ropes between players of the same color
            return;
        }

        // Only one of the two players should create the rope
        if (GetInstanceID() < otherPlayerRope.GetInstanceID())
        {
            if (_myRopesToOthers.Contains(otherPlayerRope))
                return;
            if (_myRopesToOthers.Count >= 1)
            {
                if (otherPlayerRope._myRopesToOthers.Count >= 1)
                {
                    return;
                }

                otherPlayerRope.ShootRope(this);
            }
            else
            {
                ShootRope(otherPlayerRope);
            }
        }
    }

    public void ShootRope(PlayerRope otherPlayerRope)
    {
        if (_myRopesToOthers.Contains(otherPlayerRope))
            return;

        _myRopesToOthers.Add(otherPlayerRope);

        Color otherPlayerColor = otherPlayerRope.GetComponentInParent<PlayerColor>().currentColor;
        Color mixedColor = (playerColor.currentColor + otherPlayerColor) / 2f;

        if (mixedColor == ColorPalette.colorPalette[5]) // If the color is actual green
        {
            mixedColor = ColorPalette.actualGreen;
        }

        EnableRope(otherPlayerRope.transform, mixedColor);
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
    }

    private void EnableRope(Transform endPoint, Color ropeColor)
    {
        _rope.enabled = true;
        _ropeMesh.enabled = true;
        _ropeMeshRenderer.enabled = true;

        _ropeColorBlock.SetColor(_BASE_COLOR, ropeColor);
        _ropeMeshRenderer.SetPropertyBlock(_ropeColorBlock);

        _rope.EndPoint.SetParent(endPoint, true);

        _currentRopeSequence?.Kill();
        _currentRopeSequence = ShootRopeSequence();
    }

    private void DisableRope()
    {
        _rope.EndPoint.SetParent(transform, true);
        _currentRopeSequence?.Kill();
        _currentRopeSequence = ShootRopeSequence().OnComplete(() =>
        {
            _rope.enabled = false;
            _ropeMesh.enabled = false;
            _ropeMeshRenderer.enabled = false;
        });
    }

    private Sequence ShootRopeSequence()
    {
        _currentRopeSequence?.Kill();
        return DOTween.Sequence()
            .Append(_rope.EndPoint.DOLocalMove(Vector3.zero, 0.25f).SetEase(Ease.InCubic));
    }
}