using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct PlaneSdf : ISignedDistanceField
{
    private float _d;
    private float3 _normal;
    public float Distance(float3 pos)
    {
        return math.dot(_normal, pos) + _d;
    }

    public void SetNormal(float3 normal)
    {
        _normal = normal;
    }

    public float3 GetNormal() => _normal;

    public PlaneSdf(float3 normal, float d)
    {
        _d = d;
        _normal = normal;
    }
    public float3 Normal(float3 pos)
    {
        return _normal;
    }
}