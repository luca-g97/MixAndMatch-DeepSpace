using UnityEngine;

[CreateAssetMenu(fileName = "DifficultySettingsObject", menuName = "Scriptable Objects/DifficultySettingsObject")]
public class DifficultySettingsObject : ScriptableObject
{
    public int basePlayerCount = 3;
    public float maxDifficultyMultiplier = 2;
    public float minDifficultyMultiplier = 0.5f;
    
}
