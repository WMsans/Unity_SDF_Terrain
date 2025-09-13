using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct JarTerrainSdf 
{
    public float heightScale;

    private float SampleHeight(float2 pos)
    {
        // Placeholder for noise function
        float noise = 0; // Replace with actual noise sampling
        return heightScale * (noise > 0 ? 2.0f * noise : 1.0f * noise);
    }

    private float2 SampleGradient(float2 pos, float height)
    {
        const float Epsilon = 0.01f;
        const float InvEps = 1.0f / Epsilon;

        float heightX = SampleHeight(pos + new float2(Epsilon, 0));
        float heightZ = SampleHeight(pos + new float2(0, Epsilon));

        float gradientX = (heightX - height) * InvEps;
        float gradientZ = (heightZ - height) * InvEps;

        return new float2(gradientX, gradientZ);
    }

    public float Distance(float3 pos)
    {
        float2 samplePos = new float2(pos.x, pos.z);
        float height = SampleHeight(samplePos);
        float2 gradient = SampleGradient(samplePos, height);
        
        return (pos.y - height) / math.sqrt(1 + gradient.x * gradient.x + gradient.y * gradient.y);
    }
}