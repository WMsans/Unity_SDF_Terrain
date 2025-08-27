using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;

public struct ChunkMeshData
{
    public NativeList<float3> Vertices;
    public NativeList<int> Indices;
    public NativeList<float3> Normals;

    public ChunkMeshData(Allocator allocator)
    {
        Vertices = new NativeList<float3>(allocator);
        Indices = new NativeList<int>(allocator);
        Normals = new NativeList<float3>(allocator);
    }

    public void Dispose()
    {
        Vertices.Dispose();
        Indices.Dispose();
        Normals.Dispose();
    }
}