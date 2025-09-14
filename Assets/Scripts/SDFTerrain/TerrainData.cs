using Unity.Burst;
using UnityEngine;

[BurstCompile]
public struct TerrainData
{
    public float octreeScale;
    public int minChunkSize;
    public SdfData sdf;
    public JarVoxelLod lod;
}
