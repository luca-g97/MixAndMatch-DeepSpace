using UnityEngine;

public class MissionScoring
{
    
    // Tunable values
    public int coralPenalty = 10;       // Points lost per dead coral
    public int sealPenalty = 50;        // Points lost per dead seal
    public int recoveryRate = 10;       // Mixable particles per point regained

    /// <summary>
    /// Calculates the final mission score between 0 and 100.
    /// </summary>
    public int CalculateScore(int deadCorals, int deadSeals, int mixableOilFiltered)
    {
        int baseScore = 100;

        int penalty = (deadCorals * coralPenalty) + (deadSeals * sealPenalty);
        int recovery = mixableOilFiltered / recoveryRate;

        int rawScore = baseScore - penalty + recovery;
        return Mathf.Clamp(rawScore, 0, 100);
    }

    /// <summary>
    /// Converts a score (0–100) into a star rating (1–5).
    /// </summary>
    public int GetStarRating(int score)
    {
        if (score < 20) return 1;
        else if (score < 40) return 2;
        else if (score < 60) return 3;
        else if (score < 80) return 4;
        else return 5;
    }
}

