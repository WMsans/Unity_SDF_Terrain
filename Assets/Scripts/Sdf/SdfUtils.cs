using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
    public struct SdfUtils
    {
        [BurstCompile]
        public static float Distance(in SdfData sdf, float3 pos)
        {
            switch (sdf.Type)
            {
                case SdfType.Sphere:
                    return SphereDistance(sdf, pos);
                case SdfType.Box:
                    return BoxDistance(sdf, pos);
                case SdfType.Plane:
                    return PlaneDistance(sdf, pos);
                case SdfType.Planet:
                    return PlanetDistance(sdf, pos);
                case SdfType.Terrain:
                    return TerrainDistance(sdf, pos);
                default:
                    return float.MaxValue;
            }
        }

        private static float SphereDistance(in SdfData sdf, float3 pos)
        {
            return math.length(pos - sdf.Center) - sdf.Radius;
        }

        private static float BoxDistance(in SdfData sdf, float3 pos)
        {
            float3 q = math.abs(pos - sdf.Center) - sdf.Extent;
            return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f);
        }

        private static float PlaneDistance(in SdfData sdf, float3 pos)
        {
            return math.dot(sdf.Normal, pos) + sdf.D;
        }

        private static float PlanetDistance(in SdfData sdf, float3 pos)
        {
            float3 to_center = pos - sdf.Center;
            float base_distance = math.length(to_center) - sdf.Radius;

            if (base_distance > sdf.NoiseScale * 1.25f)
            {
                return base_distance;
            }

            // Placeholder for actual noise function
            float displacement = 0;
            return base_distance - displacement;
        }

        private static float TerrainDistance(in SdfData sdf, float3 pos)
        {
            // Placeholder for noise function
            float height = 0;
            float2 gradient = 0;
            return (pos.y - height) / math.sqrt(1 + gradient.x * gradient.x + gradient.y * gradient.y);
        }
    }