using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

[BurstCompile]
public unsafe struct VoxelOctreeNode
{
    // --- FIELDS ---
    public VoxelOctreeNodePointer _parent;
    public float3 _center;
    public int _size;
    public VoxelOctreeNode* _children;

    public byte _isMaterialized;
    public int LoD;
    private float _value;
    private bool _isSet;
    public float4 NodeColor;
    private bool _isDirty;
    public bool isChunk;
    public ushort boundaries;
    private bool _isEnqueued;

    private static readonly float3[] _boundaryOffsets =
    {
        new float3(1, 0, 0), new float3(-1, 0, 0),
        new float3(0, 1, 0), new float3(0, -1, 0),
        new float3(0, 0, 1), new float3(0, 0, -1)
    };

    // --- INITIALIZATION & MEMORY MANAGEMENT ---

    public VoxelOctreeNode(int size)
    {
        _parent = new VoxelOctreeNodePointer();
        _center = float3.zero;
        _size = size;
        _children = null;
        _isMaterialized = 0;
        isChunk = false;
        boundaries = 0;
        _isEnqueued = false;
        _isDirty = true;
        LoD = 0;
        _value = 0;
        _isSet = false;
        NodeColor = float4.zero;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsLeaf() => _children == null;

    public void Subdivide(float octreeScale, Allocator allocator)
    {
        if (!IsLeaf()) return;

        _children = (VoxelOctreeNode*)UnsafeUtility.Malloc(
            sizeof(VoxelOctreeNode) * 8,
            UnsafeUtility.AlignOf<VoxelOctreeNode>(),
            allocator
        );

        int childSize = _size - 1;
        float childOffset = octreeScale * (1 << childSize);

        for (int i = 0; i < 8; i++)
        {
            float3 childCenter = _center + new float3(
                (i & 1) == 0 ? -childOffset : childOffset,
                (i & 2) == 0 ? -childOffset : childOffset,
                (i & 4) == 0 ? -childOffset : childOffset
            );
            fixed(VoxelOctreeNode* thisPtr = &this)
            {
                _children[i] = new VoxelOctreeNode
                {
                    _size = childSize,
                    _parent = new VoxelOctreeNodePointer() { Value = thisPtr },
                    _center = childCenter,
                    LoD = this.LoD,
                    _value = this.GetValue(),
                    _isSet = this._isSet,
                    NodeColor = this.NodeColor,
                    _children = null,
                    _isMaterialized = 0,
                    isChunk = false,
                    boundaries = 0,
                    _isEnqueued = false,
                    _isDirty = true
                };
            }
        }
    }

    public void PruneChildren(Allocator allocator)
    {
        if (IsLeaf()) return;

        for (int i = 0; i < 8; i++)
        {
            _children[i].PruneChildren(allocator);
        }

        UnsafeUtility.Free(_children, allocator);
        _children = null;
    }

    // --- PUBLIC METHODS ---

    public int Priority() => LoD;
    public bool IsDirty() => _isDirty;

    public void SetDirty(bool value)
    {
        if (!_isDirty && value && _parent.Value != null)
        {
            _parent.Value->SetDirty(true);
        }
        _isDirty = value;
    }

    public float GetValue()
    {
        if (!IsDirty()) return _value;

        if (!IsLeaf())
        {
            _value = 0;
            NodeColor = float4.zero;
            for (int i = 0; i < 8; i++)
            {
                _value += _children[i].GetValue();
                NodeColor += _children[i].NodeColor;
            }
            _value *= 0.125f;
            NodeColor *= 0.125f;
        }
        _isDirty = false;
        return _value;
    }

    public int GetLoD() => LoD;
    public float4 GetColor() => NodeColor;

    public void SetValue(float value)
    {
        _value = value;
        _isDirty = false;
        if (_parent.Value != null)
        {
            _parent.Value->SetDirty(true);
        }
    }

    public void MarkMaterialized()
    {
        if (IsMaterialized()) return;

        if (IsLeaf())
        {
            _isMaterialized = 0b11111111;
        }
        else
        {
            for (int i = 0; i < 8; i++)
            {
                if (_children[i].IsMaterialized())
                {
                    _isMaterialized |= (byte)(1 << i);
                }
            }
        }

        if (_parent.Value != null && IsMaterialized())
        {
            _parent.Value->MarkMaterialized();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMaterialized() => _isMaterialized == 0b11111111;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsChunk(ref TerrainData terrain) => _size == (LoD + terrain.minChunkSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAboveChunk(ref TerrainData terrain) => _size > (LoD + terrain.minChunkSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAboveMinChunk(ref TerrainData terrain) => _size > (terrain.minChunkSize);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsOneAboveChunk(ref TerrainData terrain) => _size == (LoD + terrain.minChunkSize + 1);

    public void PopulateUniqueLoDValues(ref NativeList<int> lodValues)
    {
        if (!lodValues.Contains(LoD))
        {
            lodValues.Add(LoD);
        }
        if (IsLeaf()) return;

        for (int i = 0; i < 8; i++)
        {
            _children[i].PopulateUniqueLoDValues(ref lodValues);
        }
    }

    public bool IsEnqueued() => _isEnqueued;

    public void FinishedMeshingNotifyParentAndChildren(NativeQueue<ChunkDeleteRequest>.ParallelWriter chunkDeleteQueue)
    {
        if (_parent.Value != null)
        {
            _parent.Value->RequestDeleteChunk(chunkDeleteQueue);
        }
        if (!IsLeaf())
        {
            for (int i = 0; i < 8; i++)
            {
                _children[i].RequestDeleteChunk(chunkDeleteQueue);
            }
        }
    }
    
    public bool IsParentEnqueued() => _parent.Value != null && _parent.Value->IsEnqueued();

    public bool IsAnyChildrenEnqueued()
    {
        if (IsLeaf()) return false;
        for (int i = 0; i < 8; i++)
        {
            if (_children[i].IsEnqueued()) return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldDeleteChunk() => false;
    
    public ushort ComputeBoundaries(ref TerrainData terrain)
    {
        ushort newBoundaries = 0;
        float el = EdgeLength(terrain.octreeScale);
        for (int i = 0; i < _boundaryOffsets.Length; i++)
        {
            int l = terrain.lod.LodAt(_center + el * _boundaryOffsets[i]);
            if (LoD < l) newBoundaries |= (ushort)(1 << i);
            if (LoD > l) newBoundaries |= (ushort)(1 << (i + 8));
        }
        return newBoundaries;
    }

    public void Build(ref TerrainData terrain, Allocator allocator, NativeQueue<VoxelOctreeNodePointer>.ParallelWriter mainThreadUpdates, NativeQueue<ChunkDeleteRequest>.ParallelWriter chunkDeleteQueue)
    {
        var stack = new NativeList<VoxelOctreeNodePointer>(128, allocator);
        
        fixed(VoxelOctreeNode* thisPtr = &this)
        {
            stack.Add(new VoxelOctreeNodePointer(){Value = thisPtr});
        }

        while(stack.Length > 0)
        {
            var node = stack[^1].Value;
            stack.RemoveAt(stack.Length - 1);
            
            node->LoD = terrain.lod.DesiredLod(*node);
            if (node->LoD < 0) continue;

            if (!node->_isSet)
            {
                float value = SdfUtils.Distance(terrain.sdf, node->_center);
                node->SetValue(value);

                if (node->HasSurface(ref terrain, value) && (node->_size > node->LoD))
                {
                    node->Subdivide(terrain.octreeScale, allocator);
                    node->_isSet = true;
                }
                if (node->IsLeaf() && (node->_size > node->LoD || node->_size == terrain.minChunkSize))
                {
                    node->_isSet = true;
                    node->MarkMaterialized();
                    continue;
                }
            }

            if (node->IsChunk(ref terrain))
            {
                ushort newBoundaries = node->ComputeBoundaries(ref terrain);
                if (!node->isChunk || node->boundaries != newBoundaries)
                {
                    node->boundaries = newBoundaries;
                    mainThreadUpdates.Enqueue(new VoxelOctreeNodePointer() { Value = node });
                }
                node->isChunk = true;
            }
            else
            {
                node->isChunk = false;
            }


            if (!node->IsLeaf() && !node->isChunk && (!node->IsMaterialized() || node->IsAboveMinChunk(ref terrain)))
            {
                for (int i = 0; i < 8; i++)
                {
                    stack.Add(new VoxelOctreeNodePointer(){Value = &node->_children[i]});
                }
            }

            if (!node->IsChunk(ref terrain))
            {
                node->RequestDeleteChunk(chunkDeleteQueue);
            }
        }
    }

    public bool HasSurface(ref TerrainData terrain, float value)
    {
        return math.abs(value) < (1 << _size) * terrain.octreeScale * 1.44224957f * 1.75f;
    }

    public void ModifySdfInBounds(ref TerrainData terrain, in ModifySettings settings, Allocator allocator, NativeQueue<VoxelOctreeNodePointer>.ParallelWriter mainThreadUpdates, NativeQueue<ChunkDeleteRequest>.ParallelWriter chunkDeleteQueue)
    {
        var stack = new NativeList<VoxelOctreeNodePointer>(128, allocator);
        fixed (VoxelOctreeNode* thisPtr = &this)
        {
            stack.Add(new VoxelOctreeNodePointer(){Value = thisPtr});
        }

        while (stack.Length > 0)
        {
            var node = stack[stack.Length - 1].Value;
            stack.RemoveAt(stack.Length - 1);

            var bounds = node->GetBounds(terrain.octreeScale);
            if (!settings.Bounds.Intersects(bounds)) continue;

            node->LoD = terrain.lod.DesiredLod(*node);
            if (!node->_isSet)
            {
                node->SetValue(SdfUtils.Distance(terrain.sdf, node->_center));
            }

            float oldValue = node->GetValue();
            float sdfValue = SdfUtils.Distance(settings.Sdf, node->_center - settings.Position);
            float newValue = SdfData.ApplyOperation(settings.Operation, oldValue, sdfValue);

            if (node->HasSurface(ref terrain, newValue))
            {
                node->Subdivide(terrain.octreeScale, allocator);
            }
            else if (settings.Bounds.Contains(bounds.min) && settings.Bounds.Contains(bounds.max))
            {
                node->PruneChildren(allocator);
            }

            node->SetValue(newValue);
            node->_isSet = true;
            if (math.abs(newValue - oldValue) > 0.01f)
            {
                node->NodeColor = new float4(1, 0, 0, 1);
            }

            if (node->IsLeaf())
            {
                node->MarkMaterialized();
            }
            else
            {
                for (int i = 0; i < 8; i++)
                {
                    stack.Add(new VoxelOctreeNodePointer(){Value = &node->_children[i]});
                }
            }

            if (node->IsChunk(ref terrain))
            {
                mainThreadUpdates.Enqueue(new VoxelOctreeNodePointer(){Value = node});
            }
            else if (node->isChunk)
            {
                node->RequestDeleteChunk(chunkDeleteQueue);
            }
        }
    }
    
    public void RequestDeleteChunk(NativeQueue<ChunkDeleteRequest>.ParallelWriter chunkDeleteQueue)
    {
        if (IsAnyChildrenEnqueued() || IsParentEnqueued()) return;

        if (isChunk)
        {
            fixed (VoxelOctreeNode* thisPtr = &this)
            {
                chunkDeleteQueue.Enqueue(new ChunkDeleteRequest
                    { chunk = new VoxelOctreeNodePointer { Value = thisPtr } });
            }
        }
        isChunk = false;
    }

    public float EdgeLength(float octreeScale)
    {
        return octreeScale * (1 << _size);
    }

    public BurstBounds GetBounds(float octreeScale)
    {
        float edge = EdgeLength(octreeScale);
        return new BurstBounds { center = _center, size = new float3(edge) };
    }
    
    public void GetVoxelLeavesInBounds(in TerrainData terrain, BurstBounds bounds, NativeList<VoxelOctreeNode> nodes, int lod = -1, BurstBounds? excludeBounds = null)
    {
        var stack = new NativeList<VoxelOctreeNodePointer>(128, Allocator.Temp);
        fixed (VoxelOctreeNode* thisPtr = &this)
        {
            stack.Add(new VoxelOctreeNodePointer(){Value = thisPtr});
        }

        while (stack.Length > 0)
        {
            var node = stack[^1].Value;
            stack.RemoveAt(stack.Length - 1);

            if (!node->GetBounds(terrain.octreeScale).Intersects(bounds))
            {
                continue;
            }

            if (excludeBounds.HasValue)
            {
                BurstBounds exclude = excludeBounds.Value;
                if(node->GetBounds(terrain.octreeScale).Intersects(exclude))
                {
                    continue;
                }
            }

            if (node->IsLeaf())
            {
                if (lod == -1 || node->LoD == lod)
                {
                    nodes.Add(*node);
                }
            }
            else
            {
                for (int i = 0; i < 8; i++)
                {
                    stack.Add(new  VoxelOctreeNodePointer(){Value = &node->_children[i]});
                }
            }
        }
    }

    public void GetVoxelLeavesInBounds(JarVoxelTerrain terrain, BurstBounds bounds, NativeList<VoxelOctreeNode> nodes, int lod = -1, BurstBounds? excludeBounds = null)
    {
        var terrainData = terrain.GetTerrainData();
        GetVoxelLeavesInBounds(in terrainData, bounds, nodes, lod, excludeBounds);
    }
}