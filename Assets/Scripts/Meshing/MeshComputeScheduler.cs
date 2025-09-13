using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct MeshComputeScheduler
{
    private NativeQueue<VoxelOctreeNode> ChunksToAdd;
    private NativeQueue<ChunkMeshData> ChunksToProcess;
    private NativeList<JobHandle> _jobHandles;

    // Debug variables
    private int _totalTris;
    private int _prevTris;

    public MeshComputeScheduler(int maxConcurrentTasks)
    {
        _totalTris = 0;
        _prevTris = 0;
        ChunksToAdd = new NativeQueue<VoxelOctreeNode>(Allocator.Persistent);
        ChunksToProcess = new NativeQueue<ChunkMeshData>(Allocator.Persistent);
        _jobHandles = new NativeList<JobHandle>(maxConcurrentTasks, Allocator.Persistent);
    }

    public void enqueue(VoxelOctreeNode node)
    {
        ChunksToAdd.Enqueue(node);
    }

    public void process(JarVoxelTerrain terrain)
    {
        _prevTris = _totalTris;
        if (!terrain.is_building())
        {
            process_queue(terrain);
        }

        JobHandle.CompleteAll(_jobHandles);
        _jobHandles.Clear();

        while (ChunksToProcess.Count > 0)
        {
            if (ChunksToProcess.TryDequeue(out ChunkMeshData chunkMeshData))
            {
                 // In a real implementation, you'd get the node associated with this mesh data
                 // and call an update method.
                 // node.update_chunk(terrain, chunkMeshData);
            }
        }
    }

    private void process_queue(JarVoxelTerrain terrain)
    {
        while (ChunksToAdd.Count > 0)
        {
            if (ChunksToAdd.TryDequeue(out VoxelOctreeNode chunk))
            {
                run_task(terrain, chunk);
            }
            else
                return;
        }
    }

    private void run_task(JarVoxelTerrain terrain, VoxelOctreeNode chunk)
    {
        if (!chunk.is_chunk(terrain))
            return;

        var job = new MeshGenerationJob
        {
            terrain = terrain,
            chunk = chunk,
            ChunksToProcess = ChunksToProcess.AsParallelWriter()
        };
        _jobHandles.Add(job.Schedule());
    }

    public void clear_queue()
    {
        ChunksToAdd.Clear();
        ChunksToProcess.Clear();
    }

    public bool is_meshing()
    {
        return ChunksToAdd.Count > 0;
    }
}
