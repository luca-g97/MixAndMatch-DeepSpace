using Seb.GPUSorting;
using Seb.Helpers.Internal;
using UnityEngine;

namespace Seb.Helpers
{
    public class SpatialHash
    {
        public ComputeBuffer SpatialKeys;
        public ComputeBuffer SpatialIndices;
        public ComputeBuffer SpatialOffsets;

        readonly GPUCountSort gpuSort = new();
        readonly SpatialOffsetCalculator spatialOffsetsCalc = new();

        public SpatialHash(int size)
        {
            CreateBuffers(size);
        }

        public void Resize(int newSize)
        {
            CreateBuffers(newSize);
        }

        // Before running: the SpatialKeys buffer should be populated with values.
        // After running: SpatialKeys will be sorted (in ascending order), and the Offsets buffer will
        // contain values for looking up the start index of each group of keys from the key value.
        // The Index buffer will contain the mapping of original unsorted keys to the sorted version.
        // ---- Example ----
        // Given SpatialKeys input of:          {6, 9, 2, 2, 6, 3, 9, 9, 3, 2}
        // This will be sorted to result in:    {2, 2, 2, 3, 6, 6, 9, 9, 9, 9}
        // Offsets will be: (x = irrelevant)    {x, x, 0, 3, x, x, 4, x, x, 6} 
        // So to look up where the '6' keys start for instance, Offsets[6] gives the answer (4).
        // Finally, the Index buffer will contain the mapping of original unsorted keys to the sorted version.
        // So, to sort any buffer in corresponding fashion, one can do: Sorted[i] = Unsorted[Indices[i]]
        public void Run(int numActiveParticles)
        {
            // If there are no active particles, there's nothing to do.
            if (numActiveParticles == 0) return;

            // The maximum key value is numActiveParticles - 1.
            uint maxValue = (uint)(numActiveParticles - 1);

            // Pass the active count to the sorter (from previous answer, still correct)
            gpuSort.Run(SpatialIndices, SpatialKeys, numActiveParticles, maxValue);

            // --- CHANGE: Pass the active count to the offset calculator as well ---
            // The 'true' parameter indicates that the offsets buffer should be cleared before use.
            spatialOffsetsCalc.Run(true, SpatialKeys, SpatialOffsets, numActiveParticles);
        }

        public void Release()
        {
            gpuSort.Release();
            ComputeHelper.Release(SpatialKeys, SpatialIndices, SpatialOffsets);
        }

        void CreateBuffers(int count)
        {
            ComputeHelper.CreateStructuredBuffer<uint>(ref SpatialKeys, count);
            ComputeHelper.CreateStructuredBuffer<uint>(ref SpatialIndices, count);
            ComputeHelper.CreateStructuredBuffer<uint>(ref SpatialOffsets, count);
        }
    }
}