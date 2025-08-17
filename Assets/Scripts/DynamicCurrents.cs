using NUnit.Framework;
using Seb.Fluid2D.Simulation;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DynamicCurrents : MonoBehaviour
{
    [SerializeField]
    public float maxCurrentStrength = 0.33f;
    [SerializeField, Min(0.01f)]
    private float adaptCurrentTimer = 3.0f;

    [SerializeField]
    private GameObject currentPrefab;

    private float averageY = 0.0f;
    private List<GameObject> currents = new List<GameObject>();
    void Start()
    {
        CreateCurrents();
        StartCoroutine(AdaptCurrentsCR());
    }

    void RecalculateAverageY()
    {
        float tempY = 0.0f;
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject player in players)
        {
            tempY += player.transform.position.y;
        }
        if (players.Length > 0)
        {
            averageY = tempY / players.Length;
        }
        Debug.Log(averageY);
    }

    private void CreateCurrents()
    {
        for (int currentIndex = 0; currentIndex < 2; currentIndex++)
        {
            GameObject tempCurrent = Instantiate(currentPrefab);
            tempCurrent.GetComponent<LineRenderer>().enabled = false;
            tempCurrent.name = "CurrentDyn-" + (currentIndex + 1).ToString("00");
            tempCurrent.transform.parent = GameObject.Find("Currents").transform;
            currents.Add(tempCurrent);
        }
    }

    private IEnumerator AdaptCurrentsCR()
    {
        while (true)
        {
            RecalculateAverageY();
            AdaptCurrents();
            yield return new WaitForSecondsRealtime(adaptCurrentTimer);
        }
    }

    private void AdaptCurrents()
    {
        float boundsSizeX = GameObject.FindFirstObjectByType<FluidSim2D>().boundsSize.x;
        float boundsSizeY = GameObject.FindFirstObjectByType<FluidSim2D>().boundsSize.y / 2.0f;
        float currentRangeX = boundsSizeX / currents.Count;
        int currentCurrent = 0;
        foreach (GameObject current in currents)
        {
            current.GetComponent<LineRenderer>().positionCount = 2;
            Current currentSettings = current.GetComponent<Current>();

            current.GetComponent<LineRenderer>().SetPosition(0, new Vector3(Random.Range(0.0f + (currentCurrent * boundsSizeX / currents.Count) - (boundsSizeX / 2.0f), currentRangeX + (currentCurrent * boundsSizeX / currents.Count) - (boundsSizeX / 2.0f)),
                                                                            Random.Range(boundsSizeY / 1.5f, boundsSizeY),
                                                                            0.0f));
            current.GetComponent<LineRenderer>().SetPosition(1, new Vector3(Random.Range(0.0f + (currentCurrent * boundsSizeX / currents.Count) - (boundsSizeX / 2.0f), currentRangeX + (currentCurrent * boundsSizeX / currents.Count) - (boundsSizeX / 2.0f)),
                                                                            Random.Range(0.0f, boundsSizeY / 1.5f),
                                                                            0.0f));

            currentSettings._minVelocity = maxCurrentStrength * Mathf.InverseLerp(0.0f, boundsSizeY, averageY) - Random.Range(0.0f, maxCurrentStrength * Mathf.InverseLerp(0.0f, boundsSizeY, averageY));
            currentSettings.currentVelocity = maxCurrentStrength * Mathf.InverseLerp(0.0f, boundsSizeY, averageY);
            currentSettings._maxVelocity = maxCurrentStrength * Mathf.InverseLerp(0.0f, boundsSizeY, averageY);

            currentCurrent++;
        }
    }
} 
