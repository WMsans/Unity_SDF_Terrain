using Unity.Collections;
using Unity.Mathematics;

public struct StitchedMeshChunk
{
    public const int ChunkSize = 16;
    public const int ChunkSize2 = ChunkSize * ChunkSize;
    public const int ChunkSize3 = ChunkSize * ChunkSize * ChunkSize;

    public NativeArray<float> VoxelData;
    public int Lod;

    public StitchedMeshChunk(int lod, Allocator allocator)
    {
        VoxelData = new NativeArray<float>(ChunkSize3, allocator);
        Lod = lod;
    }

    public void Dispose()
    {
        VoxelData.Dispose();
    }
}