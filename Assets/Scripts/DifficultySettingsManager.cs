using System.IO;
using UnityEngine;

public class DifficultySettingsManager : MonoBehaviour
{
    public static DifficultySettingsManager Instance { get; private set; }

    public DifficultySettingsData Settings { get; private set; }

    private string _filePath;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _filePath = Path.Combine(Application.streamingAssetsPath, "DifficultySettings.xml");
        Settings = DifficultySettingsData.Load(_filePath);
    }

    public void Save()
    {
        Settings.Save(_filePath);
    }

    public void Reload()
    {
        Settings = DifficultySettingsData.Load(_filePath);
    }
}