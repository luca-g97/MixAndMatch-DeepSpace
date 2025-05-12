const float Poly6ScalingFactor_Wall;
const float SpikyPow3ScalingFactor_Wall;
const float SpikyPow2ScalingFactor_Wall;
const float SpikyPow3DerivativeScalingFactor_Wall;
const float SpikyPow2DerivativeScalingFactor_Wall;

float SmoothingKernelPoly6_Wall(float dst, float radius)
{
	if (dst < radius)
	{
		float v = radius * radius - dst * dst;
        return v * v * v * Poly6ScalingFactor_Wall;
    }
	return 0;
}

float SpikyKernelPow3_Wall(float dst, float radius)
{
	if (dst < radius)
	{
		float v = radius - dst;
        return v * v * v * SpikyPow3ScalingFactor_Wall;
    }
	return 0;
}

float SpikyKernelPow2_Wall(float dst, float radius)
{
	if (dst < radius)
	{
		float v = radius - dst;
        return v * v * SpikyPow2ScalingFactor_Wall;
    }
	return 0;
}

float DerivativeSpikyPow3_Wall(float dst, float radius)
{
	if (dst <= radius)
	{
		float v = radius - dst;
        return -v * v * SpikyPow3DerivativeScalingFactor_Wall;
    }
	return 0;
}

float DerivativeSpikyPow2_Wall(float dst, float radius)
{
	if (dst <= radius)
	{
		float v = radius - dst;
        return -v * SpikyPow2DerivativeScalingFactor_Wall;
    }
	return 0;
}
