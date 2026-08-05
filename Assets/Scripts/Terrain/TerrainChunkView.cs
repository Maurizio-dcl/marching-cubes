using UnityEngine;

namespace DefaultNamespace.Terrain
{
    [DisallowMultipleComponent]
    public sealed class TerrainChunkView : MonoBehaviour
    {
        public TerrainChunkRuntimeData Data { get; private set; }
        public MeshFilter MeshFilter { get; private set; }
        public MeshRenderer MeshRenderer { get; private set; }
        public TerrainGrassRenderer GrassRenderer { get; private set; }

        public void Initialize(TerrainChunkRuntimeData data)
        {
            Data = data;

            if (!TryGetComponent(out MeshFilter meshFilter))
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            if (!TryGetComponent(out MeshRenderer meshRenderer))
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            if (!TryGetComponent(out TerrainGrassRenderer grassRenderer))
            {
                grassRenderer = gameObject.AddComponent<TerrainGrassRenderer>();
            }

            MeshFilter = meshFilter;
            MeshRenderer = meshRenderer;
            GrassRenderer = grassRenderer;
        }

        public void ApplyMesh(Mesh mesh)
        {
            if (MeshFilter != null)
            {
                MeshFilter.sharedMesh = mesh;
            }
        }

        public void ApplyVisibility(bool visible)
        {
            if (MeshRenderer != null)
            {
                MeshRenderer.enabled = visible;
            }

            if (GrassRenderer != null)
            {
                GrassRenderer.enabled = visible;
            }
        }
    }
}
