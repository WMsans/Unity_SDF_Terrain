using UnityEngine;
using Unity.Mathematics;

public class VoxelTerrain : MonoBehaviour
{
    [Header("Terrain Settings")]
    [Tooltip("The root signed distance field defining the initial terrain shape.")]
    public ISignedDistanceField Sdf;

    [Tooltip("The overall scale of the octree.")]
    public float OctreeScale = 1.0f;

    [Tooltip("The maximum depth of the octree. World size is 2^(Size) * OctreeScale.")]
    [Range(1, 16)]
    public int Size = 14;

    private VoxelOctreeNode _voxelRoot;

    void Start()
    {
        Initialize();
    }

    public void Initialize()
    {
        Sdf = new PlaneSdf(new float3(0, 1, 0), 0);
        
        _voxelRoot = new VoxelOctreeNode(Size);
        Build();
    }
    
    /// <summary>
    /// Rebuilds the terrain based on the current SDF and octree state.
    /// </summary>
    public void Build()
    {
        if (_voxelRoot == null || Sdf == null) return;
        _voxelRoot.Build(this);
        // --- Meshing would be triggered here ---
    }
    
    /// <summary>
    /// Modifies the terrain using a sphere shape.
    /// </summary>
    /// <param name="position">The center of the sphere modification.</param>
    /// <param name="radius">The radius of the sphere.</param>
    /// <param name="isUnion">True to add terrain (union), false to remove (subtraction).</param>
    public void SphereEdit(Vector3 position, float radius, bool isUnion)
    {
        if (_voxelRoot == null) return;
        
        var operation = isUnion ? SdfOperation.Union : SdfOperation.Subtraction;
        var sdf = new SphereSdf(float3.zero, radius);
        float boundsPadding = OctreeScale * 2.0f;
        var bounds = new Bounds(position, Vector3.one * (radius + boundsPadding) * 2f);
        
        var settings = new ModifySettings
        {
            Sdf = sdf,
            Operation = operation,
            Position = position,
            Bounds = bounds
        };
        
        _voxelRoot.ModifySdfInBounds(this, settings);
        // --- Meshing update for affected chunks would be triggered here ---
    }
}