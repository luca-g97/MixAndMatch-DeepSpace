using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class PlayerRopeAnchor : MonoBehaviour
{
    private readonly HashSet<PlayerRopeAnchor> _connections = new();
    private PlayerColor _playerColor;

    private void Awake()
    {
        _playerColor = GetComponentInParent<PlayerColor>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.TryGetComponent(out PlayerRopeAnchor otherAnchor))
            return;

        RopeManager.Instance?.Connect(this, otherAnchor);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.TryGetComponent(out PlayerRopeAnchor otherAnchor))
            return;

        RopeManager.Instance?.Disconnect(this, otherAnchor);
    }

    // Called by RopeManager when a connection is established
    public void AddConnection(PlayerRopeAnchor other)
    {
        if (other == null) return;
        _connections.Add(other);
    }

    // Called by RopeManager when a connection is removed
    public void RemoveConnection(PlayerRopeAnchor other)
    {
        if (other == null) return;
        _connections.Remove(other);
    }

    public Color GetPlayerColor() => _playerColor != null ? _playerColor.currentColor : Color.white;

    private void OnDisable()
    {
        // make sure manager cleans up
        RopeManager.Instance?.DisconnectAllFor(this);
        _connections.Clear();
    }
}