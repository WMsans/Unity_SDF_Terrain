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
    private JobHandle _jobHandle; // Use a single JobHandle to manage dependencies

    public MeshComputeScheduler(int maxConcurrentTasks)
    {
        _chunksToAdd = new NativeQueue<VoxelOctreeNodePointer>(Allocator.Persistent);
        _chunksToProcess = new NativeQueue<MeshGenerationResult>(Allocator.Persistent);
        _jobHandle = new JobHandle(); // Initialize the handle
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
        // Don't complete the job handle here, as it would stall the main thread.
        // Instead, use it as a dependency for the new jobs.
        
        var handles = new NativeList<JobHandle>(_chunksToAdd.Count, Allocator.Temp);
        while (_chunksToAdd.TryDequeue(out VoxelOctreeNodePointer chunkPointer))
        {
            var terrainData = terrain.GetTerrainData();
            if (!chunkPointer.Value->IsChunk(ref terrainData))
                continue;

            var job = new MeshGenerationJob
            {
                terrain = terrainData,
                chunk = chunkPointer.Value,
                ChunksToProcess = _chunksToProcess.AsParallelWriter()
            };
            // Schedule the new job with a dependency on the previous frame's jobs.
            handles.Add(job.Schedule(_jobHandle));
        }

        // Combine the handles of all newly scheduled jobs.
        // This new handle will be used as a dependency for the next frame's jobs.
        if (handles.Length > 0)
        {
            _jobHandle = JobHandle.CombineDependencies(handles.AsArray());
        }
        
        handles.Dispose();
    }

    public void ClearQueue()
    {
        _chunksToAdd.Clear();
        _chunksToProcess.Clear();
    }

    public bool IsMeshing()
    {
        // A more accurate check for whether we are processing meshes.
        return _chunksToAdd.Count > 0 || !_jobHandle.IsCompleted;
    }

    public void Dispose()
    {
        // Always complete any outstanding jobs before disposing of native containers.
        _jobHandle.Complete();
        
        // Dispose of any remaining mesh data in the queue
        while (_chunksToProcess.TryDequeue(out MeshGenerationResult result))
        {
            result.Dispose();
        }
        
        if (_chunksToAdd.IsCreated) _chunksToAdd.Dispose();
        if (_chunksToProcess.IsCreated) _chunksToProcess.Dispose();
    }
}