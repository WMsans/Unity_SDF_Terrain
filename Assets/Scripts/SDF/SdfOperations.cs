using Unity.Burst;
using Unity.Mathematics;

public enum SdfOperation
{
    Union,
    Subtraction,
    Intersection,
    SmoothUnion,
    SmoothSubtraction,
    SmoothIntersection
}

[BurstCompile]
public static class SdfOperations
{
    /// <summary>
    /// Applies a boolean operation to combine two SDF distance values.
    /// </summary>
    /// <param name="op">The operation to perform.</param>
    /// <param name="a">The distance value of the first SDF.</param>
    /// <param name="b">The distance value of the second SDF.</param>
    /// <param name="k">The smoothing factor for smooth operations.</param>
    /// <returns>The resulting combined distance.</returns>
    public static float ApplyOperation(SdfOperation op, float a, float b, float k = 1.0f)
    {
        switch (op)
        {
            case SdfOperation.Union:
                return math.min(a, b);

            case SdfOperation.Subtraction:
                return math.max(a, -b);

            case SdfOperation.Intersection:
                return math.max(a, b);

            case SdfOperation.SmoothUnion:
            {
                float h = math.clamp(0.5f + 0.5f * (b - a) / k, 0.0f, 1.0f);
                return math.lerp(b, a, h) - k * h * (1.0f - h);
            }

            case SdfOperation.SmoothSubtraction:
            {
                float h = math.clamp(0.5f - 0.5f * (b + a) / k, 0.0f, 1.0f);
                return math.lerp(a, -b, h) + k * h * (1.0f - h);
            }

            case SdfOperation.SmoothIntersection:
            {
                float h = math.clamp(0.5f - 0.5f * (b - a) / k, 0.0f, 1.0f);
                return math.lerp(b, a, h) + k * h * (1.0f - h);
            }

            default:
                return a;
        }
    }
}