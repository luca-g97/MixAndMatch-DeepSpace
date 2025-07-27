using System.Collections.Generic;
using UnityEngine;

public class ColorPalette
{
    public static List<Color> colorPalette = new List<Color> {
        // Primary Colors (Unchanged)
        /* 0: Red */      new Color(1f, 0.1f, 0.1f),
        /* 1: Yellow */   new Color(1f, 1f, 0.1f),
        /* 2: Blue */     new Color(0.1f, 0.4f, 1f),

        // Secondary Colors (Recalculated as Averages)
        /* 3: Orange (R+Y) */ new Color(1f, 0.55f, 0.1f),
        /* 4: Violet (R+B) */ new Color(0.6f, 0.25f, 0.55f),
        /* 5: Green (Y+B) */  new Color(0.25f, 0.75f, 0.25f), // not accurate

        // Tertiary Colors (Recalculated as Averages)
        /* 6: Red-Orange (R+O) */    new Color(1f, 0.325f, 0.1f),
        /* 7: Yellow-Orange (Y+O) */ new Color(1f, 0.775f, 0.1f),
        /* 8: Red-Violet (R+V) */    new Color(0.8f, 0.175f, 0.325f),
        /* 9: Blue-Violet (B+V) */   new Color(0.35f, 0.325f, 0.775f),
        /* 10: Yellow-Green (Y+G) */ new Color(0.625f, 0.875f, 0.175f),
        /* 11: Blue-Green (B+G) */   new Color(0.175f, 0.575f, 0.625f)
    };
    
    public static List<string> colorNames = new List<string> {
        "Red", "Yellow", "Blue",
        "Orange", "Violet", "Green",
        "Red-Orange", "Yellow-Orange", "Red-Violet", "Blue-Violet", "Yellow-Green", "Blue-Green"
    };
}
