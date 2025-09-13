using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct StitchedSurfaceNets
{
    private NativeList<float3> _verts;
    private NativeList<float3> _normals;
    private NativeList<float4> _colors;
    private NativeList<int> _indices;
    private NativeHashMap<int3, int> _innerEdgeNodes;
    private NativeHashMap<int3, int> _ringEdgeNodes;

    private VoxelOctreeNode _chunk;
    private bool _cubicVoxels;
    private StitchedMeshChunk _meshChunk;


    public StitchedSurfaceNets(JarVoxelTerrain terrain, VoxelOctreeNode chunk)
    {
        _chunk = chunk;
        _cubicVoxels = terrain.get_cubic_voxels();
        _meshChunk = new StitchedMeshChunk(terrain, chunk);
        _verts = new NativeList<float3>(Allocator.Temp);
        _normals = new NativeList<float3>(Allocator.Temp);
        _colors = new NativeList<float4>(Allocator.Temp);
        _indices = new NativeList<int>(Allocator.Temp);
        _innerEdgeNodes = new NativeHashMap<int3, int>(0, Allocator.Temp);
        _ringEdgeNodes = new NativeHashMap<int3, int>(0, Allocator.Temp);
    }

    public void Dispose()
    {
        _verts.Dispose();
        _normals.Dispose();
        _colors.Dispose();
        _indices.Dispose();
        _innerEdgeNodes.Dispose();
        _ringEdgeNodes.Dispose();
    }

    private void add_tri(int n0, int n1, int n2, bool flip)
    {
        if (!flip)
        {
            _indices.Add(n0);
            _indices.Add(n1);
            _indices.Add(n2);
        }
        else
        {
            _indices.Add(n1);
            _indices.Add(n0);
            _indices.Add(n2);
        }
    }

    private void add_tri_fix_normal(int n0, int n1, int n2)
    {
        float3 normal = math.cross(_verts[n1] - _verts[n0], _verts[n2] - _verts[n0]);
        add_tri(n0, n1, n2, math.dot(normal, _normals[n0]) > 0);
    }

    private void create_vertex(int node_id, NativeArray<int> neighbours, bool on_ring)
    {
        // ... implementation from stitched_surface_nets.cpp
    }

    private NativeList<NativeList<int>> find_ring_nodes(int3 pos, int face)
    {
        // ... implementation from stitched_surface_nets.cpp
        return new NativeList<NativeList<int>>(Allocator.Temp);
    }

    public ChunkMeshData? generate_mesh_data(JarVoxelTerrain terrain)
    {
        // ... full implementation from stitched_surface_nets.cpp
        return null;
    }
}