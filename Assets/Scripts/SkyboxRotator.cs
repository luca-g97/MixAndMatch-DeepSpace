using UnityEngine;

public class SkyboxRotator : MonoBehaviour
{
    [Tooltip("Rotation speed in degrees per second")]
    public float rotationSpeed = 1.0f;

    private float rotation = 0f;

    void Update()
    {
        // Increment rotation
        rotation += rotationSpeed * Time.deltaTime;

        // Keep it between 0 and 360
        if (rotation >= 360f)
            rotation -= 360f;
        else if (rotation < 0f)
            rotation += 360f;

        // Apply rotation to the current skybox material
        RenderSettings.skybox.SetFloat("_Rotation", rotation);
    }
}
