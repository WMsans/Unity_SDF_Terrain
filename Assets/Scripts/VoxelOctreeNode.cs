using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

/// <summary>
/// A C# struct translation of the VoxelOctreeNode C++ class, designed for use with the Unity Burst Compiler.
/// This struct manages its own children in unmanaged memory and must be handled with care.
/// </summary>
[BurstCompile]
public unsafe struct VoxelOctreeNode
{
    // --- FIELDS ---
    // Fields from the original OctreeNode base class
    public VoxelOctreeNode* _parent;
    public float3 _center;
    public int _size;
    public VoxelOctreeNode* _children; // Pointer to a block of 8 children

    // Fields from VoxelOctreeNode
    public byte _isMaterialized;
    public int LoD;
    private float _value;
    private bool _isSet;
    public float4 NodeColor;
    private bool _isDirty;
    public void* _chunk; // Opaque pointer to JarVoxelChunk
    private bool _isEnqueued;

    // A static array to replace the std::vector<glm::vec3> in compute_boundaries
    private static readonly float3[] _boundaryOffsets =
    {
        new float3(1, 0, 0), new float3(-1, 0, 0),
        new float3(0, 1, 0), new float3(0, -1, 0),
        new float3(0, 0, 1), new float3(0, 0, -1)
    };

    // --- INITIALIZATION & MEMORY MANAGEMENT ---

    /// <summary>
    /// Initializes a VoxelOctreeNode. This acts as the constructor.
    /// </summary>
    public void Initialize(VoxelOctreeNode* parent, float3 center, int size)
    {
        _parent = parent;
        _center = center;
        _size = size;
        _children = null;
        _isMaterialized = 0;
        _chunk = null;
        _isEnqueued = false;
        _isDirty = true;

        if (_parent != null)
        {
            LoD = _parent->LoD;
            _value = _parent->GetValue();
            _isSet = _parent->_isSet;
            NodeColor = _parent->NodeColor;
        }
        else
        {
            LoD = 0;
            _value = 0;
            _isSet = false;
            NodeColor = float4.zero;
        }
    }

    /// <summary>
    /// Checks if this node is a leaf (has no children).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsLeaf() => _children == null;

    /// <summary>
    /// Subdivides the node, creating 8 children.
    /// </summary>
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
            _children[i].Initialize(this, childCenter, childSize);
        }
    }

    /// <summary>
    /// Prunes all children of this node recursively.
    /// </summary>
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
        if (!_isDirty && value && _parent != null)
        {
            _parent->SetDirty(true);
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
        if (_parent != null)
        {
            _parent->SetDirty(true);
        }
    }

    /// <summary>
    /// Marks the node as materialized if it or all its children are.
    /// </summary>
    public void MarkMaterialized()
    {
        if (IsMaterialized()) return;

        if (IsLeaf())
        {
            _isMaterialized = 0b11111111;
        }
        else
        {
            // Note: The original C++ code contained a likely bug where it would only set the first bit.
            // This implementation assumes the intent was to set a bit mask for all children.
            for (int i = 0; i < 8; i++)
            {
                if (_children[i].IsMaterialized())
                {
                    _isMaterialized |= (byte)(1 << i);
                }
            }
        }

        if (_parent != null && IsMaterialized())
        {
            _parent->MarkMaterialized();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMaterialized() => _isMaterialized == 0b11111111;
    public void* GetChunk() => _chunk;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsChunk(in JarVoxelTerrain terrain) => _size == (LoD + terrain.GetChunkSize());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAboveChunk(in JarVoxelTerrain terrain) => _size > (LoD + terrain.GetChunkSize());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAboveMinChunk(in JarVoxelTerrain terrain) => _size > (terrain.GetChunkSize());
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsOneAboveChunk(in JarVoxelTerrain terrain) => _size == (LoD + terrain.GetChunkSize() + 1);

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

    public void FinishedMeshingNotifyParentAndChildren()
    {
        if (_parent != null)
        {
            _parent->DeleteChunk();
        }
        if (!IsLeaf())
        {
            for (int i = 0; i < 8; i++)
            {
                _children[i].DeleteChunk();
            }
        }
    }
    
    public bool IsParentEnqueued() => _parent == null ? false : _parent->IsEnqueued();

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
    public bool ShouldDeleteChunk(in JarVoxelTerrain terrain) => false;
    
    public ushort ComputeBoundaries(in JarVoxelTerrain terrain)
    {
        ushort boundaries = 0;
        float el = EdgeLength(terrain.octreeScale);
        for (int i = 0; i < _boundaryOffsets.Length; i++)
        {
            int l = terrain.LodAt(_center + el * _boundaryOffsets[i]);
            if (LoD < l) boundaries |= (ushort)(1 << i);       // High to low
            if (LoD > l) boundaries |= (ushort)(1 << (i + 8)); // Low to high
        }
        return boundaries;
    }

    public void Build(ref JarVoxelTerrain terrain, Allocator allocator)
    {
        LoD = terrain.DesiredLod(this);
        if (LoD < 0) return;

        if (!_isSet)
        {
            float value = terrain.get_sdf()->distance(_center);
            SetValue(value);
            if (HasSurface(in terrain, value) && (_size > LoD))
            {
                Subdivide(terrain.octreeScale, allocator);
                _isSet = true;
            }
            if (IsLeaf() && (_size > LoD || _size == terrain.min_size()))
            {
                _isSet = true;
                MarkMaterialized();
                return;
            }
        }

        if (IsChunk(in terrain) && !IsLeaf() && (_chunk == null || (_chunk->get_boundaries() != ComputeBoundaries(in terrain))))
        {
            QueueUpdate(ref terrain);
        }

        if (!IsLeaf() && !(IsChunk(in terrain) && (_chunk != null)) && (!IsMaterialized() || IsAboveMinChunk(in terrain)))
        {
            for (int i = 0; i < 8; i++)
            {
                _children[i].Build(ref terrain, allocator);
            }
        }

        if (!IsChunk(in terrain))
        {
            DeleteChunk();
        }
    }

    public bool HasSurface(in JarVoxelTerrain terrain, float value)
    {
        return math.abs(value) < (1 << _size) * terrain.octreeScale * 1.44224957f * 1.75f;
    }

    public void ModifySdfInBounds(ref JarVoxelTerrain terrain, in ModifySettings settings, Allocator allocator)
    {
        if (settings.sdf == null) return;
        
        var bounds = GetBounds(terrain.get_octree_scale());
        if (!settings.bounds.intersects(bounds)) return;

        LoD = terrain.desired_lod(this);
        if (!_isSet) SetValue(terrain.get_sdf()->distance(_center));

        float oldValue = GetValue();
        float sdfValue = settings.sdf->distance(_center - settings.position);
        float newValue = SDF.apply_operation(settings.operation, oldValue, sdfValue, terrain.get_octree_scale());

        if (HasSurface(in terrain, newValue))
        {
            Subdivide(terrain.get_octree_scale(), allocator);
        }
        else if (settings.bounds.encloses(bounds))
        {
            PruneChildren(allocator);
        }
        
        SetValue(newValue);
        _isSet = true;
        if (math.abs(newValue - oldValue) > 0.01f)
        {
            NodeColor = new float4(1, 0, 0, 1);
        }

        if (IsLeaf())
        {
            MarkMaterialized();
        }
        else
        {
            for (int i = 0; i < 8; i++)
            {
                _children[i].ModifySdfInBounds(ref terrain, in settings, allocator);
            }
        }

        if (IsChunk(in terrain))
        {
            QueueUpdate(ref terrain);
        }
        else if (_chunk != null)
        {
            DeleteChunk();
        }
    }
    
    public void UpdateChunk(ref JarVoxelTerrain terrain, ChunkMeshData* chunkMeshData)
    {
        _isEnqueued = false;
        FinishedMeshingNotifyParentAndChildren();
        if (chunkMeshData == null || !IsChunk(in terrain))
        {
            DeleteChunk();
            return;
        }

        if (_chunk == null)
        {
            _chunk = (void*)terrain.get_chunk_scene()->instantiate();
            terrain.add_child(_chunk);
        }
        
        // This method relies on external, non-translatable class behavior
        // _chunk->update_chunk(terrain, this, chunkMeshData);
    }
    
    public void QueueUpdate(ref JarVoxelTerrain terrain)
    {
        if (_isEnqueued) return;
        _isEnqueued = true;
        terrain.enqueue_chunk_update(this);
    }

    public void DeleteChunk()
    {
        if (IsAnyChildrenEnqueued() || IsParentEnqueued()) return;

        if (_chunk != null)
        {
            // This requires an interface to the managed world (e.g., via function pointers)
            // _chunk->queue_free(); 
        }
        _chunk = null;
    }

    // --- UTILITY & HELPER METHODS (from OctreeNode) ---

    public float EdgeLength(float octreeScale)
    {
        return octreeScale * (1 << _size);
    }

    public BurstBounds GetBounds(float octreeScale)
    {
        float edge = EdgeLength(octreeScale);
        return new BurstBounds { center = _center, extents = new float3(edge * 0.5f) };
    }
}