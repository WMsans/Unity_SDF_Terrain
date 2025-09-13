using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

[BurstCompile]
public struct ChunkMeshData
{
    public const int MaxCollisionLod = 2;

    public NativeArray<float3> vertices;
    public NativeArray<int> indices;
    public NativeArray<float3> collision_mesh;
    public int lod;
    public ushort boundaries;
    public bool edge_chunk;
    // TODO: remove bounds
    public BurstBounds bounds;
    public NativeHashMap<int3, int> edgeVertices;

    public bool has_collision_mesh()
    {
        return true; // lod <= MaxCollisionLod;
    }

    public ChunkMeshData(NativeArray<float3> verts, NativeArray<int> inds, int lod, bool edge_chunk, BurstBounds chunk_bounds)
    {
        this.vertices = verts;
        this.indices = inds;
        this.lod = lod;
        this.edge_chunk = edge_chunk;
        this.bounds = chunk_bounds;
        this.collision_mesh = new NativeArray<float3>(0, Allocator.Persistent);
        this.boundaries = 0;
        this.edgeVertices = new NativeHashMap<int3, int>(0, Allocator.Persistent);

        if (has_collision_mesh())
        {
            create_collision_mesh();
        }
    }

    public void create_collision_mesh() {
        collision_mesh = new NativeArray<float3>(indices.Length, Allocator.Persistent);
        for (int i = 0; i < indices.Length; i++) {
            collision_mesh[i] = vertices[indices[i]];
        }
    }
}
