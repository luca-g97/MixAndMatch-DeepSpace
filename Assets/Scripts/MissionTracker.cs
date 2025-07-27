using System;
using Seb.Fluid2D.Simulation;
using UnityEngine;

public class MissionTracker : MonoBehaviour
{
    [Header("Settings")]
    public float totalMissionRuntime;
    [SerializeField] private float missionOvertime = 10f;
    
    [Header("Stats")]
    public int mostOilFilteredColorIndex = -1;
    public int mostEfficientCollaborationColorIndex = -1;
    public int oilBarrelsFiltered;
    public int coralsDied;
    public int sealsDied;
    
    [Header("References")]
    [SerializeField] private FluidSim2D fluidSimulation;
    [SerializeField] private VentilManager coralVentilManager;
    [SerializeField] private VentilManager sealVentilManager;

    private float missionRuntime;
    private OilBarrel[] _oilBarrels;
    private bool missionIsOver;
    private bool missionIsGraded;

    public event Action<int> OnMissionGraded;
    
    private void Update()
    {
        if (fluidSimulation.lastPlayerCount > 0)
        {
            missionRuntime += Time.deltaTime;
        }
        
        if (!missionIsOver && missionRuntime >= totalMissionRuntime)
        {
            missionIsOver = true;
            EndRound();
        }

        if (!missionIsGraded && missionRuntime >= totalMissionRuntime + missionOvertime)
        {
            missionIsGraded = true;
            RecordStats();
            GradeRound();
        }
    }

    private void EndRound()
    {
        if (_oilBarrels == null || _oilBarrels.Length == 0)
        {
            _oilBarrels = FindObjectsByType<OilBarrel>(FindObjectsSortMode.None);
        }
        foreach (OilBarrel oilBarrel in _oilBarrels)
        {
            oilBarrel.allowSpawning = false;
            oilBarrel.StopSpawning();
        }
    }

    private void GradeRound()
    {
        MissionScoring scoring = new MissionScoring();

        int mixedColorParticlesRemoved = 0;
        
        for (int i = 3; i < 6; i++)
        {
            mixedColorParticlesRemoved += fluidSimulation.removedParticlesPerColor[i][0];
        }
        
        int score = scoring.CalculateScore(coralsDied,
            sealsDied, mixedColorParticlesRemoved);
        
        int stars = scoring.GetStarRating(score);

        OnMissionGraded?.Invoke(stars);
    }

    private void RecordStats()
    {
        coralsDied = coralVentilManager.GetVentilsDiedCount();
        sealsDied = sealVentilManager.GetVentilsDiedCount();

        for (int i = 0; i < 12; i++)
        {
            oilBarrelsFiltered += fluidSimulation.removedParticlesPerColor[i][0] / 100;
        }
        
        int mostParticlesRemovedOfMixedColors = 0;
        
        for (int i = 3; i < 6; i++)
        {
            if (fluidSimulation.removedParticlesPerColor[i][0] > mostParticlesRemovedOfMixedColors)
            {
                mostParticlesRemovedOfMixedColors = fluidSimulation.removedParticlesPerColor[i][0];
                mostEfficientCollaborationColorIndex = i;
            }
        }
        
        int mosetParticlesRemovedOfAllColors = 0;
        
        for (int i = 0; i < 12; i++)
        {
            if (fluidSimulation.removedParticlesPerColor[i][0] > mosetParticlesRemovedOfAllColors)
            {
                mosetParticlesRemovedOfAllColors = fluidSimulation.removedParticlesPerColor[i][0];
                mostOilFilteredColorIndex = i;
            }
        }
    }
}