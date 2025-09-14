using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct ModifyJob : IJob
{
    public VoxelOctreeNode Root;
    public ModifySettings Settings;
    public TerrainData Terrain;
    public NativeQueue<VoxelOctreeNodePointer>.ParallelWriter MainThreadUpdates;
    public NativeQueue<ChunkDeleteRequest>.ParallelWriter ChunkDeleteQueue;

    public void Execute()
    {
        Root.ModifySdfInBounds(ref Terrain, in Settings, Allocator.Persistent, MainThreadUpdates, ChunkDeleteQueue);
    }
}