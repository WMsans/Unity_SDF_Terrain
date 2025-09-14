using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using static Unity.Mathematics.math;

[BurstCompile]
public unsafe struct StitchedSurfaceNets
{
    private NativeList<float3> _verts;
    private NativeList<float3> _normals;
    private NativeList<float4> _colors;
    private NativeList<int> _indices;
    private NativeHashMap<int3, int> _innerEdgeNodes;
    private NativeHashMap<int3, int> _ringEdgeNodes;

    private VoxelOctreeNode* _chunk;
    private StitchedMeshChunk _meshChunk;
    private TerrainData _terrain;

    public StitchedSurfaceNets(TerrainData terrain, VoxelOctreeNode* chunk)
    {
        _chunk = chunk;
        _terrain = terrain;
        _meshChunk = new StitchedMeshChunk(terrain, chunk);

        _verts = new NativeList<float3>(Allocator.Temp);
        _normals = new NativeList<float3>(Allocator.Temp);
        _colors = new NativeList<float4>(Allocator.Temp);
        _indices = new NativeList<int>(Allocator.Temp);
        _innerEdgeNodes = new NativeHashMap<int3, int>(_meshChunk.innerNodeCount, Allocator.Temp);
        _ringEdgeNodes = new NativeHashMap<int3, int>(_meshChunk.ringNodeCount, Allocator.Temp);
    }

    public ChunkMeshData GenerateMeshData()
    {
        // Pass 1: Create vertices for the main body of the chunk
        for (int nodeId = 0; nodeId < _meshChunk.innerNodeCount; nodeId++)
        {
            if (_meshChunk.vertexIndices[nodeId] <= -2) continue;
            
            var neighbours = new NativeList<int>(8, Allocator.Temp);
            if (!_meshChunk.get_neighbours(_meshChunk.positions[nodeId], ref neighbours))
            {
                neighbours.Dispose();
                continue;
            }
            CreateVertex(nodeId, neighbours, false);
            neighbours.Dispose();
        }

        if (_verts.Length == 0)
        {
            Dispose();
            return default;
        }

        // Pass 2: Create vertices for the transition (ring) nodes on LOD boundaries
        for (int nodeId = _meshChunk.innerNodeCount; nodeId < _meshChunk.innerNodeCount + _meshChunk.ringNodeCount; nodeId++)
        {
            if (_meshChunk.vertexIndices[nodeId] <= -2) continue;

            var neighbours = new NativeList<int>(8, Allocator.Temp);
            if (!_meshChunk.get_ring_neighbours(_meshChunk.positions[nodeId], ref neighbours))
            {
                neighbours.Dispose();
                continue;
            }
            CreateVertex(nodeId, neighbours, true);
            neighbours.Dispose();
        }

        // Pass 3: Generate faces for the main body
        for (int nodeId = 0; nodeId < _meshChunk.innerNodeCount; nodeId++)
        {
            if (_meshChunk.vertexIndices[nodeId] <= -1) continue;

            var pos = _meshChunk.positions[nodeId];
            var faceDirs = _meshChunk.faceDirs[nodeId];

            for (int i = 0; i < 3; i++)
            {
                int flipFace = ((faceDirs >> (2 * i)) & 3) - 1;
                if (flipFace == 0 || !_meshChunk.should_have_quad(pos, i)) continue;

                var neighbours = new NativeList<int>(4, Allocator.Temp);
                if (_meshChunk.get_unique_neighbouring_vertices(pos, i, ref neighbours) && neighbours.Length == 4)
                {
                    int n0 = _meshChunk.vertexIndices[neighbours[0]];
                    int n1 = _meshChunk.vertexIndices[neighbours[1]];
                    int n2 = _meshChunk.vertexIndices[neighbours[2]];
                    int n3 = _meshChunk.vertexIndices[neighbours[3]];

                    if (distancesq(_verts[n0], _verts[n3]) < distancesq(_verts[n1], _verts[n2]))
                    {
                        AddTri(n0, n1, n3, flipFace == -1);
                        AddTri(n0, n3, n2, flipFace == -1);
                    }
                    else
                    {
                        AddTri(n1, n3, n2, flipFace == -1);
                        AddTri(n1, n2, n0, flipFace == -1);
                    }
                }
                neighbours.Dispose();
            }
        }
        
        if (_indices.Length == 0)
        {
            Dispose();
            return default;
        }

        // Pass 4: Stitch edge chunk faces to lower LOD neighbours
        if (_meshChunk.is_edge_chunk())
        {
            var innerEdgeNodes = _innerEdgeNodes.GetKeyValueArrays(Allocator.Temp);
            for(int i = 0; i < innerEdgeNodes.Length; ++i)
            {
                var pos = innerEdgeNodes.Keys[i];
                var nodeId = innerEdgeNodes.Values[i];

                for (int face = 0; face < 3; face++)
                {
                    var ringNodes = FindRingNodes(pos, face);
                    
                    int n0 = _meshChunk.vertexIndices[nodeId];
                    int n1 = -1;

                    var nextPos = pos + StitchedMeshChunk.CheckLodOffsets[face];
                    if(_innerEdgeNodes.TryGetValue(nextPos, out int innerNeighbour))
                    {
                        n1 = _meshChunk.vertexIndices[innerNeighbour];
                    }

                    if(n1 == -1) continue;

                    for(int j = 0; j < ringNodes.Length; j++)
                    {
                        var pair = ringNodes[j];
                        if (pair.y != -1) // It's a quad
                        {
                            int n2 = _meshChunk.vertexIndices[pair.x];
                            int n3 = _meshChunk.vertexIndices[pair.y];
                            if (distancesq(_verts[n0], _verts[n3]) < distancesq(_verts[n1], _verts[n2]))
                            {
                                AddTriFixNormal(n0, n1, n3);
                                AddTriFixNormal(n0, n3, n2);
                            }
                            else
                            {
                                AddTriFixNormal(n1, n3, n2);
                                AddTriFixNormal(n1, n2, n0);
                            }
                        }
                        else // It's a triangle
                        {
                            int n2 = _meshChunk.vertexIndices[pair.x];
                            AddTriFixNormal(n0, n1, n2);
                        }
                    }
                    ringNodes.Dispose();
                }
            }
            innerEdgeNodes.Dispose();
        }

        var bounds = _chunk->GetBounds(_terrain.octreeScale);
        var chunkMeshData = new ChunkMeshData(_verts.ToArray(Allocator.Persistent), _indices.ToArray(Allocator.Persistent), _chunk->LoD, _meshChunk.is_edge_chunk(), bounds);

        Dispose();
        return chunkMeshData;
    }

    private void CreateVertex(int nodeId, NativeList<int> neighbours, bool onRing)
    {
        float3 vertexPosition = new();
        float4 color = new();
        float3 normal = new();
        int edgeCrossings = 0;

        for (int i = 0; i < StitchedMeshChunk.Edges.Length; i++)
        {
            var edge = StitchedMeshChunk.Edges[i];
            var na = _meshChunk.nodes[neighbours[edge.x]];
            var nb = _meshChunk.nodes[neighbours[edge.y]];

            float valueA = na.GetValue();
            float valueB = nb.GetValue();

            if (sign(valueA) == sign(valueB)) continue;
            
            float3 posA = na._center;
            float3 posB = nb._center;

            normal += (valueB - valueA) * (posB - posA);
            
            float t = abs(valueA) / (abs(valueA) + abs(valueB));
            vertexPosition += lerp(posA, posB, t);
            edgeCrossings++;
            color += lerp(na.GetColor(), nb.GetColor(), t);
        }

        if (edgeCrossings <= 0) return;

        _meshChunk.faceDirs[nodeId] =
            (int)(sign(sign(_meshChunk.nodes[neighbours[6]].GetValue()) - sign(_meshChunk.nodes[neighbours[7]].GetValue())) + 1) << 0 |
            (int)(sign(sign(_meshChunk.nodes[neighbours[7]].GetValue()) - sign(_meshChunk.nodes[neighbours[5]].GetValue())) + 1) << 2 |
            (int)(sign(sign(_meshChunk.nodes[neighbours[3]].GetValue()) - sign(_meshChunk.nodes[neighbours[7]].GetValue())) + 1) << 4;

        vertexPosition /= edgeCrossings;
        color /= edgeCrossings;
        normal = normalize(normal);

        vertexPosition -= _chunk->_center;
        int vertexIndex = _verts.Length;
        int3 gridPosition = _meshChunk.positions[nodeId];

        if ((onRing || _meshChunk.is_on_any_boundary(gridPosition)) && _meshChunk.should_have_boundary_quad(neighbours.AsArray(), onRing))
        {
            if (onRing)
                _ringEdgeNodes.TryAdd(gridPosition, nodeId);
            else
                _innerEdgeNodes.TryAdd(gridPosition, nodeId);
        }

        _meshChunk.vertexIndices[nodeId] = vertexIndex;
        _verts.Add(vertexPosition);
        _normals.Add(normal);
        _colors.Add(color);
    }
    
    private NativeList<int2> FindRingNodes(int3 pos, int face)
    {
        var result = new NativeList<int2>(4, Allocator.Temp);
        
        var faceOffsets = new int3(1,0,0);
        if(face == 1) faceOffsets = new int3(0,1,0);
        else if (face == 2) faceOffsets = new int3(0,0,1);

        var ringOffsets = new NativeArray<int3>(8, Allocator.Temp);
        if (face == 0) { // X face
            ringOffsets[0] = new int3(0, 1, 0); ringOffsets[1] = new int3(0, -1, 0);
            ringOffsets[2] = new int3(0, 0, 1); ringOffsets[3] = new int3(0, 0, -1);
            ringOffsets[4] = new int3(0, 1, 1); ringOffsets[5] = new int3(0, -1, -1);
            ringOffsets[6] = new int3(0, -1, 1); ringOffsets[7] = new int3(0, 1, -1);
        } else if (face == 1) { // Y face
            ringOffsets[0] = new int3(1, 0, 0); ringOffsets[1] = new int3(-1, 0, 0);
            ringOffsets[2] = new int3(0, 0, 1); ringOffsets[3] = new int3(0, 0, -1);
            ringOffsets[4] = new int3(1, 0, 1); ringOffsets[5] = new int3(-1, 0, -1);
            ringOffsets[6] = new int3(-1, 0, 1); ringOffsets[7] = new int3(1, 0, -1);
        } else { // Z face
            ringOffsets[0] = new int3(1, 0, 0); ringOffsets[1] = new int3(-1, 0, 0);
            ringOffsets[2] = new int3(0, 1, 0); ringOffsets[3] = new int3(0, -1, 0);
            ringOffsets[4] = new int3(1, 1, 0); ringOffsets[5] = new int3(-1, -1, 0);
            ringOffsets[6] = new int3(-1, 1, 0); ringOffsets[7] = new int3(1, -1, 0);
        }
        
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                var dir = ringOffsets[j];
                int n0 = GetRingNode(pos + dir);
                int n1 = GetRingNode(pos + dir + faceOffsets);

                if (n0 >= 0 && n1 >= 0)
                    result.Add(new int2(n0, n1));
                else if (n0 >= 0)
                    result.Add(new int2(n0, -1));
                else if (n1 >= 0)
                    result.Add(new int2(n1, -1));
            }
        }
        ringOffsets.Dispose();
        return result;
    }

    private int GetRingNode(int3 pos)
    {
        int3 ringPos = (int3)floor((float3)pos / 2.0f);
        if (_ringEdgeNodes.TryGetValue(ringPos, out int nodeId) && nodeId >= 0 && _meshChunk.vertexIndices[nodeId] >= 0)
        {
            return nodeId;
        }
        return -1;
    }

    private void AddTriFixNormal(int n0, int n1, int n2)
    {
        float3 normal = cross(_verts[n1] - _verts[n0], _verts[n2] - _verts[n0]);
        AddTri(n0, n1, n2, dot(normal, _normals[n0]) > 0);
    }

    private void AddTri(int n0, int n1, int n2, bool flip)
    {
        if (!flip)
        {
            _indices.Add(n0);
            _indices.Add(n1);
            _indices.Add(n2);
        }
        else
        {
            _indices.Add(n1);
            _indices.Add(n0);
            _indices.Add(n2);
        }
    }

    public void Dispose()
    {
        if (_verts.IsCreated) _verts.Dispose();
        if (_normals.IsCreated) _normals.Dispose();
        if (_colors.IsCreated) _colors.Dispose();
        if (_indices.IsCreated) _indices.Dispose();
        if (_innerEdgeNodes.IsCreated) _innerEdgeNodes.Dispose();
        if (_ringEdgeNodes.IsCreated) _ringEdgeNodes.Dispose();
        if (_meshChunk.nodes.IsCreated) _meshChunk.Dispose();
    }
}