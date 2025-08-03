using UnityEngine;

namespace Seb.Helpers.Internal
{
    public class SpatialOffsetCalculator
    {
        readonly ComputeShader cs = ComputeHelper.LoadComputeShader("SpatialOffsets");
        static readonly int NumInputs = Shader.PropertyToID("numInputs");
        static readonly int Offsets = Shader.PropertyToID("Offsets");
        static readonly int SortedKeys = Shader.PropertyToID("SortedKeys");

        const int initKernel = 0;
        const int offsetsKernel = 1;


        // needsInit: Set to true if offsets buffer has not already been initialized with values >= its length.
        public void Run(bool needsInit, ComputeBuffer sortedKeys, ComputeBuffer offsets, int numActive)
        {
            if (sortedKeys.count != offsets.count) throw new System.Exception("Count mismatch");

            // --- CHANGE: Use the active particle count, not the full buffer size ---
            cs.SetInt(NumInputs, numActive);

            if (needsInit)
            {
                cs.SetBuffer(initKernel, Offsets, offsets);
                // Dispatch using the buffer's full length to ensure it's all cleared once
                ComputeHelper.Dispatch(cs, offsets.count, kernelIndex: initKernel);
            }

            cs.SetBuffer(offsetsKernel, Offsets, offsets);
            cs.SetBuffer(offsetsKernel, SortedKeys, sortedKeys);

            // --- CHANGE: Dispatch using the active particle count ---
            ComputeHelper.Dispatch(cs, numActive, kernelIndex: offsetsKernel);
        }
    }
}