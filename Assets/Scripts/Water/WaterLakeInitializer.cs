using System.Collections.Generic;
using UnityEngine;

namespace DefaultNamespace.Water
{
    public static class WaterLakeInitializer
    {
        public static void Initialize(WaterGrid grid, Bounds terrainBounds, IReadOnlyList<LakeWaterBody> lakes)
        {
            if (grid == null || lakes == null)
            {
                return;
            }

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

                        if ((cell - lake.WorldXZ).sqrMagnitude > radiusSqr)
                        {
                            continue;
                        }

                        int index = grid.Index(x, z);

                        if (!grid.HasGround(index) || grid.GroundHeights[index] >= lake.SurfaceHeight)
                        {
                            continue;
                        }

                        grid.WaterDepths[index] = Mathf.Max(grid.WaterDepths[index], lake.SurfaceHeight - grid.GroundHeights[index]);
                    }
                }
            }
        }
    }
}
