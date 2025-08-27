using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct VoxelLod
{
    public int LodLevelCount;
    public int ShellSize;
    public float OctreeScale;
    public float3 CameraPosition;

    public VoxelLod(int lodLevelCount, int shellSize, float octreeScale, float3 cameraPosition)
    {
        LodLevelCount = lodLevelCount;
        ShellSize = shellSize;
        OctreeScale = octreeScale;
        CameraPosition = cameraPosition;
    }

    public int GetLod(float3 position)
    {
        const float rChunkSize = 1.0f / 16.0f;
        float3 pos = position * rChunkSize;
        float3 camPos = CameraPosition * rChunkSize;

        float3 delta = math.abs(pos - camPos) / (2.0f * ShellSize);
        int lod = (int)math.floor(math.log2(math.max(1.0f, math.max(delta.x, math.max(delta.y, delta.z)))));
        lod = math.max(0, lod);

        if (!IsInLodShell(lod, pos, camPos))
        {
            return lod + 1;
        }

        if (lod > 0 && IsInLodShell(lod - 1, pos, camPos))
        {
            return lod - 1;
        }

        return lod;
    }

    private bool IsInLodShell(int lod, float3 pos, float3 camPos)
    {
        float gridSize = LodToGridSize(lod) * 2.0f;
        float3 lodCamPos = SnapToGrid(camPos, gridSize);
        float3 delta = math.abs(pos - lodCamPos);
        float dist = math.max(delta.x, math.max(delta.y, delta.z));
        return dist < (gridSize * ShellSize);
    }

    private float LodToGridSize(int lod)
    {
        return (1 << (lod + 1)) * OctreeScale;
    }

    private float3 SnapToGrid(float3 pos, float gridSize)
    {
        return math.floor(pos / gridSize) * gridSize;
    }
}