using UnityEngine;

public unsafe class JarVoxelChunkComponent : MonoBehaviour
{
    public VoxelOctreeNode* node;
    public MeshFilter meshFilter;
    public MeshRenderer meshRenderer;
    public MeshCollider meshCollider;

    void Awake()
    {
        if(!meshFilter) meshFilter = gameObject.AddComponent<MeshFilter>();
        if(!meshRenderer) meshRenderer = gameObject.AddComponent<MeshRenderer>();
        if(!meshCollider) meshCollider = gameObject.AddComponent<MeshCollider>();
        meshRenderer.material = new Material(Shader.Find("Standard"));
    }

    public unsafe void SetNode(VoxelOctreeNode* n)
    {
        node = n;
        if (node != null)
        {
            gameObject.name = $"Chunk_{node->_center.x:F1}_{node->_center.y:F1}_{node->_center.z:F1}";
        }
        else
        {
            gameObject.name = "Chunk_NULL";
        }
    }
}