using System;
using NUnit.Framework;
using Seb.Fluid2D.Simulation;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

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
    private GameObject[] players;
    private FluidSim2D fluidSim;

    private void Awake()
    {
        fluidSim = FindFirstObjectByType<FluidSim2D>();
    }

    private void Start()
    {
        GetPlayers();
        CreateCurrents();
        StartCoroutine(AdaptCurrentsCR());
    }

    private void OnEnable()
    {
        fluidSim.OnObstacleRegistered += OnObstacleRegisteredHandler;
        fluidSim.OnObstacleUnregistered += OnObstacleUnregisteredHandler;
    }

    private void OnDisable()
    {
        fluidSim.OnObstacleRegistered -= OnObstacleRegisteredHandler;
        fluidSim.OnObstacleUnregistered -= OnObstacleUnregisteredHandler;
    }

    private void OnObstacleRegisteredHandler(int _)
    {
        GetPlayers();
    }

    private void OnObstacleUnregisteredHandler(int _)
    {
        GetPlayers();
    }

    private void GetPlayers()
    {
        players = GameObject.FindGameObjectsWithTag("Player");
    }

    void RecalculateAverageY()
    {
        float tempY = 0.0f;
        foreach (GameObject player in players)
        {
            if (player)
            {
                tempY += player.transform.position.y;
            }
        }

        if (players.Length > 0)
        {
            averageY = tempY / players.Length;
        }
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
        float boundsSizeX = fluidSim.boundsSize.x;
        float boundsSizeY = fluidSim.boundsSize.y / 2.0f;
        float currentRangeX = boundsSizeX / currents.Count;
        int currentCurrent = 0;
        foreach (GameObject current in currents)
        {
            LineRenderer lineRenderer = current.GetComponent<LineRenderer>();
            lineRenderer.positionCount = 2;
            Current currentSettings = current.GetComponent<Current>();

            lineRenderer.SetPosition(0,
                new Vector3(
                    Random.Range(0.0f + (currentCurrent * boundsSizeX / currents.Count) - (boundsSizeX / 2.0f),
                        currentRangeX + (currentCurrent * boundsSizeX / currents.Count) - (boundsSizeX / 2.0f)),
                    Random.Range(boundsSizeY / 1.5f, boundsSizeY),
                    0.0f));
            lineRenderer.SetPosition(1,
                new Vector3(
                    Random.Range(0.0f + (currentCurrent * boundsSizeX / currents.Count) - (boundsSizeX / 2.0f),
                        currentRangeX + (currentCurrent * boundsSizeX / currents.Count) - (boundsSizeX / 2.0f)),
                    Random.Range(0.0f, boundsSizeY / 1.5f),
                    0.0f));

            currentSettings._minVelocity = maxCurrentStrength * Mathf.InverseLerp(0.0f, boundsSizeY, averageY) -
                                           Random.Range(0.0f,
                                               maxCurrentStrength * Mathf.InverseLerp(0.0f, boundsSizeY, averageY) * 0.5f);
            currentSettings._maxVelocity = maxCurrentStrength * Mathf.InverseLerp(0.0f, boundsSizeY, averageY);
            
            
            currentCurrent++;
        }
    }
}