using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

[BurstCompile]
public unsafe struct MeshGenerationResult : IDisposable
{
    public VoxelOctreeNode* chunk;
    
    [NativeDisableUnsafePtrRestriction] public float3* vertices;
    public int vertexCount;
    [NativeDisableUnsafePtrRestriction] public int* indices;
    public int indexCount;

    public int lod;
    public ushort boundaries;
    public bool edge_chunk;
    public BurstBounds bounds;

    public void Dispose()
    {
        if (vertices != null)
        {
            UnsafeUtility.Free(vertices, Allocator.Persistent);
            vertices = null;
        }
        if (indices != null)
        {
            UnsafeUtility.Free(indices, Allocator.Persistent);
            indices = null;
        }
    }
}