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

        public static void Generate(Chunk chunk, float isoLevel, Mesh mesh)
        {
            List<Vector3> vertices = new();
            List<int> triangles = new();
            Dictionary<EdgeKey, int> vertexIndicesByEdge = new();

            for (int z = 0; z < chunk.Density; z++)
            {
                for (int y = 0; y < chunk.Density; y++)
                {
                    for (int x = 0; x < chunk.Density; x++)
                    {
                        MarchCell(
                            chunk,
                            new Vector3Int(x, y, z),
                            isoLevel,
                            vertices,
                            triangles,
                            vertexIndicesByEdge);
                    }
                }
            }

            mesh.name = "Marching Cubes Chunk";
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static void MarchCell(
            Chunk chunk,
            Vector3Int cell,
            float isoLevel,
            List<Vector3> vertices,
            List<int> triangles,
            Dictionary<EdgeKey, int> vertexIndicesByEdge)
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
                    vertices.Add((positions[a] + positions[b]) * 0.5f);
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
