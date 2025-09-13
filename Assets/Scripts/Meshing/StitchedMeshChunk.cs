using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

[BurstCompile]
public struct StitchedMeshChunk
{
    public static readonly int3[] Offsets = {
        new int3(0, 0, 0), new int3(1, 0, 0), new int3(0, 1, 0), new int3(1, 1, 0),
        new int3(0, 0, 1), new int3(1, 0, 1), new int3(0, 1, 1), new int3(1, 1, 1)
    };

    public static readonly int4[] RingQuadChecks = {
        new int4(1, 5, 3, 7), // positive x
        new int4(0, 2, 4, 6), // negative x
        new int4(2, 3, 6, 7), // positive y
        new int4(0, 1, 4, 5), // negative y
        new int4(4, 5, 6, 7), // positive z
        new int4(0, 1, 2, 3)  // negative z
    };

    public static readonly float3[] CheckLodOffsets = {
        new float3(1, 0, 0), new float3(-1, 0, 0),
        new float3(0, 1, 0), new float3(0, -1, 0),
        new float3(0, 0, 1), new float3(0, 0, -1)
    };

    public static readonly int2[] Edges = {
        new int2(0, 1), new int2(2, 3), new int2(4, 5), new int2(6, 7), new int2(0, 2), new int2(1, 3),
        new int2(4, 6), new int2(5, 7), new int2(0, 4), new int2(1, 5), new int2(2, 6), new int2(3, 7)
    };

    public int3 Octant;
    public NativeList<VoxelOctreeNode> nodes;
    public NativeList<int3> positions;
    public NativeList<int> vertexIndices;
    public NativeList<int> faceDirs;
    public int innerNodeCount;
    public int ringNodeCount;
    public NativeHashMap<int3, int> _ringLut;

    public byte _lodL2HBoundaries;
    public byte _lodH2LBoundaries;
    private float3 half_leaf_size;
    private const int ChunkRes = 16 + 2;
    private const int LargestPos = ChunkRes - 1;
    private NativeArray<int> _leavesLut;

    public StitchedMeshChunk(JarVoxelTerrain terrain, VoxelOctreeNode chunk)
    {
        float3 chunkCenter = chunk._center;
        var cameraPosition = terrain.get_camera_position();
        Octant = new int3(chunkCenter.x > cameraPosition.x ? 1 : -1, chunkCenter.y > cameraPosition.y ? 1 : -1,
                        chunkCenter.z > cameraPosition.z ? 1 : -1);

        float leafSize = (1 << chunk.get_lod()) * terrain.get_octree_scale();
        BurstBounds bounds = chunk.get_bounds(terrain.get_octree_scale()).expanded(leafSize - 0.001f);
        nodes = new NativeList<VoxelOctreeNode>(Allocator.Temp);
        terrain.get_voxel_leaves_in_bounds(bounds, chunk.get_lod(), ref nodes);

        innerNodeCount = nodes.Length;
        bounds = bounds.expanded(0.001f);

        _lodH2LBoundaries = 0;
        _lodL2HBoundaries = 0;
        ringNodeCount = 0;
        _ringLut = new NativeHashMap<int3, int>(0, Allocator.Temp);
        half_leaf_size = float3.zero;
        _leavesLut = new NativeArray<int>(ChunkRes * ChunkRes * ChunkRes, Allocator.Temp);
        positions = new NativeList<int3>(Allocator.Temp);
        vertexIndices = new NativeList<int>(Allocator.Temp);
        faceDirs = new NativeList<int>(Allocator.Temp);


        if (nodes.IsEmpty)
        {
            return;
        }


        // ... rest of the constructor logic from stitched_mesh_chunk.cpp
    }


    public bool should_have_quad(int3 position, int face)
    {
        return true;
    }

    public bool on_positive_edge(int3 position)
    {
        return (((_lodH2LBoundaries & 0b1) != 0 && position.x >= LargestPos - 2) ? 1 : 0) +
               (((_lodH2LBoundaries & 0b100) != 0 && position.y >= LargestPos - 2) ? 1 : 0) +
               (((_lodH2LBoundaries & 0b10000) != 0 && position.z >= LargestPos - 2) ? 1 : 0) >= 2;
    }

    public int get_node_index_at(int3 pos)
    {
        if (pos.x < 0 || pos.x >= ChunkRes || pos.y < 0 || pos.y >= ChunkRes || pos.z < 0 || pos.z >= ChunkRes)
            return -1;
        else
            return (_leavesLut[pos.x + ChunkRes * (pos.y + ChunkRes * pos.z)] - 1);
    }

    public bool get_unique_neighbouring_vertices(int3 pos, int3[] offsets, ref NativeList<int> result)
    {
        // ... implementation
        return true;
    }

    public bool get_neighbours(int3 pos, ref NativeList<int> result)
    {
        // ... implementation
        return true;
    }

    public bool get_ring_neighbours(int3 pos, ref NativeList<int> result)
    {
        // ... implementation
        return true;
    }

    public bool should_have_boundary_quad(NativeArray<int> neighbours, bool on_ring)
    {
        // ... implementation
        return false;
    }

    public bool is_edge_chunk()
    {
        return _lodH2LBoundaries != 0 || _lodL2HBoundaries != 0;
    }

    public bool is_on_any_boundary(int3 position)
    {
        // ... implementation
        return false;
    }

    public bool is_on_boundary(byte boundaries, int3 position)
    {
        // ... implementation
        return false;
    }
}
