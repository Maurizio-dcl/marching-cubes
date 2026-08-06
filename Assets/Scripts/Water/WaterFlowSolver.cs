using UnityEngine;

namespace DefaultNamespace.Water
{
    public sealed class WaterFlowSolver
    {
        private const int DirectionCount = 4;
        private readonly WaterGrid _grid;
        private readonly WaterSimulationSettings _settings;
        private readonly float[] _requestedFlows;
        private readonly WaterOutflow[] _outflows;
        private int _outflowCount;

        public WaterFlowSolver(WaterGrid grid, WaterSimulationSettings settings, int maxOutflows)
        {
            _grid = grid;
            _settings = settings;
            _requestedFlows = new float[grid.Count * DirectionCount];
            _outflows = new WaterOutflow[Mathf.Max(16, maxOutflows)];
        }

        public WaterOutflow[] Outflows => _outflows;
        public int OutflowCount => _outflowCount;

        public bool Step(float deltaTime)
        {
            return Step(deltaTime, 0, _grid.CellsPerAxis - 1, 0, _grid.CellsPerAxis - 1);
        }

        public bool Step(float deltaTime, int minX, int maxX, int minZ, int maxZ)
        {
            float[] depths = _grid.WaterDepths;
            float[] next = _grid.NextWaterDepths;
            float[] ground = _grid.GroundHeights;
            Vector2[] velocities = _grid.FlowVelocities;
            byte[] flags = _grid.Flags;
            int cellsPerAxis = _grid.CellsPerAxis;
            float minimumDepth = _settings.minimumWaterDepth;
            float minimumDifference = _settings.minimumFlowHeightDifference;
            float flowRate = _settings.flowRate;
            float maxFlow = _settings.maximumFlowPerStep;
            float cellSize = _grid.CellSize;
            bool changed = false;
            _outflowCount = 0;

            System.Array.Copy(depths, next, depths.Length);
            System.Array.Clear(_requestedFlows, 0, _requestedFlows.Length);
            System.Array.Clear(velocities, 0, velocities.Length);
            minX = Mathf.Clamp(minX, 0, cellsPerAxis - 1);
            maxX = Mathf.Clamp(maxX, minX, cellsPerAxis - 1);
            minZ = Mathf.Clamp(minZ, 0, cellsPerAxis - 1);
            maxZ = Mathf.Clamp(maxZ, minZ, cellsPerAxis - 1);

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = _grid.Index(x, z);

                    if ((flags[index] & WaterGrid.HasGroundFlag) == 0 || depths[index] <= minimumDepth)
                    {
                        continue;
                    }

                    float surface = ground[index] + depths[index];
                    float totalRequested = 0f;
                    totalRequested += RequestFlow(index, x, z, x, z + 1, 0, surface, deltaTime, minimumDifference, flowRate, maxFlow);
                    totalRequested += RequestFlow(index, x, z, x, z - 1, 1, surface, deltaTime, minimumDifference, flowRate, maxFlow);
                    totalRequested += RequestFlow(index, x, z, x + 1, z, 2, surface, deltaTime, minimumDifference, flowRate, maxFlow);
                    totalRequested += RequestFlow(index, x, z, x - 1, z, 3, surface, deltaTime, minimumDifference, flowRate, maxFlow);

                    if (totalRequested <= depths[index])
                    {
                        continue;
                    }

                    float scale = depths[index] / totalRequested;
                    int flowOffset = index * DirectionCount;
                    _requestedFlows[flowOffset] *= scale;
                    _requestedFlows[flowOffset + 1] *= scale;
                    _requestedFlows[flowOffset + 2] *= scale;
                    _requestedFlows[flowOffset + 3] *= scale;
                }
            }

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = _grid.Index(x, z);
                    int flowOffset = index * DirectionCount;
                    ApplyFlow(index, x, z, x, z + 1, _requestedFlows[flowOffset], new Vector2(0f, 1f), cellSize, next, velocities);
                    ApplyFlow(index, x, z, x, z - 1, _requestedFlows[flowOffset + 1], new Vector2(0f, -1f), cellSize, next, velocities);
                    ApplyFlow(index, x, z, x + 1, z, _requestedFlows[flowOffset + 2], new Vector2(1f, 0f), cellSize, next, velocities);
                    ApplyFlow(index, x, z, x - 1, z, _requestedFlows[flowOffset + 3], new Vector2(-1f, 0f), cellSize, next, velocities);
                }
            }

            for (int i = 0; i < depths.Length; i++)
            {
                float depth = next[i] <= minimumDepth ? 0f : next[i];

                if (Mathf.Abs(depth - depths[i]) > _settings.settleDepthDelta)
                {
                    changed = true;
                }

                depths[i] = depth;
            }

            return changed;
        }

        private float RequestFlow(
            int index,
            int sourceX,
            int sourceZ,
            int targetX,
            int targetZ,
            int direction,
            float sourceSurface,
            float deltaTime,
            float minimumDifference,
            float flowRate,
            float maxFlow)
        {
            if (!_grid.Contains(targetX, targetZ))
            {
                float edgeFlow = Mathf.Min(maxFlow, _grid.WaterDepths[index] * flowRate * deltaTime);
                _requestedFlows[index * DirectionCount + direction] = edgeFlow;
                return edgeFlow;
            }

            int targetIndex = _grid.Index(targetX, targetZ);

            if (!_grid.HasGround(targetIndex))
            {
                float voidFlow = Mathf.Min(maxFlow, _grid.WaterDepths[index] * flowRate * deltaTime);
                _requestedFlows[index * DirectionCount + direction] = voidFlow;
                return voidFlow;
            }

            float targetSurface = _grid.GroundHeights[targetIndex] + _grid.WaterDepths[targetIndex];
            float difference = sourceSurface - targetSurface;

            if (difference <= minimumDifference)
            {
                return 0f;
            }

            float requested = Mathf.Min(maxFlow, difference * flowRate * deltaTime);
            _requestedFlows[index * DirectionCount + direction] = requested;
            return requested;
        }

        private void ApplyFlow(
            int sourceIndex,
            int sourceX,
            int sourceZ,
            int targetX,
            int targetZ,
            float amount,
            Vector2 direction,
            float cellSize,
            float[] next,
            Vector2[] velocities)
        {
            if (amount <= 0f)
            {
                return;
            }

            next[sourceIndex] -= amount;
            velocities[sourceIndex] += direction * (amount / Mathf.Max(0.0001f, cellSize));

            if (_grid.Contains(targetX, targetZ))
            {
                int targetIndex = _grid.Index(targetX, targetZ);

                if (_grid.HasGround(targetIndex))
                {
                    next[targetIndex] += amount;
                    velocities[targetIndex] += direction * (amount / Mathf.Max(0.0001f, cellSize));
                    return;
                }
            }

            AddOutflow(sourceX, sourceZ, amount, direction);
        }

        private void AddOutflow(int sourceX, int sourceZ, float amount, Vector2 direction)
        {
            if (_outflowCount >= _outflows.Length)
            {
                return;
            }

            int sourceIndex = _grid.Index(sourceX, sourceZ);
            float y = _grid.GroundHeights[sourceIndex] + _grid.WaterDepths[sourceIndex];
            _outflows[_outflowCount++] = new WaterOutflow
            {
                Position = _grid.CellCenterWorld(sourceX, sourceZ, y),
                Direction = direction,
                Amount = amount
            };
        }
    }
}
