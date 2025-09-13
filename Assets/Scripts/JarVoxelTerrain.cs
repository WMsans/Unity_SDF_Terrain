using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

/// <summary>
/// C# translation of the JarVoxelTerrain Godot C++ class for Unity.
/// Manages the generation, modification, and level-of-detail of a voxel terrain.
/// </summary>
public class JarVoxelTerrain : MonoBehaviour
{
    [Header("Core Settings")]
    public Transform playerNode;
    public JarWorld worldNode;
    [Tooltip("The base scale of the octree nodes.")]
    public float octreeScale = 1.0f;
    [Tooltip("The total size of the terrain volume, expressed as a power of 2.")]
    public int size = 14;
    [Tooltip("The minimum chunk size, expressed as a power of 2 (e.g., 4 means 16x16x16 chunks).")]
    public int minChunkSize = 4;
    [Tooltip("The prefab used to instantiate a terrain chunk.")]
    public GameObject chunkScene; // Equivalent to Godot's PackedScene
    [Tooltip("The Signed Distance Field resource defining the base terrain shape.")]
    public IJarSignedDistanceField sdf;
    [Tooltip("Whether to generate cubic-style voxels.")]
    public bool cubicVoxels = false;

    [Header("Performance")]
    [Tooltip("The maximum number of concurrent mesh generation tasks.")]
    public int maxConcurrentTasks = 12;
    [Tooltip("The maximum number of chunk colliders to update per second.")]
    public int updatedCollidersPerSecond = 128;

    [Header("Level Of Detail")]
    [Tooltip("The total number of LOD levels.")]
    public int lodLevelCount = 20;
    [Tooltip("The distance, in chunks, that each LOD level should extend.")]
    public int lodShellSize = 2;
    [Tooltip("Enable automatic LOD updates based on player position.")]
    public bool lodAutomaticUpdate = true;
    [Tooltip("The minimum distance the player must move to trigger an LOD update.")]
    public float lodAutomaticUpdateDistance = 64.0f;

    // --- Private Fields ---
    private MeshComputeScheduler _meshComputeScheduler;
    private List<float> _voxelEpsilons = new List<float>();
    private VoxelOctreeNode _voxelRoot;
    private JarVoxelLoD _voxelLod;

    private readonly Queue<ModifySettings> _modifySettingsQueue = new Queue<ModifySettings>();
    private readonly Queue<VoxelOctreeNode> _updateChunkCollidersQueue = new Queue<VoxelOctreeNode>();

    private volatile bool _isBuilding = false;
    private int _chunkSize = 0;

    // --- Unity MonoBehaviour Methods ---

    /// <summary>
    /// Corresponds to Godot's NOTIFICATION_ENTER_TREE. Initializes the terrain system.
    /// </summary>
    private void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// Corresponds to Godot's NOTIFICATION_INTERNAL_PROCESS. Runs per-frame logic.
    /// </summary>
    private void Update()
    {
        Process();
    }

    // --- Public API Methods ---

    /// <summary>
    /// Modifies the terrain using a generic SDF.
    /// </summary>
    public void Modify(IJarSignedDistanceField modifySdf, SDF.Operation operation, Vector3 position, float radius)
    {
        // Note: The original C++ created a new JarSphereSdf here.
        // This translation assumes 'modifySdf' is already configured.
        var edge = Vector3.one * radius;
        _modifySettingsQueue.Enqueue(new ModifySettings
        {
            Sdf = modifySdf,
            Bounds = new Bounds(position, edge * 2.0f),
            Position = position,
            Operation = operation
        });
    }

    /// <summary>
    /// Modifies the terrain with a sphere shape (additive or subtractive).
    /// </summary>
    public void SphereEdit(Vector3 worldPosition, float radius, bool operationIsUnion)
    {
        if (_isBuilding) return;

        // Convert world position to this node's local space
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        var operation = operationIsUnion ? SDF.Operation.SDF_OPERATION_UNION : SDF.Operation.SDF_OPERATION_SUBTRACTION;

        // In C#, you typically use ScriptableObject.CreateInstance for this pattern.
        var sphereSdf = ScriptableObject.CreateInstance<JarSphereSdf>();
        sphereSdf.SetRadius(radius);

        var edge = Vector3.one * (radius + octreeScale * 2.0f);

        ModifySettings settings = new ModifySettings
        {
            Sdf = sphereSdf,
            Bounds = new Bounds(localPosition, edge * 2.0f),
            Position = localPosition,
            Operation = operation
        };
        _voxelRoot.ModifySdfInBounds(this, settings);
    }

    /// <summary>
    /// Spawns simple spheres within a given area to visualize the octree leaf nodes.
    /// </summary>
    public void SpawnDebugSpheresInBounds(Vector3 position, float range)
    {
        List<VoxelOctreeNode> nodes = new List<VoxelOctreeNode>();
        var bounds = new Bounds(position, Vector3.one * range * 2.0f);
        GetVoxelLeavesInBounds(bounds, nodes);

        Material redMaterial = new Material(Shader.Find("Standard")) { color = Color.red };
        Material greenMaterial = new Material(Shader.Find("Standard")) { color = Color.green };

        foreach (var n in nodes)
        {
            GameObject sphereInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereInstance.transform.SetParent(this.transform);
            sphereInstance.transform.localPosition = n._center;
            sphereInstance.transform.localScale = Vector3.one * 0.2f;

            var renderer = sphereInstance.GetComponent<MeshRenderer>();
            renderer.material = (n.GetValue() > 0) ? greenMaterial : redMaterial;
            Destroy(sphereInstance.GetComponent<SphereCollider>()); // Remove collider
        }
    }

    /// <summary>
    /// Forces the LOD system to re-evaluate the camera's position and rebuild chunks if needed.
    /// </summary>
    public void ForceUpdateLod()
    {
        if (_voxelLod.UpdateCameraPosition(this, true))
            Build();
    }

    /// <summary>
    /// Enqueues a chunk's node for collider generation.
    /// </summary>
    public void EnqueueChunkCollider(VoxelOctreeNode node)
    {
        if (node != null)
        {
            _updateChunkCollidersQueue.Enqueue(node);
        }
    }

    /// <summary>
    /// Enqueues a chunk's node for mesh generation.
    /// </summary>
    public void EnqueueChunkUpdate(VoxelOctreeNode node)
    {
        _meshComputeScheduler.Enqueue(node);
    }

    // --- Public Getters / Properties ---
    public bool IsBuilding() => _isBuilding;
    public int GetChunkSize() => _chunkSize;

    // --- Octree Query Methods ---
    public void GetVoxelLeavesInBounds(Bounds bounds, List<VoxelOctreeNode> nodes) => _voxelRoot.GetVoxelLeavesInBounds(this, bounds, nodes);
    public void GetVoxelLeavesInBounds(Bounds bounds, int lod, List<VoxelOctreeNode> nodes) => _voxelRoot.GetVoxelLeavesInBounds(this, bounds, lod, nodes);
    public void GetVoxelLeavesInBoundsExcludingBounds(Bounds bounds, Bounds excludeBounds, int lod, List<VoxelOctreeNode> nodes) => _voxelRoot.GetVoxelLeavesInBoundsExcludingBounds(this, bounds, excludeBounds, lod, nodes);
    
    // --- LOD Accessors ---
    public Vector3 GetCameraPosition() => _voxelLod.GetCameraPosition();
    public int DesiredLod(VoxelOctreeNode node) => _voxelLod.DesiredLod(node);
    public int LodAt(Vector3 position) => _voxelLod.LodAt(position);

    // --- Private Implementation ---

    private void Initialize()
    {
        // Don't run initialization logic in the editor when not in play mode
        if (Application.isEditor && !Application.isPlaying) return;

        if (chunkScene == null)
        {
            Debug.LogError("Chunk Scene prefab is not assigned.", this);
            return;
        }
        if (sdf == null)
        {
            Debug.LogError("SDF asset is not assigned.", this);
            return;
        }

        _chunkSize = 1 << minChunkSize;
        _voxelLod = new JarVoxelLoD(lodAutomaticUpdate, lodAutomaticUpdateDistance, lodLevelCount, lodShellSize, octreeScale);
        _meshComputeScheduler = new MeshComputeScheduler(maxConcurrentTasks);
        _voxelRoot = new VoxelOctreeNode(size);

        Build();
    }

    private void Process()
    {
        if (Application.isEditor && !Application.isPlaying) return;
        
        float delta = Time.deltaTime;
        
        if (!_isBuilding && !_meshComputeScheduler.IsMeshing() && _voxelLod.Process(this, false))
            Build();
            
        _meshComputeScheduler.Process(this);

        if (_modifySettingsQueue.Count > 0)
        {
            ProcessModifyQueue();
        }

        ProcessChunkQueue(delta);
    }

    private void Build()
    {
        if (_isBuilding || (_meshComputeScheduler != null && _meshComputeScheduler.IsMeshing()))
            return;

        // Run the build process on a background thread to avoid freezing the main thread.
        Task.Run(() => {
            _isBuilding = true;
            try
            {
                _voxelRoot.Build(this);
            }
            finally
            {
                _isBuilding = false;
            }
        });
    }

    private void ProcessChunkQueue(float delta)
    {
        if (_updateChunkCollidersQueue.Count == 0) return;

        int rate = Math.Max(1, (int)Math.Ceiling(updatedCollidersPerSecond * delta));
        int target = Math.Min(rate, _updateChunkCollidersQueue.Count);

        int processed = 0;
        while (processed < target && _updateChunkCollidersQueue.Count > 0)
        {
            VoxelOctreeNode node = _updateChunkCollidersQueue.Dequeue();
            if (node == null) continue;

            JarVoxelChunk chunk = node.GetChunk();
            if (chunk != null)
            {
                chunk.UpdateCollisionMesh();
                processed++;
            }
        }
    }

    private void GenerateEpsilons()
    {
        int numElements = size + 1;
        _voxelEpsilons.Clear();
        _voxelEpsilons.Capacity = numElements;

        List<int> sizes = new List<int>(numElements);
        for (int i = 0; i < numElements; i++)
        {
            sizes.Add(1 << i);
        }

        for (int i = 0; i < sizes.Count; i++)
        {
            int s = sizes[i];
            float x = s * octreeScale;
            _voxelEpsilons.Add(1.75f * x);
        }
    }

    private void ProcessModifyQueue()
    {
        if (_isBuilding) return;

        _isBuilding = true;
        
        // This is kept synchronous to avoid race conditions with other processes.
        // The original C++ code used a detached thread, which can be risky.
        try
        {
            if (_modifySettingsQueue.Count > 0)
            {
                var settings = _modifySettingsQueue.Dequeue();
                _voxelRoot.ModifySdfInBounds(this, settings);
            }
        }
        finally
        {
            _isBuilding = false;
        }
    }
}