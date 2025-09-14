using UnityEngine;

/// <summary>
/// An abstract base class representing a world with gravitational properties.
/// Corresponds to the JarWorld C++ class.
/// </summary>
public abstract class JarWorld : MonoBehaviour
{
    [Tooltip("The strength of gravity in this world.")]
    [field: SerializeField]
    public float GravityStrength { get; set; } = 9.8f;

    [Tooltip("The mass of objects affected by this world's physics.")]
    [field: SerializeField]
    public float Mass { get; set; } = 1.0f;

    /// <summary>
    /// Gets the gravity vector at a given position.
    /// Inputs are assumed to be in local space.
    /// </summary>
    /// <param name="position">The position to evaluate gravity at (in local space).</param>
    /// <returns>The gravity vector.</returns>
    public abstract Vector3 GetGravityVector(Vector3 position);

    /// <summary>
    /// Gets the height relative to the world's surface at a given position.
    /// Inputs are assumed to be in local space.
    /// </summary>
    /// <param name="position">The position to evaluate the height from (in local space).</param>
    /// <returns>The height from the surface.</returns>
    public abstract float GetHeight(Vector3 position);
}