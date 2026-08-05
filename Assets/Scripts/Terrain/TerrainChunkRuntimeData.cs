using UnityEngine;

namespace DefaultNamespace.Terrain
{
    public sealed class TerrainChunkRuntimeData
    {
        public TerrainChunkRuntimeData(TerrainChunkId id, Vector3Int origin, Bounds bounds)
        {
            Id = id;
            Origin = origin;
            Bounds = bounds;
            DesiredLod = -1;
            CurrentLod = -1;
            IsDirty = true;
            DirtyBounds = bounds;
        }

        public TerrainChunkId Id { get; }
        public Vector3Int Origin { get; }
        public Bounds Bounds { get; }
        public Chunk Chunk { get; set; }
        public Mesh Mesh { get; set; }
        public TerrainChunkView View { get; set; }
        public int DesiredLod { get; set; }
        public int CurrentLod { get; set; }
        public bool IsVisible { get; set; }
        public bool WasRecentlyVisible { get; set; }
        public bool IsDirty { get; private set; }
        public bool IsBeingModified { get; private set; }
        public Bounds DirtyBounds { get; private set; }
        public int LastVisibleFrame { get; set; }
        public float DistanceToCamera { get; set; }
        public int VertexCount => Mesh != null ? Mesh.vertexCount : 0;
        public int TriangleCount => Mesh != null ? Mesh.triangles.Length / 3 : 0;

        public void MarkDirty(Bounds worldBounds, bool isModification)
        {
            DirtyBounds = IsDirty ? Encapsulate(DirtyBounds, worldBounds) : worldBounds;
            IsDirty = true;
            IsBeingModified |= isModification;
        }

        public void ClearDirty()
        {
            IsDirty = false;
            IsBeingModified = false;
            DirtyBounds = Bounds;
        }

        private static Bounds Encapsulate(Bounds a, Bounds b)
        {
            a.Encapsulate(b.min);
            a.Encapsulate(b.max);
            return a;
        }
    }
}
