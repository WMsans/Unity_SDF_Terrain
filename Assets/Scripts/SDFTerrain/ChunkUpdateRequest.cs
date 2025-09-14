using Unity.Burst;

/// <summary>
/// A request to update a chunk on the main thread.
/// This is created by a job and processed by JarVoxelTerrain.
/// </summary>
[BurstCompile]
public unsafe struct ChunkUpdateRequest
{
    public VoxelOctreeNodePointer chunk;
    public ChunkMeshData meshData;
}

/// <summary>
/// A request to delete a chunk on the main thread.
/// </summary>
[BurstCompile]
public unsafe struct ChunkDeleteRequest
{
    public VoxelOctreeNodePointer chunk;
}