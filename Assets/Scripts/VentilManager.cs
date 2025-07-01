using System;
using System.Collections.Generic;
using KBCore.Refs;
using NUnit.Framework;
using Seb.Fluid2D.Simulation;
using Unity.Mathematics;
using UnityEngine;

public class VentilManager : ValidatedMonoBehaviour
{
    [SerializeField] private FluidSim2D _fluidSimulation;
    [SerializeField, Child] private List<Ventil> _ventilList = new List<Ventil>();
    

    private void Update()
    {
        EvaluateParticlesReached();
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
