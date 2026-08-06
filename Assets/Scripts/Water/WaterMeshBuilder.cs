using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DefaultNamespace.Water
{
    public sealed class WaterMeshBuilder
    {
        private readonly WaterGrid _grid;
        private readonly WaterSimulationSettings _settings;
        private readonly List<Vector3> _vertices;
        private readonly List<Vector3> _normals;
        private readonly List<Vector2> _uvs;
        private readonly List<Vector2> _flow;
        private readonly List<Vector2> _waterDepths;
        private readonly List<int> _indices;

        public WaterMeshBuilder(WaterGrid grid, WaterSimulationSettings settings)
        {
            _grid = grid;
            _settings = settings;
            int verticesPerChunk = (grid.CellsPerChunkAxis + 1) * (grid.CellsPerChunkAxis + 1);
            _vertices = new List<Vector3>(verticesPerChunk);
            _normals = new List<Vector3>(verticesPerChunk);
            _uvs = new List<Vector2>(verticesPerChunk);
            _flow = new List<Vector2>(verticesPerChunk);
            _waterDepths = new List<Vector2>(verticesPerChunk);
            _indices = new List<int>(grid.CellsPerChunkAxis * grid.CellsPerChunkAxis * 6);
        }

        public void Build(WaterChunk chunk, Mesh mesh)
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _flow.Clear();
            _waterDepths.Clear();
            _indices.Clear();

            int startX = chunk.Coordinate.x * _grid.CellsPerChunkAxis;
            int startZ = chunk.Coordinate.y * _grid.CellsPerChunkAxis;
            float minX = _grid.WorldBounds.min.x;
            float minZ = _grid.WorldBounds.min.z;

            for (int z = 0; z < _grid.CellsPerChunkAxis; z++)
            {
                for (int x = 0; x < _grid.CellsPerChunkAxis; x++)
                {
                    int cellX = startX + x;
                    int cellZ = startZ + z;

                    if (!IsRenderCell(cellX, cellZ))
                    {
                        continue;
                    }

                    AddCellQuad(chunk, minX, minZ, cellX, cellZ);
                }
            }

            mesh.Clear();
            mesh.name = "Water Chunk";
            mesh.indexFormat = _vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetUVs(1, _flow);
            mesh.SetUVs(2, _waterDepths);
            mesh.SetTriangles(_indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void AddCellQuad(WaterChunk chunk, float minX, float minZ, int cellX, int cellZ)
        {
            float worldX0 = minX + cellX * _grid.CellSize;
            float worldZ0 = minZ + cellZ * _grid.CellSize;
            float worldX1 = worldX0 + _grid.CellSize;
            float worldZ1 = worldZ0 + _grid.CellSize;
            SampleRenderCell(cellX, cellZ, out float height, out float waterDepth, out Vector2 flow);
            int start = _vertices.Count;

            AddCellVertex(chunk, worldX0, worldZ0, height, waterDepth, flow);
            AddCellVertex(chunk, worldX1, worldZ0, height, waterDepth, flow);
            AddCellVertex(chunk, worldX0, worldZ1, height, waterDepth, flow);
            AddCellVertex(chunk, worldX1, worldZ1, height, waterDepth, flow);

            _indices.Add(start);
            _indices.Add(start + 2);
            _indices.Add(start + 1);
            _indices.Add(start + 1);
            _indices.Add(start + 2);
            _indices.Add(start + 3);
        }

        private void AddCellVertex(
            WaterChunk chunk,
            float worldX,
            float worldZ,
            float height,
            float waterDepth,
            Vector2 flow)
        {
            _vertices.Add(new Vector3(worldX - chunk.Bounds.min.x, height - chunk.Bounds.min.y, worldZ - chunk.Bounds.min.z));
            _normals.Add(Vector3.up);
            _uvs.Add(new Vector2(worldX, worldZ));
            _flow.Add(flow);
            _waterDepths.Add(new Vector2(waterDepth, 0f));
        }

        private bool IsWetCell(int x, int z)
        {
            if (!_grid.Contains(x, z))
            {
                return false;
            }

            int index = _grid.Index(x, z);
            return _grid.HasGround(index) && _grid.WaterDepths[index] > _settings.renderDepthThreshold;
        }

        private bool IsRenderCell(int x, int z)
        {
            if (!_grid.Contains(x, z))
            {
                return false;
            }

            int index = _grid.Index(x, z);

            if (!_grid.HasGround(index))
            {
                return false;
            }

            if (_grid.WaterDepths[index] > _settings.renderDepthThreshold)
            {
                return true;
            }

            int skirtCells = Mathf.Max(0, _settings.shorelineSkirtCells);

            if (skirtCells == 0)
            {
                return false;
            }

            for (int zOffset = -skirtCells; zOffset <= skirtCells; zOffset++)
            {
                for (int xOffset = -skirtCells; xOffset <= skirtCells; xOffset++)
                {
                    if (xOffset == 0 && zOffset == 0)
                    {
                        continue;
                    }

                    if (IsWetCell(x + xOffset, z + zOffset))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private float SampleRenderVertex(int vertexX, int vertexZ, out float waterDepth, out Vector2 flow)
        {
            float surfaceSum = 0f;
            float depthSum = 0f;
            Vector2 flowSum = Vector2.zero;
            int count = 0;

            AddRenderableCell(vertexX - 1, vertexZ - 1, ref surfaceSum, ref depthSum, ref flowSum, ref count);
            AddRenderableCell(vertexX, vertexZ - 1, ref surfaceSum, ref depthSum, ref flowSum, ref count);
            AddRenderableCell(vertexX - 1, vertexZ, ref surfaceSum, ref depthSum, ref flowSum, ref count);
            AddRenderableCell(vertexX, vertexZ, ref surfaceSum, ref depthSum, ref flowSum, ref count);

            if (count > 0)
            {
                waterDepth = depthSum / count;
                flow = flowSum / count;
                return surfaceSum / count - _settings.shorelineOverlap;
            }

            AddNearbyWetCell(vertexX - 2, vertexZ - 2, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX - 1, vertexZ - 2, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX, vertexZ - 2, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX + 1, vertexZ - 2, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX - 2, vertexZ - 1, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX + 1, vertexZ - 1, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX - 2, vertexZ, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX + 1, vertexZ, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX - 2, vertexZ + 1, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX - 1, vertexZ + 1, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX, vertexZ + 1, ref surfaceSum, ref flowSum, ref count);
            AddNearbyWetCell(vertexX + 1, vertexZ + 1, ref surfaceSum, ref flowSum, ref count);

            if (count > 0)
            {
                waterDepth = 0f;
                flow = flowSum / count;
                return surfaceSum / count - _settings.shorelineOverlap;
            }

            int fallbackX = Mathf.Clamp(vertexX, 0, _grid.CellsPerAxis - 1);
            int fallbackZ = Mathf.Clamp(vertexZ, 0, _grid.CellsPerAxis - 1);
            int fallbackIndex = _grid.Index(fallbackX, fallbackZ);
            waterDepth = 0f;
            flow = Vector2.zero;
            return _grid.GroundHeights[fallbackIndex];
        }

        private void SampleRenderCell(int x, int z, out float height, out float waterDepth, out Vector2 flow)
        {
            int index = _grid.Index(x, z);

            if (_grid.HasGround(index) && _grid.WaterDepths[index] > _settings.renderDepthThreshold)
            {
                waterDepth = _grid.WaterDepths[index];
                flow = _grid.FlowVelocities[index];
                height = _grid.GroundHeights[index] + waterDepth - _settings.shorelineOverlap;
                return;
            }

            float surfaceSum = 0f;
            Vector2 flowSum = Vector2.zero;
            int count = 0;
            int skirtCells = Mathf.Max(0, _settings.shorelineSkirtCells);

            for (int zOffset = -skirtCells; zOffset <= skirtCells; zOffset++)
            {
                for (int xOffset = -skirtCells; xOffset <= skirtCells; xOffset++)
                {
                    AddNearbyWetCell(x + xOffset, z + zOffset, ref surfaceSum, ref flowSum, ref count);
                }
            }

            waterDepth = 0f;
            flow = count > 0 ? flowSum / count : Vector2.zero;
            height = count > 0 ? surfaceSum / count - _settings.shorelineOverlap : _grid.GroundHeights[index];
        }

        private void AddRenderableCell(
            int x,
            int z,
            ref float surfaceSum,
            ref float depthSum,
            ref Vector2 flowSum,
            ref int count)
        {
            if (!IsWetCell(x, z))
            {
                return;
            }

            int index = _grid.Index(x, z);
            float depth = _grid.WaterDepths[index];
            surfaceSum += _grid.GroundHeights[index] + depth;
            depthSum += depth;
            flowSum += _grid.FlowVelocities[index];
            count++;
        }

        private void AddNearbyWetCell(
            int x,
            int z,
            ref float surfaceSum,
            ref Vector2 flowSum,
            ref int count)
        {
            if (!IsWetCell(x, z))
            {
                return;
            }

            int index = _grid.Index(x, z);
            float depth = _grid.WaterDepths[index];
            surfaceSum += _grid.GroundHeights[index] + depth;
            flowSum += _grid.FlowVelocities[index];
            count++;
        }

    }
}
