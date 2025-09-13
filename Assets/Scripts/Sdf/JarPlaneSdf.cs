using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct JarPlaneSdf 
{
    public float3 normal;
    public float d;

    public float Distance(float3 pos)
    {
        return math.dot(normal, pos) + d;
    }
}