using System.Collections.Generic;
using UnityEngine;

namespace DefaultNamespace.Water
{
    public static class WaterLakeInitializer
    {
        public static void Initialize(
            WaterGrid grid,
            Bounds terrainBounds,
            IReadOnlyList<LakeWaterBody> lakes,
            IReadOnlyList<RiverWaterBody> rivers)
        {
            if (grid == null)
            {
                return;
            }

            if (lakes != null)
            {
                InitializeLakes(grid, terrainBounds, lakes);
            }

            if (rivers != null)
            {
                InitializeRivers(grid, terrainBounds, rivers);
            }
        }

        private static void InitializeLakes(WaterGrid grid, Bounds terrainBounds, IReadOnlyList<LakeWaterBody> lakes)
        {
            for (int i = 0; i < lakes.Count; i++)
            {
                LakeWaterBody lake = lakes[i];
                float fillRadius = Mathf.Max(0.001f, lake.Radius);
                float radiusSqr = fillRadius * fillRadius;
                Bounds lakeBounds = new(
                    new Vector3(lake.WorldXZ.x, terrainBounds.center.y, lake.WorldXZ.y),
                    new Vector3(fillRadius * 2f, terrainBounds.size.y, fillRadius * 2f));

                grid.WorldBoundsToCellRect(lakeBounds, 0, out int minX, out int maxX, out int minZ, out int maxZ);

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 cell = grid.CellCenterXZ(x, z);

                        if ((cell - lake.WorldXZ).sqrMagnitude <= radiusSqr)
                        {
                            FillCellToSurface(grid, x, z, lake.SurfaceHeight);
                        }
                    }
                }
            }
        }

        private static void InitializeRivers(WaterGrid grid, Bounds terrainBounds, IReadOnlyList<RiverWaterBody> rivers)
        {
            for (int i = 0; i < rivers.Count; i++)
            {
                RiverWaterBody river = rivers[i];
                float width = Mathf.Max(0.001f, river.Width);
                Vector2 min = Vector2.Min(river.StartWorldXZ, river.EndWorldXZ) - Vector2.one * width;
                Vector2 max = Vector2.Max(river.StartWorldXZ, river.EndWorldXZ) + Vector2.one * width;
                Bounds riverBounds = new(
                    new Vector3((min.x + max.x) * 0.5f, terrainBounds.center.y, (min.y + max.y) * 0.5f),
                    new Vector3(max.x - min.x, terrainBounds.size.y, max.y - min.y));

                grid.WorldBoundsToCellRect(riverBounds, 1, out int minX, out int maxX, out int minZ, out int maxZ);

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 cell = grid.CellCenterXZ(x, z);
                        float distance = DistanceToRiverCenterLine(cell, river, out float t);

                        if (distance > width)
                        {
                            continue;
                        }

                        float surfaceHeight = Mathf.Lerp(river.StartSurfaceHeight, river.EndSurfaceHeight, t);
                        FillCellToSurface(grid, x, z, surfaceHeight);
                    }
                }
            }
        }

        private static void FillCellToSurface(WaterGrid grid, int x, int z, float surfaceHeight)
        {
            int index = grid.Index(x, z);

            if (!grid.HasGround(index) || grid.GroundHeights[index] >= surfaceHeight)
            {
                return;
            }

            grid.WaterDepths[index] = Mathf.Max(grid.WaterDepths[index], surfaceHeight - grid.GroundHeights[index]);
        }

        private static float DistanceToRiverCenterLine(Vector2 point, RiverWaterBody river, out float t)
        {
            Vector2 start = river.StartWorldXZ;
            Vector2 end = river.EndWorldXZ;
            Vector2 segment = end - start;
            float lengthSqr = segment.sqrMagnitude;

            if (lengthSqr <= 0.000001f)
            {
                t = 0f;
                return (point - start).magnitude;
            }

            t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSqr);
            Vector2 center = Vector2.Lerp(start, end, t);

            if (river.MeanderStrength > 0f && river.MeanderFrequency > 0f)
            {
                Vector2 normal = new Vector2(-segment.y, segment.x).normalized;
                Vector2 localCenter = center - river.NoiseOriginWorldXZ;
                float meander = PerlinNoise3D.SampleSigned(
                    (localCenter.x + river.NoiseSeedOffsets.x) * river.MeanderFrequency,
                    0f,
                    (localCenter.y + river.NoiseSeedOffsets.y) * river.MeanderFrequency);
                center += normal * (meander * river.Width * river.MeanderStrength);
            }

            return (point - center).magnitude;
        }

    }
}
