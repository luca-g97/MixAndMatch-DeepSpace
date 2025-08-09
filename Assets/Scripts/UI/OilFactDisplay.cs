using UnityEngine;
using UnityEngine.UI; // Or TMPro if you use TextMeshPro
using System.IO;
using System.Collections.Generic;
using KBCore.Refs;
using TMPro;

[System.Serializable]
public class OilFactsData
{
    public List<string> facts;
}

public class OilFactDisplay : ValidatedMonoBehaviour
{
    [SerializeField, Self] TMP_Text factText; // Assign in Inspector (or TMP_Text if using TextMeshPro)
    private OilFactsData oilFacts;

    private void Start()
    {
        LoadFactsFromJSON();
        RollRandomFact();
    }

    private void LoadFactsFromJSON()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, "OilFacts.json");

        if (File.Exists(filePath))
        {
            string jsonContent = File.ReadAllText(filePath);
            oilFacts = JsonUtility.FromJson<OilFactsData>(jsonContent);
        }
        else
        {
            Debug.LogError("OilFacts.json not found at: " + filePath);
            oilFacts = new OilFactsData { facts = new List<string>() };
        }
    }

    private void RollRandomFact()
    {
        if (oilFacts != null && oilFacts.facts.Count > 0)
        {
            int randomIndex = Random.Range(0, oilFacts.facts.Count);
            factText.text = oilFacts.facts[randomIndex];
        }
        else
        {
            factText.text = "No facts available.";
        }
    }
}