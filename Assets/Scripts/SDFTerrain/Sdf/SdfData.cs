using Unity.Mathematics;
using UnityEngine;

[System.Serializable]
public struct SdfData
{
    public SdfType Type;

    // Common data
    public float3 Center;
    public float Radius;

    // BoxSdf data
    public float3 Extent;

    // PlaneSdf data
    public float3 Normal;
    public float D; // Distance from origin

    // PlanetSdf / TerrainSdf data
    public float NoiseScale;
    public float HeightScale;

    // --- Constructors for convenience ---

    public static SdfData CreateSphere(float3 center, float radius)
    {
        return new SdfData
        {
            Type = SdfType.Sphere,
            Center = center,
            Radius = radius
        };
    }

    public static SdfData CreateBox(float3 center, float3 extent)
    {
        return new SdfData
        {
            Type = SdfType.Box,
            Center = center,
            Extent = extent
        };
    }

    public static float ApplyOperation(Operation op, float d1, float d2)
    {
        switch (op)
        {
            case Operation.SDF_OPERATION_UNION: return math.min(d1, d2);
            case Operation.SDF_OPERATION_SUBTRACTION: return math.max(d1, -d2);
            case Operation.SDF_OPERATION_INTERSECTION: return math.max(d1, d2);
            // TODO: Implement smooth operations
            default: return d1;
        }
    }
}