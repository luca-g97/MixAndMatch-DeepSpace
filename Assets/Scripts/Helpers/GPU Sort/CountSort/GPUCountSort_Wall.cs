using Seb.Helpers;
using UnityEngine;

namespace Seb.GPUSorting
{
    public class GPUCountSort_Wall
    {
        static readonly int ID_InputItems = Shader.PropertyToID("InputItems_Wall");
        static readonly int ID_InputSortKeys = Shader.PropertyToID("InputKeys_Wall");
        static readonly int ID_SortedItems = Shader.PropertyToID("SortedItems_Wall");
        static readonly int ID_SortedKeys = Shader.PropertyToID("SortedKeys_Wall");
        static readonly int ID_Counts = Shader.PropertyToID("Counts_Wall");
        static readonly int ID_NumInputs = Shader.PropertyToID("numInputs");

        readonly Scan_Wall scan = new();
        readonly ComputeShader cs = ComputeHelper.LoadComputeShader("CountSort_Wall");

        ComputeBuffer sortedItemsBuffer;
        ComputeBuffer sortedValuesBuffer;
        ComputeBuffer countsBuffer;

        const int ClearCountsKernel = 0;
        const int CountKernel = 1;
        const int ScatterOutputsKernel = 2;
        const int CopyBackKernel = 3;

        // Sorts a buffer of indices based on a buffer of keys (note that the keys will also be sorted in the process).
        // Note: the maximum possible key value must be known ahead of time for this algorithm (and preferably not be too large), as memory is allocated for all possible keys.
        // Both buffers expected to be of type <uint>
        // Note: index buffer is initialized here to values 0...n before sorting

        public void Run(ComputeBuffer itemsBuffer, ComputeBuffer keysBuffer, int numToSort, uint maxValue)
        {
            // ---- Init ----
            // The total buffer size might be larger than the number of items we need to sort.
            int bufferSize = itemsBuffer.count;

            if (ComputeHelper.CreateStructuredBuffer<uint>(ref sortedItemsBuffer, bufferSize))
            {
                cs.SetBuffer(ScatterOutputsKernel, ID_SortedItems, sortedItemsBuffer);
                cs.SetBuffer(CopyBackKernel, ID_SortedItems, sortedItemsBuffer);
            }

            if (ComputeHelper.CreateStructuredBuffer<uint>(ref sortedValuesBuffer, bufferSize))
            {
                cs.SetBuffer(ScatterOutputsKernel, ID_SortedKeys, sortedValuesBuffer);
                cs.SetBuffer(CopyBackKernel, ID_SortedKeys, sortedValuesBuffer);
            }

            if (ComputeHelper.CreateStructuredBuffer<uint>(ref countsBuffer, (int)maxValue + 1))
            {
                cs.SetBuffer(ClearCountsKernel, ID_Counts, countsBuffer);
                cs.SetBuffer(CountKernel, ID_Counts, countsBuffer);
                cs.SetBuffer(ScatterOutputsKernel, ID_Counts, countsBuffer);
            }

            cs.SetBuffer(ClearCountsKernel, ID_InputItems, itemsBuffer);
            cs.SetBuffer(CountKernel, ID_InputSortKeys, keysBuffer);
            cs.SetBuffer(ScatterOutputsKernel, ID_InputItems, itemsBuffer);
            cs.SetBuffer(CopyBackKernel, ID_InputItems, itemsBuffer);

            cs.SetBuffer(ScatterOutputsKernel, ID_InputSortKeys, keysBuffer);
            cs.SetBuffer(CopyBackKernel, ID_InputSortKeys, keysBuffer);

            // --- CHANGE: Use the active number of items to sort ---
            cs.SetInt(ID_NumInputs, numToSort);

            // ---- Run ----
            // --- CHANGE: Dispatch using the active number of items to sort ---
            ComputeHelper.Dispatch(cs, numToSort, kernelIndex: ClearCountsKernel);
            ComputeHelper.Dispatch(cs, numToSort, kernelIndex: CountKernel);

            scan.Run(countsBuffer);
            ComputeHelper.Dispatch(cs, numToSort, kernelIndex: ScatterOutputsKernel);
            ComputeHelper.Dispatch(cs, numToSort, kernelIndex: CopyBackKernel);
        }

        public void Release()
        {
            ComputeHelper.Release(sortedItemsBuffer, sortedValuesBuffer, countsBuffer);
            scan.Release();
        }
    }
}