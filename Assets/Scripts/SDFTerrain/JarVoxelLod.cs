using Unity.Burst;
using Unity.Mathematics;
using static Unity.Mathematics.math;

/// <summary>
/// Manages Level of Detail (LOD) calculations for a voxel octree based on camera position.
/// This is a C# struct translation compatible with Unity's Burst Compiler.
/// </summary>
[BurstCompile]
public struct JarVoxelLod
{
    // Note: In C#, public fields in structs are preferred over properties for Burst performance.
    public bool AutomaticUpdate;
    public float AutomaticUpdateDistance;
    public int LodLevelCount;
    public int ShellSize;
    public float OctreeScale;
    public float AutoMeshCoolDown;
    public float3 CameraPosition;

    /// <summary>
    /// Initializes a new instance of the JarVoxelLod struct.
    /// </summary>
    public JarVoxelLod(bool automaticUpdate, float automaticUpdateDistance, int lodLevelCount, int shellSize, float octreeScale)
    {
        AutomaticUpdate = automaticUpdate;
        AutomaticUpdateDistance = automaticUpdateDistance;
        LodLevelCount = lodLevelCount;
        ShellSize = shellSize;
        OctreeScale = octreeScale;
        AutoMeshCoolDown = 0.0f;
        CameraPosition = new float3(float.MinValue); // Initialize to a value that guarantees the first update
    }

    /// <summary>
    /// Processes the LOD logic for a frame.
    /// </summary>
    /// <param name="terrainPosition">The current position of the terrain transform.</param>
    /// <param name="playerPosition">The current position of the player/camera.</param>
    /// <param name="delta">The time since the last frame.</param>
    /// <returns>True if an LOD update is required, false otherwise.</returns>
    public bool Process(float3 playerPosition, float delta)
    {
        AutoMeshCoolDown -= delta;
        if (!AutomaticUpdate || AutoMeshCoolDown > 0)
        {
            return false;
        }

        return UpdateCameraPosition(playerPosition, false);
    }

    /// <summary>
    /// Updates the camera position for LOD calculations and determines if a rebuild is needed.
    /// </summary>
    /// <param name="newPosition">The new position of the camera/player.</param>
    /// <param name="force">If true, forces an update regardless of distance.</param>
    /// <returns>True if the terrain should be rebuilt, false otherwise.</returns>
    public bool UpdateCameraPosition(float3 newPosition, bool force)
    {
        if (!force && distancesq(newPosition, CameraPosition) < AutomaticUpdateDistance * AutomaticUpdateDistance)
        {
            return false;
        }

        CameraPosition = newPosition;
        AutoMeshCoolDown = 0.2f; // Cooldown to prevent rapid updates
        return true;
    }

    /// <summary>
    /// Determines the desired LOD for a given octree node.
    /// </summary>
    /// <param name="node">The VoxelOctreeNode to evaluate.</param>
    /// <returns>The desired LOD level.</returns>
    public int DesiredLod(VoxelOctreeNode node)
    {
        // If the node's size is greater than the max chunk size, it's a high-level node that should be subdivided.
        // We return LOD 0 for these high-level nodes to encourage subdivision.
        var l = LodAt(node._center);
        return l;
    }

    /// <summary>
    /// Gets the camera position used for LOD calculations.
    /// </summary>
    public float3 GetCameraPosition()
    {
        return CameraPosition;
    }

    /// <summary>
    /// Calculates the grid size for a given LOD level.
    /// </summary>
    /// <param name="lod">The level of detail.</param>
    /// <returns>The corresponding grid size.</returns>
    public float LodToGridSize(int lod)
    {
        // Use 1L (long) to perform a 64-bit shift, matching the C++ behavior
        // and preventing overflow if lod is large.
        return (1L << (lod + 1)) * OctreeScale;
    }

    /// <summary>
    /// Snaps a position to the nearest point on a grid of a given size.
    /// </summary>
    /// <param name="pos">The position to snap.</param>
    /// <param name="gridSize">The size of the grid cells.</param>
    /// <returns>The snapped position.</returns>
    public float3 SnapToGrid(float3 pos, float gridSize)
    {
        return floor(pos / gridSize) * gridSize;
    }

    /// <summary>
    /// Checks if a position is within the shell of a specific LOD level around the camera.
    /// </summary>
    /// <param name="lod">The LOD level to check.</param>
    /// <param name="pos">The world position.</param>
    /// <param name="camPos">The camera's position.</param>
    /// <returns>True if the position is within the LOD shell, false otherwise.</returns>
    public bool IsInLodShell(int lod, float3 pos, float3 camPos)
    {
        float gridSize = LodToGridSize(lod) * 2.0f;
        float3 lodCamPos = SnapToGrid(camPos, gridSize);
        float3 delta = abs(pos - lodCamPos);
        // cmax gets the maximum component of a vector (e.g., max(delta.x, delta.y, delta.z))
        float dist = cmax(delta);
        return dist < (gridSize * ShellSize);
    }

    /// <summary>
    /// Determines the optimal LOD level for a given world position.
    /// </summary>
    /// <param name="position">The world position to evaluate.</param>
    /// <returns>The calculated LOD index.</returns>
    public int LodAt(float3 position)
    {
        const float RChunksize = 1.0f / 16.0f;
        float3 pos = position * RChunksize;
        float3 camPos = CameraPosition * RChunksize;

        // This is a direct translation of the logarithmic approximation from the C++ code.
        float3 delta = abs(pos - camPos) / (2.0f * ShellSize);
        int lod = (int)max(0, floor(log2(max(1.0f, cmax(delta)))));

        // The approximation can be off by 1, so we check neighbors to correct it.
        if (!IsInLodShell(lod, pos, camPos))
        {
            return lod + 1;
        }

        if (lod <= 0 || !IsInLodShell(lod - 1, pos, camPos))
        {
            return lod;
        }

        return lod - 1;
    }
}