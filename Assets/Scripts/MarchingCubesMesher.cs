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

            for (int z = 0; z < chunk.Density; z++)
            {
                for (int y = 0; y < chunk.Density; y++)
                {
                    for (int x = 0; x < chunk.Density; x++)
                    {
                        MarchCell(chunk, new Vector3Int(x, y, z), isoLevel, vertices, triangles);
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
            List<int> triangles)
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

            Vector3[] edgeVertices = new Vector3[12];

            for (int edge = 0; edge < edgeVertices.Length; edge++)
            {
                int a = MarchingCubesLookup.EdgeCorners[edge, 0];
                int b = MarchingCubesLookup.EdgeCorners[edge, 1];

                if (inside[a] == inside[b])
                {
                    continue;
                }

                edgeVertices[edge] = Interpolate(positions[a], positions[b], values[a], values[b], isoLevel);
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
                int vertexIndex = vertices.Count;

                vertices.Add(edgeVertices[edgeC]);
                vertices.Add(edgeVertices[edgeB]);
                vertices.Add(edgeVertices[edgeA]);

                triangles.Add(vertexIndex);
                triangles.Add(vertexIndex + 1);
                triangles.Add(vertexIndex + 2);
            }
        }

        private static Vector3 Interpolate(Vector3 a, Vector3 b, float valueA, float valueB, float isoLevel)
        {
            float denominator = valueB - valueA;

            if (Mathf.Abs(denominator) < Mathf.Epsilon)
            {
                return (a + b) * 0.5f;
            }

            float t = Mathf.InverseLerp(valueA, valueB, isoLevel);
            return Vector3.Lerp(a, b, t);
        }
    }
}
