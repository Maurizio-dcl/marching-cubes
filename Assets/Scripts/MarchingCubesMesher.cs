using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DefaultNamespace
{
    internal static class MarchingCubesMesher
    {
        private static readonly Vector3Int[] CornerOffsets =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(1, 1, 0),
            new(0, 1, 0),
            new(0, 0, 1),
            new(1, 0, 1),
            new(1, 1, 1),
            new(0, 1, 1)
        };

        public static void Generate(Chunk chunk, float isoLevel, Mesh mesh, bool interpolate)
        {
            Builder builder = new(chunk, isoLevel, interpolate);
            builder.MarchAll();
            builder.ApplyTo(mesh);
        }

        public sealed class Builder
        {
            private readonly Chunk _chunk;
            private readonly float _isoLevel;
            private readonly bool _interpolate;
            private readonly List<Vector3> _vertices = new();
            private readonly List<int> _triangles = new();
            private readonly Dictionary<EdgeKey, int> _vertexIndicesByEdge = new();
            private int _nextCellIndex;

            public Builder(Chunk chunk, float isoLevel, bool interpolate)
            {
                _chunk = chunk;
                _isoLevel = isoLevel;
                _interpolate = interpolate;
            }

            public bool IsComplete => _nextCellIndex >= _chunk.Density * _chunk.Density * _chunk.Density;

            public void MarchAll()
            {
                while (MarchNextCell())
                {
                }
            }

            public bool MarchNextCell()
            {
                if (IsComplete)
                {
                    return false;
                }

                int density = _chunk.Density;
                int x = _nextCellIndex % density;
                int y = _nextCellIndex / density % density;
                int z = _nextCellIndex / (density * density);
                _nextCellIndex++;

                MarchCell(
                    _chunk,
                    new Vector3Int(x, y, z),
                    _isoLevel,
                    _vertices,
                    _triangles,
                    _vertexIndicesByEdge,
                    _interpolate);

                return true;
            }

            public void ApplyTo(Mesh mesh)
            {
                mesh.name = "Marching Cubes Chunk";
                mesh.Clear();
                mesh.indexFormat = _vertices.Count > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16;
                mesh.SetVertices(_vertices);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }

        private static void MarchCell(
            Chunk chunk,
            Vector3Int cell,
            float isoLevel,
            List<Vector3> vertices,
            List<int> triangles,
            Dictionary<EdgeKey, int> vertexIndicesByEdge,
            bool interpolate)
        {
            Vector3[] positions = new Vector3[8];
            float[] values = new float[8];
            bool[] inside = new bool[8];

            int caseIndex = 0;

            for (int i = 0; i < CornerOffsets.Length; i++)
            {
                Vector3Int sample = cell + CornerOffsets[i];
                Point point = chunk.GetPoint(sample.x, sample.y, sample.z);

                positions[i] = chunk.GetLocalPosition(point);
                values[i] = point.Value;
                inside[i] = values[i] >= isoLevel;

                if (inside[i])
                {
                    caseIndex |= 1 << i;
                }
            }

            if (caseIndex == 0 || caseIndex == 255)
            {
                return;
            }

            int[] edgeVertexIndices = new int[12];

            for (int i = 0; i < edgeVertexIndices.Length; i++)
            {
                edgeVertexIndices[i] = -1;
            }

            for (int edge = 0; edge < edgeVertexIndices.Length; edge++)
            {
                int a = MarchingCubesLookup.EdgeCorners[edge, 0];
                int b = MarchingCubesLookup.EdgeCorners[edge, 1];

                if (inside[a] == inside[b])
                {
                    continue;
                }

                Vector3Int pointA = cell + CornerOffsets[a];
                Vector3Int pointB = cell + CornerOffsets[b];
                EdgeKey edgeKey = new(pointA, pointB);

                if (!vertexIndicesByEdge.TryGetValue(edgeKey, out int vertexIndex))
                {
                    vertexIndex = vertices.Count;
                    Vector3 vertex = interpolate
                        ? InterpolateEdgeVertex(
                            positions[a],
                            positions[b],
                            values[a],
                            values[b],
                            isoLevel)
                        : (positions[a] + positions[b]) * 0.5f;
                    vertices.Add(vertex);
                    vertexIndicesByEdge.Add(edgeKey, vertexIndex);
                }

                edgeVertexIndices[edge] = vertexIndex;
            }

            for (int i = 0; i < 16; i += 3)
            {
                int edgeA = MarchingCubesLookup.Triangulation[caseIndex, i];

                if (edgeA == -1)
                {
                    break;
                }

                int edgeB = MarchingCubesLookup.Triangulation[caseIndex, i + 1];
                int edgeC = MarchingCubesLookup.Triangulation[caseIndex, i + 2];

                triangles.Add(edgeVertexIndices[edgeC]);
                triangles.Add(edgeVertexIndices[edgeB]);
                triangles.Add(edgeVertexIndices[edgeA]);
            }
        }

        private static Vector3 InterpolateEdgeVertex(
            Vector3 positionA,
            Vector3 positionB,
            float valueA,
            float valueB,
            float isoLevel)
        {
            float t = (isoLevel - valueA) / (valueB - valueA);
            return Vector3.Lerp(positionA, positionB, t);
        }

        private readonly struct EdgeKey
        {
            private readonly Vector3Int _a;
            private readonly Vector3Int _b;

            public EdgeKey(Vector3Int a, Vector3Int b)
            {
                if (Compare(a, b) <= 0)
                {
                    _a = a;
                    _b = b;
                }
                else
                {
                    _a = b;
                    _b = a;
                }
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other && _a == other._a && _b == other._b;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_a.GetHashCode() * 397) ^ _b.GetHashCode();
                }
            }

            private static int Compare(Vector3Int a, Vector3Int b)
            {
                if (a.x != b.x)
                {
                    return a.x.CompareTo(b.x);
                }

                if (a.y != b.y)
                {
                    return a.y.CompareTo(b.y);
                }

                return a.z.CompareTo(b.z);
            }
        }
    }
}
