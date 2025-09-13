using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct JarPlanetSdf 
{
    public float3 center;
    public float radius;
    public float noiseScale;

    // Note: FastNoiseLite is not included as it's a reference type.
    // You would typically pass the noise data in another way,
    // such as through a NativeArray or by baking it into a texture.

    private float GetSphericalDisplacement(float3 pos)
    {
        // This is a placeholder for the noise function.
        // In a real implementation, you would sample your noise data here.
        // For example, using procedural noise functions compatible with Burst.
        float height = 0; // Replace with actual noise sampling

        if (height < 0.0f)
        {
            return 0.5f * height * noiseScale;
        }
        return height * noiseScale;
    }

    public float Distance(float3 pos)
    {
        float3 to_center = pos - center;
        float base_distance = math.length(to_center) - radius;

        if (base_distance > noiseScale * 1.25f)
        {
            return base_distance;
        }

        float displacement = GetSphericalDisplacement(pos);
        return base_distance - displacement;
    }
}