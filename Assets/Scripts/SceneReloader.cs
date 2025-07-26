using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityUtils;
using Assets.Tracking_Example.Scripts;

public class SceneReloader : MonoBehaviour
{
    private SceneReload sceneReloadInput;
    private PIELabTracklinkPlayerManager playerManager;

    void Start()
    {
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = 60;
        Time.timeScale = 1.0f;
        playerManager = GameObject.FindFirstObjectByType<PIELabTracklinkPlayerManager>().GetComponent<PIELabTracklinkPlayerManager>();
    }

    void Awake()
    {
        sceneReloadInput = new SceneReload();
    }

    void OnEnable()
    {
        sceneReloadInput.SceneReload_Action.Enable();
        sceneReloadInput.SceneReload_Action.RHold.started += action => OnHoldStarted();
        sceneReloadInput.SceneReload_Action.RHold.performed += action => OnHoldFinished();
        sceneReloadInput.SceneReload_Action.RHold.canceled += action => OnHoldReleased();
    }

    void OnDisable()
    {
        sceneReloadInput.SceneReload_Action.Disable();
    }

    private void OnHoldStarted()
    {
        foreach (Transform child in this.transform.Children())
        {
            child.gameObject.SetActive(true);
        }
        playerManager.UpdateBoundaries();
    }

    private void OnHoldFinished()
    {
        StartCoroutine(ReloadScenes());
    }

    private void OnHoldReleased()
    {
        foreach (Transform child in this.transform.Children())
        {
            child.gameObject.SetActive(false);
        }
    }

    private IEnumerator ReloadScenes()
    {
        Time.timeScale = 0.0f;
        AsyncOperation sceneUnload = SceneManager.UnloadSceneAsync(1, UnloadSceneOptions.UnloadAllEmbeddedSceneObjects);
        yield return sceneUnload;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
    }
}
