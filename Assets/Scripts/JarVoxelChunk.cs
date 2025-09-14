using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

[BurstCompile]
public unsafe struct JarVoxelChunk
{
    // ... (Fields are unchanged) ...
    public void* MeshData;
    public void* CollisionShapeData;
    public void* StaticBodyData;
    public NativeList<long> MultiMeshInstances;
    public ChunkMeshData* ChunkMeshData;
    public int Lod;
    public int ColliderLodThreshold;
    public byte Boundaries;
    public bool IsEdgeChunk;
    public BurstBounds Bounds;
    public float3 Position;

    public bool UpdateChunk(ref JarVoxelTerrain terrain, ChunkMeshData* chunkMeshData)    {
        this.ChunkMeshData = chunkMeshData;

        Lod = chunkMeshData->lod;
        Boundaries = (byte)chunkMeshData->boundaries;
        IsEdgeChunk = chunkMeshData->edge_chunk;
        Bounds = chunkMeshData->bounds;
        Position = Bounds.center;

        bool colliderUpdateRequired = false;
        bool generateCollider = Lod <= ColliderLodThreshold;

        if (generateCollider)
        {
            colliderUpdateRequired = true;
        }
        
        return colliderUpdateRequired;
    }

    public NativeArray<float3> CreateCollisionMesh(Allocator allocator)
    {
        if (ChunkMeshData == null)
        {
            return new NativeArray<float3>(0, allocator);
        }

        var collisionMesh = new NativeArray<float3>(ChunkMeshData->indices.Length, allocator);
        for (int i = 0; i < ChunkMeshData->indices.Length; i++)
        {
            collisionMesh[i] = ChunkMeshData->vertices[ChunkMeshData->indices[i]];
        }
        return collisionMesh;
    }
    
    public void Dispose()
    {
        if (MultiMeshInstances.IsCreated)
        {
            MultiMeshInstances.Dispose();
        }
    }
}