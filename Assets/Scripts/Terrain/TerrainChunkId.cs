using System;
using UnityEngine;

namespace DefaultNamespace.Terrain
{
    [Serializable]
    public readonly struct TerrainChunkId : IEquatable<TerrainChunkId>
    {
        public TerrainChunkId(int islandInstanceId, Vector3Int coordinate)
        {
            IslandInstanceId = islandInstanceId;
            Coordinate = coordinate;
        }

        public int IslandInstanceId { get; }
        public Vector3Int Coordinate { get; }

        public bool Equals(TerrainChunkId other)
        {
            return IslandInstanceId == other.IslandInstanceId && Coordinate == other.Coordinate;
        }

        public override bool Equals(object obj)
        {
            return obj is TerrainChunkId other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (IslandInstanceId * 397) ^ Coordinate.GetHashCode();
            }
        }

        public override string ToString()
        {
            return IslandInstanceId + ":" + Coordinate;
        }
    }
}
