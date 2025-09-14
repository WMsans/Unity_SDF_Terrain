using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using static Unity.Mathematics.math;

[BurstCompile]
public unsafe struct StitchedMeshChunk : IDisposable
{
    // --- STATIC FIELDS ---

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

    public static readonly int3[] CheckLodOffsets = {
        new int3(1, 0, 0), new int3(-1, 0, 0),
        new int3(0, 1, 0), new int3(0, -1, 0),
        new int3(0, 0, 1), new int3(0, 0, -1)
    };

    public static readonly int2[] Edges = {
        new int2(0, 1), new int2(2, 3), new int2(4, 5), new int2(6, 7), new int2(0, 2), new int2(1, 3),
        new int2(4, 6), new int2(5, 7), new int2(0, 4), new int2(1, 5), new int2(2, 6), new int2(3, 7)
    };
    
    // Note: In the C++ code, this is a vector of vectors. 
    // For Burst, we flatten it and access it with strides.
    public static readonly int3[] FaceOffsets = {
        // YZ Plane (Face 0)
        new int3(0, 0, 0), new int3(0, 1, 0), new int3(0, 0, 1), new int3(0, 1, 1),
        // XZ Plane (Face 1)
        new int3(0, 0, 0), new int3(1, 0, 0), new int3(0, 0, 1), new int3(1, 0, 1),
        // XY Plane (Face 2)
        new int3(0, 0, 0), new int3(1, 0, 0), new int3(0, 1, 0), new int3(1, 1, 0)
    };


    // --- INSTANCE FIELDS ---

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
    private const float LEAF_COUNT = 16.0f;
    private NativeArray<int> _leavesLut;
    
    // --- CONSTRUCTOR ---

    public StitchedMeshChunk(TerrainData terrain, VoxelOctreeNode* chunk)
    {
        float3 chunkCenter = chunk->_center;
        var cameraPosition = terrain.lod.CameraPosition;
        Octant = new int3(chunkCenter.x > cameraPosition.x ? 1 : -1, chunkCenter.y > cameraPosition.y ? 1 : -1,
                        chunkCenter.z > cameraPosition.z ? 1 : -1);

        float leafSize = (1 << chunk->LoD) * terrain.octreeScale;
        BurstBounds bounds = chunk->GetBounds(terrain.octreeScale).Expanded(leafSize - 0.001f);
        
        nodes = new NativeList<VoxelOctreeNode>(Allocator.Temp);
        chunk->GetVoxelLeavesInBounds(in terrain, bounds, nodes, chunk->LoD);
        
        innerNodeCount = nodes.Length;
        bounds = bounds.Expanded(0.001f);

        // Initialize collections
        positions = new NativeList<int3>(innerNodeCount, Allocator.Temp);
        vertexIndices = new NativeList<int>(innerNodeCount, Allocator.Temp);
        faceDirs = new NativeList<int>(innerNodeCount, Allocator.Temp);
        _leavesLut = new NativeArray<int>(ChunkRes * ChunkRes * ChunkRes, Allocator.Temp);
        _ringLut = new NativeHashMap<int3, int>(0, Allocator.Temp);
        
        ringNodeCount = 0;
        half_leaf_size = new float3(leafSize * 0.5f);

        if (nodes.Length == 0)
        {
             _lodH2LBoundaries = 0;
             _lodL2HBoundaries = 0;
             return;
        }

        ushort boundaries = chunk->ComputeBoundaries(ref terrain);
        _lodH2LBoundaries = (byte)(boundaries & 0xFF);
        _lodL2HBoundaries = (byte)((boundaries >> 8) & 0xFF);

        // Initialize positions and LUT for inner nodes
        float normalizingFactor = 1.0f / leafSize;
        float3 minPos = bounds.min;
        int3 clampMax = new int3(LargestPos);

        positions.Resize(nodes.Length, NativeArrayOptions.UninitializedMemory);
        vertexIndices.Resize(nodes.Length, NativeArrayOptions.UninitializedMemory);
        faceDirs.Resize(nodes.Length, NativeArrayOptions.UninitializedMemory);

        for (int i = 0; i < nodes.Length; i++)
        {
            VoxelOctreeNode node = nodes[i];
            int3 pos = (int3)ceil((node._center - minPos) * normalizingFactor) - new int3(1);
            pos = clamp(pos, new(0,0,0), clampMax);
            
            positions[i] = pos;
            vertexIndices[i] = -1;
            _leavesLut[pos.x + ChunkRes * (pos.y + ChunkRes * pos.z)] = i + 1;
        }
        
        // Handle LOD boundaries and ring nodes
        if (_lodH2LBoundaries != 0)
        {
            normalizingFactor = 0.5f / leafSize;
            float edge_length = chunk->EdgeLength(terrain.octreeScale);
            
            BurstBounds acceptance_bounds = default;
            BurstBounds rejection_bounds = chunk->GetBounds(terrain.octreeScale);

            for (int i = 0; i < 6; i++)
            {
                if (((_lodH2LBoundaries >> i) & 0b1) != 1) continue;
                
                float3 edge = (edge_length * 0.5f) * (float3)CheckLodOffsets[i];
                
                // C++ RingBounds logic translated
                BurstBounds b;
                int axis = i / 2;
                if (axis == 0) // X
                    b = new BurstBounds(new(0,0,0), new float3(0, 1, 1));
                else if (axis == 1) // Y
                    b = new BurstBounds(new(0,0,0), new float3(1, 0, 1));
                else // Z
                    b = new BurstBounds(new(0,0,0), new float3(1, 1, 0));
                
                b.size *= edge_length;
                b.center = chunkCenter + edge;
                
                if(acceptance_bounds.size.x == 0) acceptance_bounds = b;
                else acceptance_bounds = new BurstBounds((acceptance_bounds.min + b.min) / 2, (acceptance_bounds.max + b.max) / 2);


                float3 difference = (edge_length * 2.0f / LEAF_COUNT) * abs((float3)CheckLodOffsets[i]);
                if (i % 2 == 0)
                    rejection_bounds.size -= difference;
                else
                    rejection_bounds.center += difference;
            }

            acceptance_bounds = acceptance_bounds.Expanded(-0.001f);
            rejection_bounds = rejection_bounds.Expanded(-0.001f);
            
            chunk->GetVoxelLeavesInBounds(in terrain, acceptance_bounds, nodes, chunk->LoD + 1, rejection_bounds);
            
            ringNodeCount = nodes.Length - innerNodeCount;
            if (ringNodeCount <= 0) return;

            _ringLut.Capacity = ringNodeCount;
            
            minPos = chunkCenter - 10.0f / LEAF_COUNT * edge_length;
            clampMax = new int3(9);
            
            for (int i = innerNodeCount; i < nodes.Length; i++)
            {
                VoxelOctreeNode node = nodes[i];
                int3 pos = (int3)ceil((node._center - minPos) * normalizingFactor) - new int3(1);
                pos = clamp(pos, new(0,0,0), clampMax);

                positions.Add(pos);
                vertexIndices.Add(-1);
                faceDirs.Add(0);
                _ringLut.TryAdd(pos, i);
            }
        }
    }

    // --- METHODS ---

    public bool should_have_quad(int3 position, int face)
    {
        if (_lodL2HBoundaries != 0) return true;
        
        switch (face)
        {
            case 0: return position.x < LargestPos;
            case 1: return position.y < LargestPos;
            case 2: return position.z < LargestPos;
            default: return true;
        }
    }

    public bool on_positive_edge(int3 position)
    {
        int count = 0;
        if ((_lodH2LBoundaries & 0b1) != 0 && position.x >= LargestPos - 2) count++;
        if ((_lodH2LBoundaries & 0b100) != 0 && position.y >= LargestPos - 2) count++;
        if ((_lodH2LBoundaries & 0b10000) != 0 && position.z >= LargestPos - 2) count++;
        return count >= 2;
    }

    public int get_node_index_at(int3 pos)
    {
        if (any(pos < 0) || any(pos >= ChunkRes))
            return -1;
            
        return (_leavesLut[pos.x + ChunkRes * (pos.y + ChunkRes * pos.z)] - 1);
    }
    
    public bool get_unique_neighbouring_vertices(int3 pos, int face, ref NativeList<int> result)
    {
        for (int i = 0; i < 4; i++)
        {
            // Access the flattened FaceOffsets array
            var o = FaceOffsets[face * 4 + i];
            var n = get_node_index_at(pos + o);
            if (n < 0 || vertexIndices[n] < 0)
            {
                return false;
            }
            if (!result.Contains(n))
                result.Add(n);
        }
        return true;
    }


    public bool get_neighbours(int3 pos, ref NativeList<int> result)
    {
        for (int i=0; i < Offsets.Length; i++)
        {
            var o = Offsets[i];
            var n = get_node_index_at(pos + o);
            if (n < 0)
            {
                return false;
            }
            result.Add(n);
        }
        return true;
    }

    public bool get_ring_neighbours(int3 pos, ref NativeList<int> result)
    {
       for (int i=0; i < Offsets.Length; i++)
        {
            var o = Offsets[i];
            if (!_ringLut.TryGetValue(pos + o, out int index) || index < 0)
            {
                 return false;
            }
            result.Add(index);
        }
        return true;
    }
    
    public bool should_have_boundary_quad(NativeArray<int> neighbours, bool on_ring)
    {
        for (int i = 0; i < 6; i++)
        {
            if (((_lodH2LBoundaries >> i) & 0b1) != 1) continue;

            int j = i;
            if (on_ring)
            {
                j = (j % 2 == 0) ? j + 1 : j - 1;
            }

            int4 nx = RingQuadChecks[j];
            float s0 = sign(nodes[neighbours[nx.x]].GetValue());
            float s1 = sign(nodes[neighbours[nx.y]].GetValue());
            float s2 = sign(nodes[neighbours[nx.z]].GetValue());
            float s3 = sign(nodes[neighbours[nx.w]].GetValue());

            if (s0 != s1 || s1 != s2 || s2 != s3)
                return true;
        }
        return false;
    }

    public bool is_edge_chunk()
    {
        return _lodH2LBoundaries != 0 || _lodL2HBoundaries != 0;
    }
    
    public bool is_on_any_boundary(int3 position)
    {
        return _lodH2LBoundaries != 0 && (
            (position.x == LargestPos - 2 && (_lodH2LBoundaries & 0b1) > 0) ||
            (position.x == 1 && (_lodH2LBoundaries & 0b10) > 0) ||
            (position.y == LargestPos - 2 && (_lodH2LBoundaries & 0b100) > 0) ||
            (position.y == 1 && (_lodH2LBoundaries & 0b1000) > 0) ||
            (position.z == LargestPos - 2 && (_lodH2LBoundaries & 0b10000) > 0) ||
            (position.z == 1 && (_lodH2LBoundaries & 0b100000) > 0)
        );
    }
    
    public bool is_on_boundary(byte boundaries, int3 position)
    {
        return boundaries != 0 && (
            (position.x == LargestPos && (boundaries & 0b1) > 0) ||
            (position.x == 0 && (boundaries & 0b10) > 0) ||
            (position.y == LargestPos && (boundaries & 0b100) > 0) ||
            (position.y == 0 && (boundaries & 0b1000) > 0) ||
            (position.z == LargestPos && (boundaries & 0b10000) > 0) ||
            (position.z == 0 && (boundaries & 0b100000) > 0)
        );
    }
    
    public void Dispose()
    {
        if (nodes.IsCreated) nodes.Dispose();
        if (positions.IsCreated) positions.Dispose();
        if (vertexIndices.IsCreated) vertexIndices.Dispose();
        if (faceDirs.IsCreated) faceDirs.Dispose();
        if (_ringLut.IsCreated) _ringLut.Dispose();
        if (_leavesLut.IsCreated) _leavesLut.Dispose();
    }
}