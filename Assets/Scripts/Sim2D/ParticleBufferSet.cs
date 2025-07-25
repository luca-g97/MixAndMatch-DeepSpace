using Seb.Helpers;
using Unity.Mathematics;
using UnityEngine;

// Helper class to hold a set of particle buffers
public class ParticleBufferSet
{
    public ComputeBuffer PositionBuffer;
    public ComputeBuffer PredictedPositionBuffer;
    public ComputeBuffer VelocityBuffer;
    public ComputeBuffer DensityBuffer;
    public ComputeBuffer GravityScaleBuffer;
    public ComputeBuffer ParticleTypeBuffer;
    // Add any other per-particle buffers here

    public ParticleBufferSet(int capacity)
    {
        int safeCapacity = Mathf.Max(1, capacity);
        PositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
        PredictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
        VelocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
        DensityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(safeCapacity);
        GravityScaleBuffer = ComputeHelper.CreateStructuredBuffer<float>(safeCapacity);
        ParticleTypeBuffer = ComputeHelper.CreateStructuredBuffer<int2>(safeCapacity);
    }

    public void Release()
    {
        ComputeHelper.Release(
            PositionBuffer, PredictedPositionBuffer, VelocityBuffer,
            DensityBuffer, GravityScaleBuffer, ParticleTypeBuffer
        );
    }
}
