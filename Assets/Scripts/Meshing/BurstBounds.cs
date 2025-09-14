using Unity.Burst;
using Unity.Mathematics;
using static Unity.Mathematics.math;

[BurstCompile]
public struct BurstBounds
{
    public float3 center;
    public float3 size;

    // Helper properties to get the corners of the bounds
    // FIX: Add the 'readonly' keyword to the properties.
    public readonly float3 min => center - size / 2f;
    public readonly float3 max => center + size / 2f;

    public BurstBounds(float3 center, float3 size)
    {
        this.center = center;
        this.size = size;
    }

    public BurstBounds Expanded(float amount)
    {
        var half_amount = amount / 2;
        return new BurstBounds(center, size + new float3(half_amount, half_amount, half_amount));
    }

    /// <summary>
    /// Checks if this bounding box intersects with another.
    /// </summary>
    public readonly bool Intersects(BurstBounds other)
    {
        if (this.max.x < other.min.x || this.min.x > other.max.x) return false;
        if (this.max.y < other.min.y || this.min.y > other.max.y) return false;
        if (this.max.z < other.min.z || this.min.z > other.max.z) return false;
        return true;
    }

    /// <summary>
    /// Checks if a point is contained within this bounding box (inclusive).
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <returns>True if the point is inside the bounds, false otherwise.</returns>
    public readonly bool Contains(float3 point)
    {
        // This check is inclusive, so points on the boundary are considered inside.
        // We use math.all to check if the condition is true for all components (x, y, and z).
        return all(point >= min) && all(point <= max);
    }
}