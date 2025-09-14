using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct BuildJob : IJob
{
    [NativeDisableUnsafePtrRestriction]
    public VoxelOctreeNode Root;
    public TerrainData Terrain;
    public NativeQueue<VoxelOctreeNodePointer>.ParallelWriter MainThreadUpdates;
    public NativeQueue<ChunkDeleteRequest>.ParallelWriter ChunkDeleteQueue;

    public void Execute()
    {
        Root.Build(ref Terrain, Allocator.Persistent, MainThreadUpdates, ChunkDeleteQueue);
    }
}