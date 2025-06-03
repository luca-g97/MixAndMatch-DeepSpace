using UnityEngine;
using System.Collections;

public class ActivateAllDisplays : MonoBehaviour
{
    void Start()
    {
        for (int i = 1; i < Display.displays.Length; i++)
        {
            Display.displays[i].Activate(Display.displays[1].systemWidth,
                                         Display.displays[1].systemHeight, new RefreshRate());
        }
    }
}