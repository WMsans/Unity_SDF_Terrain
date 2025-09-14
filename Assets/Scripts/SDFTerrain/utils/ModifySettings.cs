using Unity.Mathematics;

public struct ModifySettings
{
    public SdfData Sdf; 
    public BurstBounds Bounds;
    public float3 Position;
    public Operation Operation;
}