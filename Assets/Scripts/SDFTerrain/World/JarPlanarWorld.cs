using UnityEngine;

/// <summary>
/// Represents a simple, flat planar world.
/// Corresponds to the JarPlanarWorld C++ class.
/// </summary>
public class JarPlanarWorld : JarWorld
{
    [Tooltip("The height of the world's surface along its normal vector.")]
    [field: SerializeField]
    public float SurfaceHeight { get; set; } = 0.0f;

    [Tooltip("The 'up' direction of the plane. Gravity acts in the opposite direction.")]
    [field: SerializeField]
    public Vector3 Normal { get; set; } = Vector3.up;

    /// <summary>
    /// Gets the gravity vector, which is constant for a planar world.
    /// It points in the opposite direction of the Normal vector.
    /// </summary>
    /// <param name="position">The position to evaluate gravity at (ignored in this implementation).</param>
    /// <returns>The constant gravity vector of the plane.</returns>
    public override Vector3 GetGravityVector(Vector3 position)
    {
        return Normal * -GravityStrength;
    }

    /// <summary>
    /// Gets the height from the plane's surface at a given position.
    /// </summary>
    /// <param name="position">The position to evaluate the height from (in local space).</param>
    /// <returns>The perpendicular distance to the plane's surface.</returns>
    public override float GetHeight(Vector3 position)
    {
        // Calculates the dot product of the position and the normal vector,
        // then subtracts the surface height to find the distance from the surface.
        return Vector3.Dot(position, Normal) - SurfaceHeight;
    }
}