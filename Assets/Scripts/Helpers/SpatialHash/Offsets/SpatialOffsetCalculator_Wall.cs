using Seb.Helpers;
using UnityEngine;

namespace Seb.Helpers.Internal
{
	public class SpatialOffsetCalculator_Wall
	{
		readonly ComputeShader cs = ComputeHelper.LoadComputeShader("SpatialOffsets_Wall");
		static readonly int NumInputs = Shader.PropertyToID("numInputs");
		static readonly int Offsets = Shader.PropertyToID("Offsets_Wall");
		static readonly int SortedKeys = Shader.PropertyToID("SortedKeys_Wall");

		const int initKernel = 0;
		const int offsetsKernel = 1;


		// needsInit: Set to true if offsets buffer has not already been initialized with values >= its length.
		public void Run(bool needsInit, ComputeBuffer sortedKeys, ComputeBuffer offsets)
		{
			if (sortedKeys.count != offsets.count) throw new System.Exception("Count mismatch");
			cs.SetInt(NumInputs, sortedKeys.count);

			if (needsInit)
			{
				cs.SetBuffer(initKernel, Offsets, offsets);
                ComputeHelper.Dispatch(cs, sortedKeys.count, kernelIndex: initKernel);
			}

			cs.SetBuffer(offsetsKernel, Offsets, offsets);
			cs.SetBuffer(offsetsKernel, SortedKeys, sortedKeys);
            ComputeHelper.Dispatch(cs, sortedKeys.count, kernelIndex: offsetsKernel);
		}
	}
}