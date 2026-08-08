using Unity.Profiling;

namespace DefaultNamespace.Terrain
{
    public static class IslandProfiler
    {
        public static readonly ProfilerMarker Refresh = new("Island.Refresh");
        public static readonly ProfilerMarker GenerateChunks = new("Island.GenerateChunks");
        public static readonly ProfilerMarker BuildChunk = new("Island.BuildChunk");
        public static readonly ProfilerMarker MeshExtraction = new("Island.MeshExtraction");
        public static readonly ProfilerMarker LODUpdate = new("Island.LODUpdate");
        public static readonly ProfilerMarker DirtyTerrain = new("Island.DirtyTerrain");
    }
}
