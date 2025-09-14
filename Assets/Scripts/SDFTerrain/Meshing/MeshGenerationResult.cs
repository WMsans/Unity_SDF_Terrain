using System;
using Unity.Burst;

[BurstCompile]
public unsafe struct MeshGenerationResult : IDisposable
{
    public VoxelOctreeNode* chunk;
    public ChunkMeshData meshData;

    public void Dispose()
    {
        meshData.Dispose();
    }
}