using UnityEditor;
using UnityEngine;

public class PauseOnEnter : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            EditorApplication.isPaused = true;
        }
    }
}
