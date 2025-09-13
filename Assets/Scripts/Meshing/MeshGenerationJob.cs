using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct MeshGenerationJob : IJob
{
    public JarVoxelTerrain terrain;
    public VoxelOctreeNode chunk;
    public NativeQueue<ChunkMeshData>.ParallelWriter ChunksToProcess;

    public void Execute()
    {
        var meshCompute = new StitchedSurfaceNets(terrain, chunk);
        ChunkMeshData? chunkMeshData = meshCompute.generate_mesh_data(terrain);
        if (chunkMeshData.HasValue) {
            ChunksToProcess.Enqueue(chunkMeshData.Value);
        }
    }
}
