using UnityEngine;
using UnityEngine.SceneManagement;

public class AsyncLocader : MonoBehaviour
{
    private bool sceneLoaded = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            sceneLoaded = !sceneLoaded;
            HandleSceneLoad();
        } 
    }

    private void HandleSceneLoad()
    {
        if (sceneLoaded)
        {
            SceneManager.LoadSceneAsync(0, LoadSceneMode.Additive);
        }
        else
        {
            SceneManager.UnloadSceneAsync(0);
        }
    }

}
