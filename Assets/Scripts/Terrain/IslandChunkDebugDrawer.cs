using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DefaultNamespace.Terrain
{
    [DisallowMultipleComponent]
    public sealed class IslandChunkDebugDrawer : MonoBehaviour
    {
        private TerrainChunkView _view;
        private IslandChunkDebugSettings _settings;

        public void Initialize(TerrainChunkView view, IslandChunkDebugSettings settings)
        {
            _view = view;
            _settings = settings;
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (_settings == null || !_settings.enabled || _view == null || _view.Data == null)
            {
                return;
            }

            if (_settings.drawOnlySelected && Selection.activeGameObject != gameObject)
            {
                return;
            }

            TerrainChunkRuntimeData data = _view.Data;

            if (_settings.drawBounds)
            {
                Gizmos.color = _settings.boundsColor;
                Gizmos.DrawWireCube(data.Bounds.center, data.Bounds.size);
            }

            if (_settings.drawSamplePoints && data.Chunk.Points != null)
            {
                DrawSamples(data);
            }

            if (_settings.drawCells && data.Chunk.Points != null)
            {
                DrawCells(data);
            }

            if (_settings.drawNormals && data.Mesh != null)
            {
                DrawNormals(data);
            }
#endif
        }

#if UNITY_EDITOR
        private void DrawSamples(TerrainChunkRuntimeData data)
        {
            Point[] points = data.Chunk.Points;
            int stride = Mathf.Max(1, Mathf.CeilToInt(points.Length / (float)Mathf.Max(1, _settings.maxSamples)));

            for (int i = 0; i < points.Length; i += stride)
            {
                Point point = points[i];
                Gizmos.color = point.Value >= 0f ? _settings.insideSampleColor : _settings.outsideSampleColor;
                Gizmos.DrawSphere(data.Origin + data.Chunk.GetLocalPosition(point), _settings.sampleRadius);
            }
        }

        private void DrawCells(TerrainChunkRuntimeData data)
        {
            int density = data.Chunk.Density;
            int total = density * density * density;
            int stride = Mathf.Max(1, Mathf.CeilToInt(total / (float)Mathf.Max(1, _settings.maxCells)));
            float cellSize = data.Chunk.Size / density;
            Gizmos.color = _settings.cellColor;

            for (int i = 0; i < total; i += stride)
            {
                int x = i % density;
                int y = i / density % density;
                int z = i / (density * density);
                Vector3 center = (Vector3)data.Origin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cellSize;
                Gizmos.DrawWireCube(center, Vector3.one * cellSize);
            }
        }

        private void DrawNormals(TerrainChunkRuntimeData data)
        {
            Vector3[] vertices = data.Mesh.vertices;
            Vector3[] normals = data.Mesh.normals;

            if (vertices == null || normals == null || vertices.Length != normals.Length)
            {
                return;
            }

            int stride = Mathf.Max(1, Mathf.CeilToInt(vertices.Length / (float)Mathf.Max(1, _settings.maxNormals)));
            Handles.color = _settings.normalColor;

            for (int i = 0; i < vertices.Length; i += stride)
            {
                Vector3 start = (Vector3)data.Origin + vertices[i];
                Handles.DrawLine(start, start + normals[i] * 0.35f);
            }
        }
#endif
    }
}
