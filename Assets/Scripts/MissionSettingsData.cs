using System;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

[Serializable]
public class MissionSettingsData
{
    public float totalMissionRuntime = 120f;
    public float missionOvertime = 10f;
    public float missionRestartDelayAfterGrade = 30f;
    public int coralPenalty = 10;       
    public int sealPenalty = 50;        
    public int mixedOilCountPerPointRecovered = 10;   

    public static MissionSettingsData Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"Mission settings file not found at {filePath}, creating default.");
                var defaults = new MissionSettingsData();
                defaults.Save(filePath);
                return defaults;
            }

            using var stream = new FileStream(filePath, FileMode.Open);
            var serializer = new XmlSerializer(typeof(MissionSettingsData));
            return (MissionSettingsData)serializer.Deserialize(stream);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading MissionSettingsData: {ex.Message}");
            return new MissionSettingsData();
        }
    }

    public void Save(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            var serializer = new XmlSerializer(typeof(MissionSettingsData));
            serializer.Serialize(stream, this);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error saving MissionSettingsData: {ex.Message}");
        }
    }
}