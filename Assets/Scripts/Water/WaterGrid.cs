using UnityEngine;

namespace DefaultNamespace.Water
{
    public sealed class WaterGrid
    {
        public const byte HasGroundFlag = 1 << 0;
        public const byte ActiveFlag = 1 << 1;

        private readonly float[] _groundHeights;
        private readonly float[] _waterDepths;
        private readonly float[] _nextWaterDepths;
        private readonly Vector2[] _flowVelocities;
        private readonly byte[] _flags;

        public WaterGrid(Bounds worldBounds, int chunksPerAxis, float chunkWorldSize, int cellsPerChunkAxis)
        {
            WorldBounds = worldBounds;
            ChunksPerAxis = Mathf.Max(1, chunksPerAxis);
            ChunkWorldSize = Mathf.Max(0.001f, chunkWorldSize);
            CellsPerChunkAxis = Mathf.Max(1, cellsPerChunkAxis);
            CellsPerAxis = ChunksPerAxis * CellsPerChunkAxis;
            CellSize = ChunkWorldSize / CellsPerChunkAxis;

            int count = CellsPerAxis * CellsPerAxis;
            _groundHeights = new float[count];
            _waterDepths = new float[count];
            _nextWaterDepths = new float[count];
            _flowVelocities = new Vector2[count];
            _flags = new byte[count];
        }

        public Bounds WorldBounds { get; }
        public int ChunksPerAxis { get; }
        public float ChunkWorldSize { get; }
        public int CellsPerChunkAxis { get; }
        public int CellsPerAxis { get; }
        public float CellSize { get; }
        public int Count => _waterDepths.Length;
        public float[] GroundHeights => _groundHeights;
        public float[] WaterDepths => _waterDepths;
        public float[] NextWaterDepths => _nextWaterDepths;
        public Vector2[] FlowVelocities => _flowVelocities;
        public byte[] Flags => _flags;

        public int Index(int x, int z)
        {
            return x + z * CellsPerAxis;
        }

        public bool Contains(int x, int z)
        {
            return x >= 0 && z >= 0 && x < CellsPerAxis && z < CellsPerAxis;
        }

        public Vector2 CellCenterXZ(int x, int z)
        {
            Vector3 min = WorldBounds.min;
            return new Vector2(
                min.x + (x + 0.5f) * CellSize,
                min.z + (z + 0.5f) * CellSize);
        }

        public Vector3 CellCenterWorld(int x, int z, float y)
        {
            Vector2 xz = CellCenterXZ(x, z);
            return new Vector3(xz.x, y, xz.y);
        }

        public bool TryWorldToCell(Vector3 worldPosition, out int x, out int z)
        {
            Vector3 min = WorldBounds.min;
            x = Mathf.FloorToInt((worldPosition.x - min.x) / CellSize);
            z = Mathf.FloorToInt((worldPosition.z - min.z) / CellSize);
            return Contains(x, z);
        }

        public void WorldBoundsToCellRect(Bounds bounds, int padding, out int minX, out int maxX, out int minZ, out int maxZ)
        {
            Vector3 min = WorldBounds.min;
            Vector3 boundsMin = bounds.min;
            Vector3 boundsMax = bounds.max;
            minX = Mathf.Clamp(Mathf.FloorToInt((boundsMin.x - min.x) / CellSize) - padding, 0, CellsPerAxis - 1);
            maxX = Mathf.Clamp(Mathf.FloorToInt((boundsMax.x - min.x) / CellSize) + padding, 0, CellsPerAxis - 1);
            minZ = Mathf.Clamp(Mathf.FloorToInt((boundsMin.z - min.z) / CellSize) - padding, 0, CellsPerAxis - 1);
            maxZ = Mathf.Clamp(Mathf.FloorToInt((boundsMax.z - min.z) / CellSize) + padding, 0, CellsPerAxis - 1);
        }

        public int CellToChunk(int cell)
        {
            return Mathf.Clamp(cell / CellsPerChunkAxis, 0, ChunksPerAxis - 1);
        }

        public bool HasGround(int index)
        {
            return (_flags[index] & HasGroundFlag) != 0;
        }

        public void SetHasGround(int index, bool hasGround)
        {
            if (hasGround)
            {
                _flags[index] |= HasGroundFlag;
            }
            else
            {
                _flags[index] &= unchecked((byte)~HasGroundFlag);
            }
        }
    }
}
