using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct SignedDistanceField : ISignedDistanceField
{
    public float Distance(float3 pos)
    {
        // Default implementation, should be overridden by specific SDF types
        return float.MaxValue;
    }

    public float3 Normal(float3 pos)
    {
        const float delta = 0.001f;
        var xyy = new float3(delta, -delta, -delta);
        var yyx = new float3(-delta, -delta, delta);
        var yxy = new float3(-delta, delta, -delta);
        var xxx = new float3(delta, delta, delta);

        return math.normalize(
            xyy * Distance(pos + xyy) +
            yyx * Distance(pos + yyx) +
            yxy * Distance(pos + yxy) +
            xxx * Distance(pos + xxx)
        );
    }
}