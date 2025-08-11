using System;
using System.Threading.Tasks;
using DG.Tweening;
using Seb.Fluid2D.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MissionTracker : MonoBehaviour
{
    [HideInInspector] public float totalMissionRuntime = 60f;
    [HideInInspector] public float missionOvertime = 10f;
    [HideInInspector] public float missionRestartDelayAfterGrade = 30f;

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

    public float missionRuntimeLeft;
    public float missionRestartTimeLeft;
    private OilBarrel[] _oilBarrels;
    public bool missionIsOver;
    public bool missionIsGraded;

    public event Action<int> OnMissionGraded;
    public event Action OnMissionOver;

    public event Action<int> OnSecondPassed;
    public event Action<int> OnMinutePassed;

    private int previousSecond = -1;
    private int previousMinute = -1;

    private void Start()
    {
        MissionSettingsData settings = MissionSettingsManager.Instance.Settings;

        totalMissionRuntime = settings.totalMissionRuntime;
        missionOvertime = settings.missionOvertime;
        missionRestartDelayAfterGrade = settings.missionRestartDelayAfterGrade;
        
        missionRuntimeLeft = totalMissionRuntime;
        missionRestartTimeLeft = missionRestartDelayAfterGrade;
    }

    private void OnDestroy()
    {
        DOTween.KillAll();
    }

    private void Update()
    {
        if (fluidSimulation.lastPlayerCount > 0)
        {
            missionRuntimeLeft -= Time.deltaTime;

            int currentSecond = Mathf.FloorToInt(missionRuntimeLeft % 60);
            if (currentSecond != previousSecond)
            {
                OnSecondPassed?.Invoke(currentSecond);
                previousSecond = currentSecond;
            }

            int currentMinute = Mathf.FloorToInt(missionRuntimeLeft / 60);
            if (currentMinute != previousMinute)
            {
                OnMinutePassed?.Invoke(currentMinute);
                previousMinute = currentMinute;
            }
        }

        if (!missionIsOver && missionRuntimeLeft <= 0)
        {
            missionIsOver = true;
            EndRound();
        }

        if (!missionIsGraded && missionRuntimeLeft <= -missionOvertime)
        {
            missionIsGraded = true;
            RecordStats();
            GradeRound();
        }

        if (missionIsGraded)
        {
            if (missionRestartTimeLeft >= 0)
            {
                missionRestartTimeLeft -= Time.deltaTime;
            }

            else
            {
                RestartMission();
            }
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

        OnMissionOver?.Invoke();
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

    private void RestartMission()
    {
        SceneManager.LoadScene(0);
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