using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Seb.Helpers;

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
		readonly ComputeShader cs = ComputeHelper_Wall.LoadComputeShader("CountSort_Wall");

		ComputeBuffer sortedItemsBuffer_Wall;
		ComputeBuffer sortedValuesBuffer_Wall;
		ComputeBuffer countsBuffer_Wall;

		const int ClearCountsKernel = 0;
		const int CountKernel = 1;
		const int ScatterOutputsKernel = 2;
		const int CopyBackKernel = 3;

		// Sorts a buffer of indices based on a buffer of keys (note that the keys will also be sorted in the process).
		// Note: the maximum possible key value must be known ahead of time for this algorithm (and preferably not be too large), as memory is allocated for all possible keys.
		// Both buffers expected to be of type <uint>
		// Note: index buffer is initialized here to values 0...n before sorting

		public void Run(ComputeBuffer itemsBuffer, ComputeBuffer keysBuffer, uint maxValue)
		{
			// ---- Init ----
			int count = itemsBuffer.count;
			if (ComputeHelper_Wall.CreateStructuredBuffer<uint>(ref sortedItemsBuffer_Wall, count))
			{
				cs.SetBuffer(ScatterOutputsKernel, ID_SortedItems, sortedItemsBuffer_Wall);
				cs.SetBuffer(CopyBackKernel, ID_SortedItems, sortedItemsBuffer_Wall);
			}

			if (ComputeHelper_Wall.CreateStructuredBuffer<uint>(ref sortedValuesBuffer_Wall, count))
			{
				cs.SetBuffer(ScatterOutputsKernel, ID_SortedKeys, sortedValuesBuffer_Wall);
				cs.SetBuffer(CopyBackKernel, ID_SortedKeys, sortedValuesBuffer_Wall);
			}

			if (ComputeHelper_Wall.CreateStructuredBuffer<uint>(ref countsBuffer_Wall, (int)maxValue + 1))
			{
				cs.SetBuffer(ClearCountsKernel, ID_Counts, countsBuffer_Wall);
				cs.SetBuffer(CountKernel, ID_Counts, countsBuffer_Wall);
				cs.SetBuffer(ScatterOutputsKernel, ID_Counts, countsBuffer_Wall);
			}

			cs.SetBuffer(ClearCountsKernel, ID_InputItems, itemsBuffer);
			cs.SetBuffer(CountKernel, ID_InputSortKeys, keysBuffer);
			cs.SetBuffer(ScatterOutputsKernel, ID_InputItems, itemsBuffer);
			cs.SetBuffer(CopyBackKernel, ID_InputItems, itemsBuffer);

			cs.SetBuffer(ScatterOutputsKernel, ID_InputSortKeys, keysBuffer);
			cs.SetBuffer(CopyBackKernel, ID_InputSortKeys, keysBuffer);

			cs.SetInt(ID_NumInputs, count);

            // ---- Run ----
            ComputeHelper_Wall.Dispatch(cs, count, kernelIndex: ClearCountsKernel);
            ComputeHelper_Wall.Dispatch(cs, count, kernelIndex: CountKernel);

			scan.Run(countsBuffer_Wall);
            ComputeHelper_Wall.Dispatch(cs, count, kernelIndex: ScatterOutputsKernel);
            ComputeHelper_Wall.Dispatch(cs, count, kernelIndex: CopyBackKernel);
		}

		public void Release()
		{
            ComputeHelper_Wall.Release(sortedItemsBuffer_Wall, sortedValuesBuffer_Wall, countsBuffer_Wall);
			scan.Release();
		}
	}
}