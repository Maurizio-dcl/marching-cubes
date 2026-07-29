using UnityEngine;

namespace DefaultNamespace
{
    public class ChunkDebugRenderer : MonoBehaviour
    {
        [SerializeField] private bool drawPoints = true;
        [SerializeField, Range(0.01f, 1f)] private float pointRadius = 0.05f;
        [SerializeField] private bool overridePointColor = true;
        [SerializeField] private Color pointColor = Color.white;

        [SerializeField] private bool drawBounds = true;
        [SerializeField] private Color boundsColor = Color.white;

        private Chunk _chunk;

        public void Initialize(Chunk chunk)
        {
            _chunk = chunk;
        }

        private void OnDrawGizmos()
        {
            if (drawPoints)
                DrawPoints();
            
            if (drawBounds)
                DrawChunkBounds();
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

                if (overridePointColor)
                    Gizmos.color = pointColor;
                else
                {
                    float value = point.Value;
                    Gizmos.color = new Color(value, value, value, 1f);
                }

                Gizmos.DrawSphere(_chunk.GetLocalPosition(point), pointRadius);
            }

            Gizmos.matrix = previousMatrix;
        }

        private void DrawChunkBounds()
        {
            Gizmos.color = boundsColor;

            float size = _chunk.Size;

            // Generated points range from 0 to Size on each axis.
            Vector3 center = Vector3.one * size * 0.5f;
            Vector3 boundsSize = Vector3.one * size;

            Gizmos.DrawWireCube(center, boundsSize);
        }
    }
}