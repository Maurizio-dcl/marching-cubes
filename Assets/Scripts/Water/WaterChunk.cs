using UnityEngine;

namespace DefaultNamespace.Water
{
    public sealed class WaterChunk
    {
        public WaterChunk(Vector2Int coordinate, Bounds bounds)
        {
            Coordinate = coordinate;
            Bounds = bounds;
            IsActive = true;
            IsMeshDirty = true;
        }

        public Vector2Int Coordinate { get; }
        public Bounds Bounds { get; }
        public bool IsActive { get; set; }
        public bool IsMeshDirty { get; set; }
        public bool IsSimulationDirty { get; set; }
        public Mesh Mesh { get; set; }
        public MeshFilter MeshFilter { get; set; }
        public MeshRenderer MeshRenderer { get; set; }
    }
}
