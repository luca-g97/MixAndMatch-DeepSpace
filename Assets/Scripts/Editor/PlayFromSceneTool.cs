using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TotemGame.Editor
{
    [InitializeOnLoad]
    public static class PlayFromSceneTool
    {
        private const string _PREFS_KEY = "PlayFromSceneTool_ScenePath";
        private static string _previousScenePath;

        static PlayFromSceneTool()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/Play From Scene/Set Play Mode Scene")]
        public static void SetPlayModeScene()
        {
            string path = EditorUtility.OpenFilePanel("Select Scene to Play From", "Assets", "unity");
            if (!string.IsNullOrEmpty(path))
            {
                if (path.StartsWith(Application.dataPath))
                {
                    string relativePath = "Assets" + path[Application.dataPath.Length..];
                    EditorPrefs.SetString(_PREFS_KEY, relativePath);
                    Debug.Log("Play Mode Scene set to: " + relativePath);
                }
                else
                {
                    Debug.LogError("Scene must be inside the Assets folder.");
                }
            }
        }

        [MenuItem("Tools/Play From Scene/Clear Play Mode Scene")]
        public static void ClearPlayModeScene()
        {
            EditorPrefs.DeleteKey(_PREFS_KEY);
            Debug.Log("Play Mode Scene cleared.");
        }

        [MenuItem("Tools/Play From Scene/Show Current")]
        public static void ShowCurrent()
        {
            string scene = EditorPrefs.GetString(_PREFS_KEY, "None");
            Debug.Log("Current Play Mode Scene: " + scene);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                string scenePath = EditorPrefs.GetString(_PREFS_KEY, "");
                if (!string.IsNullOrEmpty(scenePath))
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        EditorApplication.isPlaying = false;
                        return;
                    }

                    _previousScenePath = SceneManager.GetActiveScene().path;

                    if (EditorSceneManager.OpenScene(scenePath).IsValid())
                    {
                        Debug.Log("Opened Play Mode Scene: " + scenePath);
                    }
                    else
                    {
                        Debug.LogError("Could not open specified play scene: " + scenePath);
                        EditorApplication.isPlaying = false;
                    }
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (!string.IsNullOrEmpty(_previousScenePath))
                {
                    if (EditorSceneManager.OpenScene(_previousScenePath).IsValid())
                    {
                        Debug.Log("Restored original scene: " + _previousScenePath);
                    }
                    else
                    {
                        Debug.LogWarning("Could not restore original scene: " + _previousScenePath);
                    }

                    _previousScenePath = null;
                }
            }
        }
    }
}