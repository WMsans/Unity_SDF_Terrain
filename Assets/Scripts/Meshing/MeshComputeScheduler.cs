using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

// Wrapper struct to hold a pointer, allowing it to be used as a generic type argument in NativeQueue.
[BurstCompile]
public unsafe struct VoxelOctreeNodePointer
{
    [NativeDisableUnsafePtrRestriction]
    public VoxelOctreeNode* Value;
}


[BurstCompile]
public unsafe struct MeshComputeScheduler : IDisposable
{
    private NativeQueue<VoxelOctreeNodePointer> _chunksToAdd;
    private NativeQueue<MeshGenerationResult> _chunksToProcess;
    private NativeList<JobHandle> _jobHandles;

    public MeshComputeScheduler(int maxConcurrentTasks)
    {
        _chunksToAdd = new NativeQueue<VoxelOctreeNodePointer>(Allocator.Persistent);
        _chunksToProcess = new NativeQueue<MeshGenerationResult>(Allocator.Persistent);
        _jobHandles = new NativeList<JobHandle>(maxConcurrentTasks, Allocator.Persistent);
    }

    public void Enqueue(VoxelOctreeNode* node)
    {
        _chunksToAdd.Enqueue(new VoxelOctreeNodePointer { Value = node });
    }
    
    public bool TryGetResult(out MeshGenerationResult result)
    {
        return _chunksToProcess.TryDequeue(out result);
    }


    public void Process(JarVoxelTerrain terrain)
    {
        ScheduleJobs(terrain);
        JobHandle.CompleteAll(_jobHandles.AsArray());
        _jobHandles.Clear();
    }

    public void ScheduleJobs(JarVoxelTerrain terrain)
    {
        while (_chunksToAdd.TryDequeue(out VoxelOctreeNodePointer chunkPointer))
        {
            RunTask(terrain, chunkPointer.Value);
        }
    }

    private void RunTask(JarVoxelTerrain terrain, VoxelOctreeNode* chunk)
    {
        var terrainData = terrain.GetTerrainData();
        if (!chunk->IsChunk(ref terrainData))
            return;

        var job = new MeshGenerationJob
        {
            terrain = terrainData,
            chunk = chunk,
            ChunksToProcess = _chunksToProcess.AsParallelWriter()
        };
        _jobHandles.Add(job.Schedule());
    }

    public void ClearQueue()
    {
        _chunksToAdd.Clear();
        _chunksToProcess.Clear();
    }

    public bool IsMeshing()
    {
        return _chunksToAdd.Count > 0 || _jobHandles.Length > 0;
    }

    public void Dispose()
    {
        _jobHandles.Dispose();
        _chunksToAdd.Dispose();
        _chunksToProcess.Dispose();
    }
}