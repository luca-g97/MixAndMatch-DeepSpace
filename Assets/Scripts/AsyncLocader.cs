using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AsyncLocader : MonoBehaviour
{
    void Start()
    {
        StartCoroutine(LoadScene());    
    }

    private IEnumerator LoadScene()
    {
        yield return new WaitForSecondsRealtime(0.1f);
        SceneManager.LoadSceneAsync(1, LoadSceneMode.Additive);
    }
}
