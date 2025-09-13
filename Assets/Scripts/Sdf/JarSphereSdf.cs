using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct JarSphereSdf 
{
    public float3 center;
    public float radius;

    public float Distance(float3 pos)
    {
        return math.length(pos - center) - radius;
    }
}