using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

[BurstCompile]
public unsafe struct MeshGenerationJob : IJob
{
    [ReadOnly] public TerrainData terrain;
    [NativeDisableUnsafePtrRestriction] public VoxelOctreeNode* chunk;
    public NativeQueue<MeshGenerationResult>.ParallelWriter ChunksToProcess;

    public void Execute()
    {
        var meshCompute = new StitchedSurfaceNets(terrain, chunk);
        var chunkMeshData = meshCompute.GenerateMeshData();
        
        if (chunkMeshData.vertices.IsCreated)
        {
            ChunksToProcess.Enqueue(new MeshGenerationResult
            {
                chunk = chunk,
                meshData = chunkMeshData
            });
        }
    }
}