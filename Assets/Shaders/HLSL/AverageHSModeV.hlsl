#define PI 3.14159265359

float3 RGBToHSV(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = c.g < c.b ? float4(c.bg, K.wz) : float4(c.gb, K.xy);
    float4 q = c.r < p.x ? float4(p.xyw, c.r) : float4(c.r, p.yzx);

    float d = q.x - min(q.w, q.y);
    float e = 1e-10;

    float h = abs(q.z + (q.w - q.y) / (6.0 * d + e));
    float s = d / (q.x + e);
    float v = q.x;

    return float3(h, s, v);
}

void AverageHSV_float(
    float3 rgb1, float3 rgb2, float3 rgb3, float3 rgb4, float3 rgb5,
    float valueMode, // <0.5 = min, >=0.5 and <1 = avg, >=1 = max
    out float3 avgHSV)
{
    float3 hsv1 = RGBToHSV(rgb1);
    float3 hsv2 = RGBToHSV(rgb2);
    float3 hsv3 = RGBToHSV(rgb3);
    float3 hsv4 = RGBToHSV(rgb4);
    float3 hsv5 = RGBToHSV(rgb5);

    // Convert hue to angle in radians
    float a1 = hsv1.x * 2.0 * PI;
    float a2 = hsv2.x * 2.0 * PI;
    float a3 = hsv3.x * 2.0 * PI;
    float a4 = hsv4.x * 2.0 * PI;
    float a5 = hsv5.x * 2.0 * PI;

    // Convert to unit vectors
    float2 vH1 = float2(cos(a1), sin(a1));
    float2 vH2 = float2(cos(a2), sin(a2));
    float2 vH3 = float2(cos(a3), sin(a3));
    float2 vH4 = float2(cos(a4), sin(a4));
    float2 vH5 = float2(cos(a5), sin(a5));

    // Average hue direction
    float2 avgVec = normalize(vH1 + vH2 + vH3 + vH4 + vH5);
    float avgAngle = atan2(avgVec.y, avgVec.x);
    if (avgAngle < 0.0) avgAngle += 2.0 * PI;
    float avgHue = avgAngle / (2.0 * PI);

    float avgSat = (hsv1.y + hsv2.y + hsv3.y + hsv4.y + hsv5.y) / 5.0;

    float v1 = hsv1.z;
    float v2 = hsv2.z;
    float v3 = hsv3.z;
    float v4 = hsv4.z;
    float v5 = hsv5.z;

    float minVal = min(v1, min(v2, min(v3, min(v4, v5))));
    float avgVal = (v1 + v2 + v3 + v4 + v5) / 5.0;
    float maxVal = max(v1, max(v2, max(v3, max(v4, v5))));

    // Compute selection masks
    float isAvg = step(0.5, valueMode) * (1.0 - step(1.0, valueMode)); // [0.5, 1)
    float isMax = step(1.0, valueMode);                                 // >=1
    float isMin = 1.0 - isAvg - isMax;                                  // <0.5

    float val = isMin * minVal + isAvg * avgVal + isMax * maxVal;

    avgHSV = float3(avgHue, avgSat, val);
}
