using KBCore.Refs;
using Seb.Fluid2D.Simulation;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

public class VentilManager : ValidatedMonoBehaviour
{
    [SerializeField] private FluidSim2D _fluidSimulation;
    [SerializeField] private List<Ventil> _ventilList = new List<Ventil>();
    [SerializeField] private Transform[] _ventilSpawnPoints;

    private Dictionary<Transform, Ventil> _ventilAtSpawnPoint = new Dictionary<Transform, Ventil>();
    private Dictionary<Ventil, Transform> _spawnPointForVentil = new Dictionary<Ventil, Transform>();


    private void Update()
    {
        //EvaluateParticlesReached(); // not working yet

        if (Input.GetKeyDown(KeyCode.Space))
        {
            SpawnVentilWave();
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            KillRandomVentil();
        }
    }

    private void Start()
    {
        if (_ventilSpawnPoints.Length == 0)
        {
            Debug.LogError("No ventil spawn points assigned.");
            return;
        }

        SpawnVentilWave();
    }

    private void SpawnVentilWave()
    {
        int aliveVentils = _ventilList.Count(ventil => !ventil.IsNotAlive);
        Debug.Log("Alive Ventils: " + aliveVentils);

        if (aliveVentils == _ventilList.Count)
        {
            Debug.Log("All ventil are alive, no new ventil will be spawned.");
            return;
        }

        for (int i = 0; i < _ventilList.Count - aliveVentils; i++)
        {
            Transform randomSpawnPoint = null;

            int maxIterations = 1000; // Prevent infinite loop
            int iterations = 0;

            while (iterations < maxIterations)
            {
                iterations++;
                int randomIndex = Random.Range(0, _ventilSpawnPoints.Length);
                Transform spawnPoint = _ventilSpawnPoints[randomIndex];

                if (!_ventilAtSpawnPoint.ContainsKey(spawnPoint) || _ventilAtSpawnPoint[spawnPoint].IsNotAlive)
                {
                    randomSpawnPoint = spawnPoint;

                    foreach (Ventil ventil in _ventilList.Where(ventil => ventil.IsNotAlive))
                    {
                        _ventilAtSpawnPoint[randomSpawnPoint] = ventil;
                        _spawnPointForVentil[ventil] = randomSpawnPoint;
                        ventil.transform.position = randomSpawnPoint.position;
                        ventil.transform.localEulerAngles = new Vector3(0, 0, Random.Range(0f, 360f));
                        ventil.SpawnSequence(); // Start spawn sequence
                        break; // Exit the loop after spawning one ventil
                    }

                    break; // Exit the loop once a valid spawn point is found
                }
            }
        }
    }

    private void KillRandomVentil()
    {
        int aliveVentils = _ventilList.Count(ventil => !ventil.IsNotAlive);
        if (aliveVentils <= 0)
        {
            Debug.Log("Not enough ventil to kill. At least one ventil must be alive.");
            return;
        }

        Ventil ventilToKill = null;

        int maxIterations = 100; // Prevent infinite loop
        int iterations = 0;
        while (!ventilToKill && iterations < maxIterations)
        {
            iterations++;
            int randomIndex = Random.Range(0, _ventilList.Count);

            if (_ventilList[randomIndex].IsNotAlive)
            {
                Debug.LogWarning("Selected ventil is already dead.");
                continue;
            }

            ventilToKill = _ventilList[randomIndex];
            _ventilAtSpawnPoint.Remove(_spawnPointForVentil[ventilToKill]);

            ventilToKill.Kill();
        }
    }

    private void EvaluateParticlesReached()
    {
        if (!_fluidSimulation)
        {
            return;
        }

        int4[] particlesReachedDestinationThisFrame = _fluidSimulation.particlesReachedDestinationThisFrame;
        int4 accumulatedParticlesPerVentil = new int4(0, 0, 0, 0);

        foreach (int4 particleType in particlesReachedDestinationThisFrame)
        {
            for (int i = 0; i < 4; i++)
            {
                accumulatedParticlesPerVentil[i] += particleType[i];
            }
        }

        for (int i = 0; i < 4; i++)
        {
            if (_ventilList.Count <= i)
            {
                break;
            }

            _ventilList[i].UpdateHealth(accumulatedParticlesPerVentil[i]);
        }
    }
}