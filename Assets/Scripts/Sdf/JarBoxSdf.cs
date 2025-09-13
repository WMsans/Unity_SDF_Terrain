using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct JarBoxSdf 
{
    public float3 center;
    public float3 extent;

    public float Distance(float3 pos)
    {
        float3 q = math.abs(pos - center) - extent;
        return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f);
    }
}