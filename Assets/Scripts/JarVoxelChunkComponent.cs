using UnityEngine;

public unsafe class JarVoxelChunkComponent : MonoBehaviour
{
    public VoxelOctreeNode* node;
    public MeshFilter meshFilter;
    public MeshRenderer meshRenderer;
    public MeshCollider meshCollider;

    void Awake()
    {
        meshFilter = gameObject.AddComponent<MeshFilter>();
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshCollider = gameObject.AddComponent<MeshCollider>();
        meshRenderer.material = new Material(Shader.Find("Standard"));
    }

    public void SetNode(VoxelOctreeNode* n)
    {
        node = n;
        gameObject.name = $"Chunk_{node->_center.x}_{node->_center.y}_{node->_center.z}";
    }
}