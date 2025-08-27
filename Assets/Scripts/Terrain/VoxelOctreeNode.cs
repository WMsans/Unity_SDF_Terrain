using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class VoxelOctreeNode
{
    private VoxelOctreeNode _parent;
    private VoxelOctreeNode[] _children;
    private float3 _center;
    private int _size;
    private float _value;
    private bool _isSet;
    private bool _isDirty;

    public int LoD { get; private set; }

    public VoxelOctreeNode(int size) : this(null, float3.zero, size) { }

    public VoxelOctreeNode(VoxelOctreeNode parent, float3 center, int size)
    {
        _parent = parent;
        _center = center;
        _size = size;

        if (_parent != null)
        {
            LoD = _parent.LoD;
            _value = _parent.GetValue();
            _isSet = _parent._isSet;
        }
    }

    public bool IsLeaf() => _children == null;

    public float GetValue()
    {
        if (!_isDirty) return _value;

        if (!IsLeaf())
        {
            _value = 0;
            foreach (var child in _children)
            {
                _value += child.GetValue();
            }
            _value *= 0.125f; // Average of children
        }
        _isDirty = false;
        return _value;
    }
    
    public void SetValue(float value)
    {
        _value = value;
        _isDirty = false;
        _parent?.SetDirty(true);
    }
    
    private void SetDirty(bool value)
    {
        if (!_isDirty && value && _parent != null)
        {
            _parent.SetDirty(true);
        }
        _isDirty = value;
    }

    private void Subdivide(float octreeScale)
    {
        if (!IsLeaf()) return;

        _children = new VoxelOctreeNode[8];
        int childSize = _size - 1;
        float offset = (1 << childSize) * octreeScale;

        for (int i = 0; i < 8; i++)
        {
            float3 childCenter = _center + new float3(
                (i & 1) == 0 ? -offset : offset,
                (i & 2) == 0 ? -offset : offset,
                (i & 4) == 0 ? -offset : offset
            );
            _children[i] = new VoxelOctreeNode(this, childCenter, childSize);
        }
    }
    
    private void PruneChildren()
    {
        if (IsLeaf()) return;
        _children = null;
    }

    public void Build(VoxelTerrain terrain)
    {
        // Placeholder for LOD logic
        LoD = 0; 

        if (!_isSet)
        {
            float value = terrain.Sdf.Distance(_center);
            SetValue(value);
            if (HasSurface(terrain, value) && (_size > LoD))
            {
                Subdivide(terrain.OctreeScale);
                _isSet = true;
            }
        }
        
        if (!IsLeaf())
        {
            foreach (var child in _children)
            {
                child.Build(terrain);
            }
        }
    }
    
    private bool HasSurface(VoxelTerrain terrain, float value)
    {
        return math.abs(value) < (1 << _size) * terrain.OctreeScale * 1.44224957f * 1.75f;
    }
    
    public void ModifySdfInBounds(VoxelTerrain terrain, ModifySettings settings)
    {
        var bounds = GetBounds(terrain.OctreeScale);
        if (!settings.Bounds.Intersects(bounds)) return;
        
        LoD = 0; // Placeholder for LOD logic
        if (!_isSet) SetValue(terrain.Sdf.Distance(_center));

        float oldValue = GetValue();
        float sdfValue = settings.Sdf.Distance(_center - settings.Position);
        float newValue = SdfOperations.ApplyOperation(settings.Operation, oldValue, sdfValue, terrain.OctreeScale);

        if (HasSurface(terrain, newValue))
        {
             Subdivide(terrain.OctreeScale);
        }
        else
        {
            if (settings.Bounds.Contains(bounds.min) && settings.Bounds.Contains(bounds.max))
            {
                PruneChildren();
            }
        }
        
        SetValue(newValue);
        _isSet = true;

        if (!IsLeaf())
        {
            foreach (var child in _children)
            {
                child.ModifySdfInBounds(terrain, settings);
            }
        }
    }
    
    private Bounds GetBounds(float octreeScale)
    {
        float extent = (1 << _size) * octreeScale * 0.5f;
        var unityCenter = new Vector3(_center.x, _center.y, _center.z);
        return new Bounds(unityCenter, Vector3.one * extent * 2f);
    }
}