using System.Collections.Generic;
using UnityEngine;

public class TouchPlayer : MonoBehaviour
{
    public GameObject playerPrefab;
    public Camera floorCam;

    private Dictionary<int, GameObject> trackedObjects = new Dictionary<int, GameObject>();

    void Update()
    {
        if (Input.touchCount > 0)
        {
            foreach (Touch touch in Input.touches)
            {
                ProcessInput(touch.fingerId, touch.position, touch.phase);
            }
        }
    }

    void ProcessInput(int inputId, Vector2 screenPosition, TouchPhase phase)
    {
        Vector3 viewportPoint = floorCam.ScreenToViewportPoint(screenPosition);
        bool inputWithinCamera = viewportPoint.x >= 0 && viewportPoint.x <= 1 &&
                                 viewportPoint.y >= 0 && viewportPoint.y <= 1 &&
                                 viewportPoint.z >= 0;

        if (inputWithinCamera)
        {
            Vector3 currentWorldInputPosition = floorCam.ScreenToWorldPoint(
                new Vector3(screenPosition.x, screenPosition.y, 12.0f)
            );

            switch (phase)
            {
                case TouchPhase.Began:
                    if (!trackedObjects.ContainsKey(inputId))
                    {
                        GameObject newObject = Instantiate(playerPrefab, currentWorldInputPosition, Quaternion.identity);
                        newObject.name = "TouchPlayer_" + inputId;
                        trackedObjects.Add(inputId, newObject);
                    }
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (trackedObjects.TryGetValue(inputId, out GameObject trackedObject))
                    {
                        trackedObject.transform.position = currentWorldInputPosition;
                    }
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (trackedObjects.TryGetValue(inputId, out GameObject objectToDestroy))
                    {
                        trackedObjects.Remove(inputId);
                        Destroy(objectToDestroy);
                    }
                    break;
            }
        }
        else
        {
            if (phase == TouchPhase.Ended || phase == TouchPhase.Canceled)
            {
                if (trackedObjects.TryGetValue(inputId, out GameObject objectToDestroy))
                {
                    Destroy(objectToDestroy);
                    trackedObjects.Remove(inputId);
                }
            }
        }
    }

    void OnValidate()
    {
        if (Display.displays.Length <= 1 && floorCam != null && floorCam.targetDisplay != 0)
        {
            Debug.LogWarning("Focus on wrong Monitor!");
        }
    }

    void OnDisable()
    {
        if (trackedObjects != null)
        {
            foreach (var pair in trackedObjects)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
            }
            trackedObjects.Clear();
        }
    }
}
