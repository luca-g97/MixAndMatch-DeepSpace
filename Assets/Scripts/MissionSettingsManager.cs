using System.IO;
using UnityEngine;

public class MissionSettingsManager : MonoBehaviour
{
    public static MissionSettingsManager Instance { get; private set; }
    public MissionSettingsData Settings { get; private set; }

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

        _filePath = Path.Combine(Application.streamingAssetsPath, "MissionSettings.xml");
        Settings = MissionSettingsData.Load(_filePath);
    }

    public void Save()
    {
        Settings.Save(_filePath);
    }

    public void Reload()
    {
        Settings = MissionSettingsData.Load(_filePath);
    }
}