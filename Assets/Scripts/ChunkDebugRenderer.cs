using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

namespace DefaultNamespace
{
    public class ChunkDebugRenderer : MonoBehaviour
    {
        [SerializeField] private bool drawOutsidePoints = true;
        [SerializeField] private bool drawInsidePoints = true;
        [SerializeField, Range(0.01f, 1f)] private float pointRadius = 0.05f;
        [SerializeField] private bool overridePointColor = true;
        [SerializeField] private Color pointColor = Color.white;

        [SerializeField] private bool drawBounds = true;
        [SerializeField] private Color boundsColor = Color.white;
        
        [SerializeField] private bool drawCubeBounds;
        [SerializeField] private Color cubeBoundsColor = Color.grey;
        [SerializeField] private bool cubeBoundsHiddenByGeometry = true;

        private Chunk _chunk;
        private float _isoLevel;

        public void Initialize(Chunk chunk, float isoLevel)
        {
            _chunk = chunk;
            _isoLevel = isoLevel;
        }

        public void Clear()
        {
            _chunk = default;
        }

        private void OnDrawGizmos()
        {
            if (drawOutsidePoints || drawInsidePoints)
                DrawPoints();
            
            if (drawBounds)
                DrawChunkBounds();

            if (drawCubeBounds)
                DrawCubeBounds();
        }

        private void DrawPoints()
        {
            Point[] points = _chunk.Points;

            if (points == null)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;

            // Points store normalized positions, so convert them before drawing.
            Gizmos.matrix = Matrix4x4.Translate(_chunk.Position);

            for (int i = 0; i < points.Length; i++)
            {
                Point point = points[i];
                float value = point.Value;
                
                bool isInside = value >= _isoLevel;
                bool shouldDraw = isInside ? drawInsidePoints : drawOutsidePoints;

                if (!shouldDraw)
                    continue;

                if (overridePointColor)
                    Gizmos.color = pointColor;
                else
                {
                    Gizmos.color = new Color(value, value, value, 1f);
                }

                Gizmos.DrawSphere(_chunk.GetLocalPosition(point), pointRadius);
            }

            Gizmos.matrix = previousMatrix;
        }

        private void DrawChunkBounds()
        {
            if (_chunk.Points == null)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.color = boundsColor;
            Gizmos.matrix = Matrix4x4.Translate(_chunk.Position);

            float size = _chunk.Size;

            // Generated points range from 0 to Size on each axis.
            Vector3 center = Vector3.one * size * 0.5f;
            Vector3 boundsSize = Vector3.one * size;

            Gizmos.DrawWireCube(center, boundsSize);
            Gizmos.matrix = previousMatrix;
        }

        private void DrawCubeBounds()
        {
            if (_chunk.Points == null)
                return;

#if UNITY_EDITOR
            if (cubeBoundsHiddenByGeometry)
            {
                DrawDepthTestedCubeBounds();
                return;
            }
#endif
            
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.color = cubeBoundsColor;
            Gizmos.matrix = Matrix4x4.Translate(_chunk.Position);

            float cubeSize = _chunk.Size / _chunk.Density;
            Vector3 boundsSize = Vector3.one * cubeSize;

            for (int z = 0; z < _chunk.Density; z++)
            {
                for (int y = 0; y < _chunk.Density; y++)
                {
                    for (int x = 0; x < _chunk.Density; x++)
                    {
                        Vector3 center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cubeSize;
                        Gizmos.DrawWireCube(center, boundsSize);
                    }
                }
            }

            Gizmos.matrix = previousMatrix;
        }

#if UNITY_EDITOR
        private void DrawDepthTestedCubeBounds()
        {
            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            CompareFunction previousZTest = Handles.zTest;

            Handles.matrix = Matrix4x4.Translate(_chunk.Position);
            Handles.color = cubeBoundsColor;
            Handles.zTest = CompareFunction.LessEqual;

            float cubeSize = _chunk.Size / _chunk.Density;
            Vector3 boundsSize = Vector3.one * cubeSize;

            for (int z = 0; z < _chunk.Density; z++)
            {
                for (int y = 0; y < _chunk.Density; y++)
                {
                    for (int x = 0; x < _chunk.Density; x++)
                    {
                        Vector3 center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cubeSize;
                        Handles.DrawWireCube(center, boundsSize);
                    }
                }
            }

            Handles.zTest = previousZTest;
            Handles.color = previousColor;
            Handles.matrix = previousMatrix;
        }
#endif
    }
}
