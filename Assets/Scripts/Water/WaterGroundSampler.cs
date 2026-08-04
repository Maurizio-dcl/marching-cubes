using UnityEngine;

namespace DefaultNamespace.Water
{
    public sealed class WaterGroundSampler
    {
        private readonly ITerrainDensityField _terrain;
        private readonly WaterGrid _grid;
        private readonly WaterSimulationSettings _settings;

        public WaterGroundSampler(ITerrainDensityField terrain, WaterGrid grid, WaterSimulationSettings settings)
        {
            _terrain = terrain;
            _grid = grid;
            _settings = settings;
        }

        public void RecalculateAll()
        {
            RecalculateCells(0, _grid.CellsPerAxis - 1, 0, _grid.CellsPerAxis - 1);
        }

        public void Recalculate(Bounds worldBounds)
        {
            _grid.WorldBoundsToCellRect(worldBounds, 1, out int minX, out int maxX, out int minZ, out int maxZ);
            RecalculateCells(minX, maxX, minZ, maxZ);
        }

        public void RecalculateCells(int minX, int maxX, int minZ, int maxZ)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = _grid.Index(x, z);
                    bool hadGround = _grid.HasGround(index);
                    float oldGround = _grid.GroundHeights[index];

                    if (TrySampleGroundHeight(x, z, out float groundHeight))
                    {
                        _grid.GroundHeights[index] = groundHeight;
                        _grid.SetHasGround(index, true);

                        if (hadGround && groundHeight > oldGround)
                        {
                            float displaced = Mathf.Min(_grid.WaterDepths[index], groundHeight - oldGround);
                            _grid.WaterDepths[index] -= displaced;
                            RedistributeDisplacedWater(x, z, displaced);
                        }
                    }
                    else
                    {
                        float displaced = _grid.WaterDepths[index];
                        _grid.SetHasGround(index, false);
                        _grid.GroundHeights[index] = 0f;
                        _grid.WaterDepths[index] = 0f;
                        RedistributeDisplacedWater(x, z, displaced);
                    }
                }
            }
        }

        public bool TrySampleGroundHeight(int x, int z, out float height)
        {
            Bounds bounds = _terrain.WorldBounds;
            Vector2 xz = _grid.CellCenterXZ(x, z);
            float step = Mathf.Max(0.001f, _settings.groundSearchStep);
            float iso = _terrain.IsoLevel;
            float top = bounds.max.y + _settings.groundSearchPadding;
            float bottom = bounds.min.y - _settings.groundSearchPadding;

            Vector3 previousPosition = new(xz.x, top, xz.y);
            float previousDensity = _terrain.SampleDensity(previousPosition);

            for (float y = top - step; y >= bottom; y -= step)
            {
                Vector3 position = new(xz.x, y, xz.y);
                float density = _terrain.SampleDensity(position);

                if (previousDensity < iso && density >= iso)
                {
                    float t = Mathf.InverseLerp(previousDensity, density, iso);
                    height = Mathf.Lerp(previousPosition.y, position.y, t);
                    return true;
                }

                previousPosition = position;
                previousDensity = density;
            }

            height = 0f;
            return false;
        }

        private void RedistributeDisplacedWater(int x, int z, float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            int bestIndex = -1;
            float bestSurface = float.PositiveInfinity;

            TryFindDisplacementTarget(x - 1, z, ref bestIndex, ref bestSurface);
            TryFindDisplacementTarget(x + 1, z, ref bestIndex, ref bestSurface);
            TryFindDisplacementTarget(x, z - 1, ref bestIndex, ref bestSurface);
            TryFindDisplacementTarget(x, z + 1, ref bestIndex, ref bestSurface);

            if (bestIndex >= 0)
            {
                _grid.WaterDepths[bestIndex] += amount;
            }
        }

        private void TryFindDisplacementTarget(int x, int z, ref int bestIndex, ref float bestSurface)
        {
            if (!_grid.Contains(x, z))
            {
                return;
            }

            int index = _grid.Index(x, z);

            if (!_grid.HasGround(index))
            {
                return;
            }

            float surface = _grid.GroundHeights[index] + _grid.WaterDepths[index];

            if (surface < bestSurface)
            {
                bestSurface = surface;
                bestIndex = index;
            }
        }
    }
}
