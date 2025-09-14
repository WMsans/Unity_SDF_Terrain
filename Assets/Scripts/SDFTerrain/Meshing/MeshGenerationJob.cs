using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

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
                vertices = (float3*)chunkMeshData.vertices.GetUnsafePtr(),
                vertexCount = chunkMeshData.vertices.Length,
                indices = (int*)chunkMeshData.indices.GetUnsafePtr(),
                indexCount = chunkMeshData.indices.Length,
                lod = chunkMeshData.lod,
                boundaries = chunkMeshData.boundaries,
                edge_chunk = chunkMeshData.edge_chunk,
                bounds = chunkMeshData.bounds
            });
            // By doing this, we transfer ownership of the memory from chunkMeshData
            // to the MeshGenerationResult. We must not dispose the arrays here.
        }
        else
        {
            chunkMeshData.Dispose();
        }
    }
}