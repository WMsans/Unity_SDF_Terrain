using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct SphereSdf : ISignedDistanceField
{
    public float3 Center;
    public float Radius;

    public SphereSdf(float3 center, float radius)
    {
        Center = center;
        Radius = radius;
    }

    public float Distance(float3 pos)
    {
        return math.length(pos - Center) - Radius;
    }
    public float3 Normal(float3 pos)
    {
        return math.normalize(pos - Center);
    }
}