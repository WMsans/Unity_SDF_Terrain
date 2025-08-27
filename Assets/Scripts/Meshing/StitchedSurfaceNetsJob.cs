using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct StitchedSurfaceNetsJob : IJob
{
    [ReadOnly] public StitchedMeshChunk Chunk;
    public ChunkMeshData MeshData;

    private static readonly int3[] CubeCorners = {
        new int3(0, 0, 0), new int3(1, 0, 0), new int3(0, 1, 0), new int3(1, 1, 0),
        new int3(0, 0, 1), new int3(1, 0, 1), new int3(0, 1, 1), new int3(1, 1, 1)
    };

    private static readonly int2[] Edges = {
        new int2(0, 1), new int2(2, 3), new int2(4, 5), new int2(6, 7),
        new int2(0, 2), new int2(1, 3), new int2(4, 6), new int2(5, 7),
        new int2(0, 4), new int2(1, 5), new int2(2, 6), new int2(3, 7)
    };

    public void Execute()
    {
        var vertexMap = new NativeHashMap<int3, int>(StitchedMeshChunk.ChunkSize3, Allocator.Temp);

        for (int z = 0; z < StitchedMeshChunk.ChunkSize - 1; z++)
        {
            for (int y = 0; y < StitchedMeshChunk.ChunkSize - 1; y++)
            {
                for (int x = 0; x < StitchedMeshChunk.ChunkSize - 1; x++)
                {
                    var pos = new int3(x, y, z);
                    CreateVertex(pos, vertexMap);
                }
            }
        }
        
        for (int z = 0; z < StitchedMeshChunk.ChunkSize - 1; z++)
        {
            for (int y = 0; y < StitchedMeshChunk.ChunkSize - 1; y++)
            {
                for (int x = 0; x < StitchedMeshChunk.ChunkSize - 1; x++)
                {
                    var pos = new int3(x, y, z);
                    CreateQuads(pos, vertexMap);
                }
            }
        }


        vertexMap.Dispose();
    }

    private void CreateVertex(int3 pos, NativeHashMap<int3, int> vertexMap)
    {
        float cornerValues_0 = GetVoxel(pos + CubeCorners[0]);
        float cornerValues_1 = GetVoxel(pos + CubeCorners[1]);
        float cornerValues_2 = GetVoxel(pos + CubeCorners[2]);
        float cornerValues_3 = GetVoxel(pos + CubeCorners[3]);
        float cornerValues_4 = GetVoxel(pos + CubeCorners[4]);
        float cornerValues_5 = GetVoxel(pos + CubeCorners[5]);
        float cornerValues_6 = GetVoxel(pos + CubeCorners[6]);
        float cornerValues_7 = GetVoxel(pos + CubeCorners[7]);

        float3 vertexPosition = float3.zero;
        float3 normal = float3.zero;
        int edgeCrossings = 0;

        for (int i = 0; i < 12; i++)
        {
            int c1 = Edges[i].x;
            int c2 = Edges[i].y;

            float v1 = GetVoxel(pos + CubeCorners[c1]);
            float v2 = GetVoxel(pos + CubeCorners[c2]);

            if (math.sign(v1) == math.sign(v2)) continue;
            
            float t = math.abs(v1) / (math.abs(v1) + math.abs(v2));
            vertexPosition += math.lerp(CubeCorners[c1], CubeCorners[c2], t);
            edgeCrossings++;
        }

        if (edgeCrossings == 0) return;

        vertexPosition /= edgeCrossings;
        
        normal += new float3(cornerValues_0 - cornerValues_1, cornerValues_2 - cornerValues_3, cornerValues_4 - cornerValues_5);
        normal = math.normalize(normal);

        vertexMap.Add(pos, MeshData.Vertices.Length);
        MeshData.Vertices.Add(pos + vertexPosition);
        MeshData.Normals.Add(normal);
    }
    
    void CreateQuads(int3 pos, NativeHashMap<int3, int> vertexMap)
    {
        if (!vertexMap.TryGetValue(pos, out int v0)) return;

        int[] faceNeighbors = new int[3];
        bool[] hasFaceNeighbor = new bool[3];
        hasFaceNeighbor[0] = vertexMap.TryGetValue(pos + new int3(1, 0, 0), out faceNeighbors[0]);
        hasFaceNeighbor[1] = vertexMap.TryGetValue(pos + new int3(0, 1, 0), out faceNeighbors[1]);
        hasFaceNeighbor[2] = vertexMap.TryGetValue(pos + new int3(0, 0, 1), out faceNeighbors[2]);

        for (int i = 0; i < 3; i++)
        {
            if (!hasFaceNeighbor[i]) continue;

            int v1 = faceNeighbors[i];
            int v2 = -1, v3 = -1;

            if (i == 0) // x face
            {
                if (!vertexMap.TryGetValue(pos + new int3(0, 0, 1), out v2)) continue;
                if (!vertexMap.TryGetValue(pos + new int3(1, 0, 1), out v3)) continue;
            }
            else if (i == 1) // y face
            {
                if (!vertexMap.TryGetValue(pos + new int3(1, 0, 0), out v2)) continue;
                if (!vertexMap.TryGetValue(pos + new int3(1, 1, 0), out v3)) continue;
            }
            else // z face
            {
                if (!vertexMap.TryGetValue(pos + new int3(0, 1, 0), out v2)) continue;
                if (!vertexMap.TryGetValue(pos + new int3(0, 1, 1), out v3)) continue;
            }
            
            AddQuad(v0, v1, v3, v2);
        }
    }


    private float GetVoxel(int3 pos)
    {
        return Chunk.VoxelData[pos.x + pos.y * StitchedMeshChunk.ChunkSize + pos.z * StitchedMeshChunk.ChunkSize2];
    }
    
    private void AddQuad(int v0, int v1, int v2, int v3)
    {
        MeshData.Indices.Add(v0);
        MeshData.Indices.Add(v1);
        MeshData.Indices.Add(v2);

        MeshData.Indices.Add(v0);
        MeshData.Indices.Add(v2);
        MeshData.Indices.Add(v3);
    }
}