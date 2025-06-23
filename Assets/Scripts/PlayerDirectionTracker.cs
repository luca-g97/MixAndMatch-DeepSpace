using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerDirectionTracker : MonoBehaviour
{
    [Tooltip("The number of recent positions to store for calculating the direction.")]
    [SerializeField]
    private int positionHistoryCount = 10;

    [Tooltip("The minimum speed the player must be moving at to update their rotation.")]
    [SerializeField]
    private float minSpeedThreshold = 1f;

    private List<Vector2> recentPositions = new List<Vector2>();

    void FixedUpdate()
    {
        // Add the current position to our list
        recentPositions.Add(transform.position);

        // Ensure the list does not exceed our desired history count
        while (recentPositions.Count > positionHistoryCount)
        {
            recentPositions.RemoveAt(0);
        }

        // Calculate and apply the direction if moving fast enough
        UpdateDirection();
    }

    private void UpdateDirection()
    {
        // We need at least two positions to calculate speed and direction
        if (recentPositions.Count < 2)
        {
            return;
        }

        // Get the oldest and newest positions from our list
        Vector2 oldestPosition = recentPositions[0];
        Vector2 currentPosition = recentPositions[recentPositions.Count - 1];

        // Calculate the distance traveled over the tracked period
        float distance = Vector2.Distance(oldestPosition, currentPosition);

        // Calculate the time elapsed over the tracked period
        // (Count - 1) because there are N-1 intervals between N points in FixedUpdate
        float timeElapsed = (recentPositions.Count - 1) * Time.fixedDeltaTime;

        // Avoid division by zero if for some reason time is zero
        if (timeElapsed <= 0)
        {
            return;
        }

        // Calculate the average speed over the tracked duration
        float speed = distance / timeElapsed;

        // Only update rotation if the player's speed is above the threshold
        if (speed > minSpeedThreshold)
        {
            // We still need the direction vector to calculate the angle
            Vector2 direction = (currentPosition - oldestPosition).normalized;

            // Calculate the angle in degrees
            // Atan2 gives us the angle in radians between the positive X-axis and the point (x, y)
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            // Create a quaternion representing the rotation around the Z-axis
            // The -90.0f offset from your original code is preserved
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle - 90.0f);

            // Apply the rotation to the player's local transform
            transform.localRotation = targetRotation;
        }
    }
}