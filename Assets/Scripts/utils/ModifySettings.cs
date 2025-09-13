using Unity.Mathematics;
using UnityEngine;

public struct ModifySettings
{
    public IJarSignedDistanceField sdf;
    public BurstBounds bounds;
    public float3 position;
    public Operation operation;
}
