
using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct BurstBounds
{
    public float3 center;
    public float3 size;

    public BurstBounds(float3 center, float3 size)
    {
        this.center = center;
        this.size = size;
    }

    public BurstBounds expanded(float amount)
    {
        var half_amount = amount / 2;
        return new BurstBounds(center, size + new float3(half_amount, half_amount, half_amount));
    }
}
