using UnityEngine;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using System;
using UnityEngine.Rendering;

/// <summary>
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
    public JarVoxelChunkComponent chunkScene;
    [Tooltip("The Signed Distance Field resource defining the base terrain shape.")]
    public SdfData sdf;

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
    private NativeList<float> _voxelEpsilons;
    private VoxelOctreeNode _voxelRoot;
    private JarVoxelLod _voxelLod;

    private NativeQueue<ModifySettings> _modifySettingsQueue;
    private NativeQueue<VoxelOctreeNode> _updateChunkCollidersQueue;
    private NativeQueue<ChunkDeleteRequest> _chunkDeleteQueue;

    private Dictionary<long, JarVoxelChunkComponent> _activeChunks = new Dictionary<long, JarVoxelChunkComponent>();
    private Queue<JarVoxelChunkComponent> _chunkPool = new Queue<JarVoxelChunkComponent>();

    private bool _isBuilding = false;

    // --- Unity MonoBehaviour Methods ---

    private void Awake()
    {
        Initialize();
    }

    private void Update()
    {
        Process();
    }

    private void OnDestroy()
    {
        if (_voxelEpsilons.IsCreated) _voxelEpsilons.Dispose();
        if (_modifySettingsQueue.IsCreated) _modifySettingsQueue.Dispose();
        if (_updateChunkCollidersQueue.IsCreated) _updateChunkCollidersQueue.Dispose();
        if (_chunkDeleteQueue.IsCreated) _chunkDeleteQueue.Dispose();
        
        _meshComputeScheduler.Dispose();
        
        // Properly dispose of the voxel octree memory
        _voxelRoot.PruneChildren(Allocator.Persistent);

        foreach (var chunk in _activeChunks.Values)
        {
            Destroy(chunk.gameObject);
        }
        _activeChunks.Clear();

        foreach (var chunk in _chunkPool)
        {
            Destroy(chunk.gameObject);
        }
        _chunkPool.Clear();
    }

    // --- Public API Methods ---

    public void Modify(SdfData modifySdf, Operation operation, Vector3 position, float radius)
    {
        var edge = Vector3.one * radius;
        _modifySettingsQueue.Enqueue(new ModifySettings
        {
            Sdf = modifySdf,
            Bounds = new BurstBounds(position, edge * 2.0f),
            Position = position,
            Operation = operation
        });
    }

    public void SphereEdit(Vector3 worldPosition, float radius, bool operationIsUnion)
    {
        if (_isBuilding) return;

        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        var operation = operationIsUnion ? Operation.SDF_OPERATION_UNION : Operation.SDF_OPERATION_SUBTRACTION;

        var sphereSdf = SdfData.CreateSphere(Vector3.zero, radius);

        var edge = Vector3.one * (radius + octreeScale * 2.0f);

        ModifySettings settings = new ModifySettings
        {
            Sdf = sphereSdf,
            Bounds = new BurstBounds(localPosition, edge * 2.0f),
            Position = localPosition,
            Operation = operation
        };
        
        ProcessModifyJob(settings);
    }
    
    public void SpawnDebugSpheresInBounds(Vector3 position, float range)
    {
        NativeList<VoxelOctreeNode> nodes = new NativeList<VoxelOctreeNode>(Allocator.Temp);
        var bounds = new BurstBounds(position, Vector3.one * range * 2.0f);
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
        nodes.Dispose();
    }
    
    public void EnqueueChunkCollider(VoxelOctreeNode node)
    {
        _updateChunkCollidersQueue.Enqueue(node);
    }
    
    public unsafe void EnqueueChunkUpdate(VoxelOctreeNode* node)
    {
        _meshComputeScheduler.Enqueue(node);
    }

    // --- Public Getters / Properties ---
    public bool IsBuilding => _isBuilding;
    public int ChunkSize => 1 << minChunkSize;

    // --- Octree Query Methods ---
    public void GetVoxelLeavesInBounds(BurstBounds bounds, NativeList<VoxelOctreeNode> nodes, int lod = -1, BurstBounds? excludeBounds = null) => 
        _voxelRoot.GetVoxelLeavesInBounds(this, bounds, nodes, lod, excludeBounds);

    // --- LOD Accessors ---
    public float3 GetCameraPosition() => _voxelLod.CameraPosition;
    public int DesiredLod(VoxelOctreeNode node) => _voxelLod.DesiredLod(node);
    public int LodAt(float3 position) => _voxelLod.LodAt(position);

    // --- Private Implementation ---

    private void Initialize()
    {
        if (Application.isEditor && !Application.isPlaying) return;

        if (chunkScene == null)
        {
            Debug.LogError("Chunk Scene prefab is not assigned.", this);
            return;
        }
        if (sdf.Type == SdfType.None)
        {
            Debug.LogError("SDF asset is not assigned.", this);
            return;
        }

        _voxelLod = new JarVoxelLod(lodAutomaticUpdate, lodAutomaticUpdateDistance, lodLevelCount, lodShellSize, octreeScale);
        _meshComputeScheduler = new MeshComputeScheduler(maxConcurrentTasks);
        _voxelRoot = new VoxelOctreeNode(size);
        _voxelEpsilons = new NativeList<float>(size + 1, Allocator.Persistent);
        _modifySettingsQueue = new NativeQueue<ModifySettings>(Allocator.Persistent);
        _updateChunkCollidersQueue = new NativeQueue<VoxelOctreeNode>(Allocator.Persistent);
        _chunkDeleteQueue = new NativeQueue<ChunkDeleteRequest>(Allocator.Persistent);

        for (int i = 0; i < maxConcurrentTasks * 2; i++)
        {
            var chunkGO = Instantiate(chunkScene, transform);
            chunkGO.gameObject.SetActive(false);
            _chunkPool.Enqueue(chunkGO);
        }
        
        GenerateEpsilons();
        Build();
    }

    private void Process()
    {
        if (Application.isEditor && !Application.isPlaying) return;
        
        float delta = Time.deltaTime;
        
        if (!_isBuilding && !_meshComputeScheduler.IsMeshing() && _voxelLod.Process(playerNode.position, delta))
        {
            Build();
        }
            
        _meshComputeScheduler.Process(this);

        while (_meshComputeScheduler.TryGetResult(out MeshGenerationResult result))
        {
            unsafe
            {
                ApplyMeshToChunk(result.chunk, result.meshData);
            }
            result.Dispose();
        }


        if (_modifySettingsQueue.Count > 0)
        {
            if (_modifySettingsQueue.TryDequeue(out var settings))
            {
                ProcessModifyJob(settings);
            }
        }

        ProcessDeleteQueue();
    }
    
    private unsafe void ApplyMeshToChunk(VoxelOctreeNode* node, ChunkMeshData meshData)
    {
        long nodePtr = (long)node;
        if (!_activeChunks.TryGetValue(nodePtr, out JarVoxelChunkComponent chunk))
        {
            chunk = _chunkPool.Count > 0 ? _chunkPool.Dequeue() : Instantiate(chunkScene, transform);
            chunk.gameObject.SetActive(true);
            chunk.SetNode(node);
            _activeChunks.Add(nodePtr, chunk);
        }

        if (meshData.vertices.Length > 0 && meshData.indices.Length > 0)
        {
            var mesh = new Mesh();
            mesh.SetVertices(meshData.vertices);
            // Set triangles
            mesh.SetIndexBufferParams(meshData.indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(meshData.indices, 0, 0, meshData.indices.Length);
            SubMeshDescriptor subMesh = new SubMeshDescriptor(0, meshData.indices.Length, MeshTopology.Triangles);
            mesh.SetSubMesh(0, subMesh);
            
            mesh.RecalculateNormals();
            chunk.meshFilter.mesh = mesh;
            chunk.meshCollider.sharedMesh = mesh;
        }
    }


    private void Build()
    {
        if (_isBuilding || _meshComputeScheduler.IsMeshing()) return;

        _isBuilding = true;
        var mainThreadUpdates = new NativeQueue<VoxelOctreeNodePointer>(Allocator.TempJob);
        
        var job = new BuildJob
        {
            Root = _voxelRoot,
            Terrain = GetTerrainData(),
            MainThreadUpdates = mainThreadUpdates.AsParallelWriter(),
            ChunkDeleteQueue = _chunkDeleteQueue.AsParallelWriter()
        };
        
        var handle = job.Schedule();
        handle.Complete();

        while(mainThreadUpdates.TryDequeue(out var nodePtr))
        {
            unsafe
            {
                EnqueueChunkUpdate(nodePtr.Value);
            }
        }
        mainThreadUpdates.Dispose();
        _isBuilding = false;
    }

    private void GenerateEpsilons()
    {
        int numElements = size + 1;
        _voxelEpsilons.Clear();
        
        for (int i = 0; i < numElements; i++)
        {
            int s = 1 << i;
            float x = s * octreeScale;
            _voxelEpsilons.Add(1.75f * x);
        }
    }

    private void ProcessModifyJob(ModifySettings settings)
    {
        if (_isBuilding) return;

        _isBuilding = true;
        
        var mainThreadUpdates = new NativeQueue<VoxelOctreeNodePointer>(Allocator.TempJob);

        var job = new ModifyJob
        {
            Root = _voxelRoot,
            Settings = settings,
            Terrain = GetTerrainData(),
            MainThreadUpdates = mainThreadUpdates.AsParallelWriter(),
            ChunkDeleteQueue = _chunkDeleteQueue.AsParallelWriter()
        };
        
        var handle = job.Schedule();
        handle.Complete();

        while(mainThreadUpdates.TryDequeue(out var nodePtr))
        {
            unsafe
            {
                EnqueueChunkUpdate(nodePtr.Value);
            }
        }
        mainThreadUpdates.Dispose();
        
        _isBuilding = false;
    }

    private void ProcessDeleteQueue()
    {
        while (_chunkDeleteQueue.TryDequeue(out var request))
        {
            unsafe
            {
                long nodePtr = (long)request.chunk.Value;
                if (_activeChunks.TryGetValue(nodePtr, out JarVoxelChunkComponent chunk))
                {
                    chunk.gameObject.SetActive(false);
                    _chunkPool.Enqueue(chunk);
                    _activeChunks.Remove(nodePtr);
                }
            }
        }
    }

    public TerrainData GetTerrainData()
    {
        return new TerrainData
        {
            octreeScale = this.octreeScale,
            minChunkSize = this.minChunkSize,
            sdf = this.sdf,
            lod = _voxelLod,
        };
    }
}