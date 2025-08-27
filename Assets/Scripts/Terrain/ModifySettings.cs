using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public struct ModifySettings
{
    public ISignedDistanceField Sdf;
    public SdfOperation Operation;
    public float3 Position;
    public Bounds Bounds;
}
