using Unity.Mathematics;

public interface ISignedDistanceField
{
    float Distance(float3 pos);
    float3 Normal(float3 pos);
}