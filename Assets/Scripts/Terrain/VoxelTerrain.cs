// Terrain/VoxelTerrain.cs
using UnityEngine;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Collections;

public class VoxelTerrain : MonoBehaviour
{
    [Header("Terrain Settings")]
    [Tooltip("The root signed distance field defining the initial terrain shape.")]
    public ISignedDistanceField Sdf;

    [Tooltip("The overall scale of the octree.")]
    public float OctreeScale = 1.0f;

    [Tooltip("The maximum depth of the octree. World size is 2^(Size) * OctreeScale.")]
    [Range(1, 16)]
    public int Size = 14;

    [Header("LOD Settings")]
    [Tooltip("The main camera used for LOD calculations.")]
    public Camera MainCamera;

    [Tooltip("The number of LOD levels.")]
    [Range(1, 16)]
    public int LodLevelCount = 8;

    [Tooltip("The size of the shell for each LOD level.")]
    [Range(1, 8)]
    public int ShellSize = 2;

    [Header("Chunk Settings")]
    [Tooltip("The material to apply to the generated voxel chunks.")]
    public Material ChunkMaterial;

    private VoxelOctreeNode _voxelRoot;
    private VoxelLod _voxelLod;

    // Job-related fields
    private JobHandle _meshingJobHandle;
    private StitchedSurfaceNetsJob _meshingJob;
    private bool _isMeshingJobRunning = false;
    private ChunkMeshData _chunkMeshData;

    // Reference to the VoxelChunk instance.
    private VoxelChunk _voxelChunk;

    void Start()
    {
        if (MainCamera == null)
        {
            MainCamera = Camera.main;
        }
        Initialize();
        ScheduleMeshingJob();
    }
    
    void Update()
    {
        // This is a simple way to trigger updates. In a real game, 
        // you would have a more sophisticated system to check if the camera has moved enough.
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Build();
            ScheduleMeshingJob();
        }
    }

    public void Initialize()
    {
        Sdf = new PlaneSdf(new float3(0, 1, 0), 0);
        _voxelRoot = new VoxelOctreeNode(Size);
        Build();
    }

    public void Build()
    {
        if (_voxelRoot == null || Sdf == null) return;
        
        _voxelLod = new VoxelLod(LodLevelCount, ShellSize, OctreeScale, MainCamera.transform.position);
        _voxelRoot.Build(this, _voxelLod);
    }

    public void SphereEdit(Vector3 position, float radius, bool isUnion)
    {
        if (_voxelRoot == null) return;

        var operation = isUnion ? SdfOperation.Union : SdfOperation.Subtraction;
        var sdf = new SphereSdf(float3.zero, radius);
        float boundsPadding = OctreeScale * 2.0f;
        var bounds = new Bounds(position, Vector3.one * (radius + boundsPadding) * 2f);

        var settings = new ModifySettings
        {
            Sdf = sdf,
            Operation = operation,
            Position = position,
            Bounds = bounds
        };

        _voxelLod = new VoxelLod(LodLevelCount, ShellSize, OctreeScale, MainCamera.transform.position);
        _voxelRoot.ModifySdfInBounds(this, settings, _voxelLod);
        ScheduleMeshingJob();
    }

    private void ScheduleMeshingJob()
    {
        if (_isMeshingJobRunning)
        {
            _meshingJobHandle.Complete();
            _chunkMeshData.Dispose();
            _meshingJob.Chunk.Dispose();
        }

        var chunk = new StitchedMeshChunk(0, Allocator.Persistent);
        for (int i = 0; i < StitchedMeshChunk.ChunkSize3; i++)
        {
            var pos = new int3(i % 16, (i / 16) % 16, i / (16 * 16));
            chunk.VoxelData[i] = Sdf.Distance(pos);
        }

        _chunkMeshData = new ChunkMeshData(Allocator.Persistent);

        _meshingJob = new StitchedSurfaceNetsJob
        {
            Chunk = chunk,
            MeshData = _chunkMeshData
        };

        _meshingJobHandle = _meshingJob.Schedule();
        _isMeshingJobRunning = true;
    }

    private void LateUpdate()
    {
        if (_isMeshingJobRunning && _meshingJobHandle.IsCompleted)
        {
            _meshingJobHandle.Complete();
            _isMeshingJobRunning = false;

            if (_voxelChunk == null)
            {
                var go = new GameObject("Voxel Chunk");
                _voxelChunk = go.AddComponent<VoxelChunk>();
            }

            Material material = ChunkMaterial != null ? ChunkMaterial : new Material(Shader.Find("Standard"));
            _voxelChunk.UpdateChunk(_chunkMeshData, material);

            _chunkMeshData.Dispose();
            _meshingJob.Chunk.Dispose();
        }
    }

    private void OnDestroy()
    {
        if (_isMeshingJobRunning)
        {
            _meshingJobHandle.Complete();
            _chunkMeshData.Dispose();
            _meshingJob.Chunk.Dispose();
        }
    }
}