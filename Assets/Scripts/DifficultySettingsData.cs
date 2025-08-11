using System;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

[Serializable]
public class DifficultySettingsData
{
    public int basePlayerCount = 4;
    public float maxDifficultyMultiplier = 1.5f;
    public float minDifficultyMultiplier = 0.5f;

    public static DifficultySettingsData Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"Difficulty settings file not found at {filePath}, creating default.");
                var defaults = new DifficultySettingsData();
                defaults.Save(filePath);
                return defaults;
            }

            using var stream = new FileStream(filePath, FileMode.Open);
            var serializer = new XmlSerializer(typeof(DifficultySettingsData));
            return (DifficultySettingsData)serializer.Deserialize(stream);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading DifficultySettingsData: {ex.Message}");
            return new DifficultySettingsData();
        }
    }

    public void Save(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            var serializer = new XmlSerializer(typeof(DifficultySettingsData));
            serializer.Serialize(stream, this);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error saving DifficultySettingsData: {ex.Message}");
        }
    }
}