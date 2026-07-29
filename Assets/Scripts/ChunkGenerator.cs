using DefaultNamespace;
using UnityEngine;

public readonly struct Point
{
    public Point(Vector3 normalizedPosition, float value)
    {
        NormalizedPosition = normalizedPosition;
        Value = Mathf.Clamp01(value);
    }

    public Vector3 NormalizedPosition { get; }
    public float Value { get; }
}

public readonly struct NoiseConfiguration
{
    public NoiseConfiguration(int octaves, float frequency, float persistence, float lacunarity)
    {
        Octaves = octaves;
        Frequency = frequency;
        Persistence = persistence;
        Lacunarity = lacunarity;
    }

    public int Octaves { get; }
    public float Frequency { get; }
    public float Persistence { get; }
    public float Lacunarity { get; }
}

public readonly struct Chunk
{
    public Chunk(Vector3Int position, float size, int density, NoiseConfiguration noiseConfiguration)
    {
        Position = position;
        Size = Mathf.Max(1, size);
        Density = Mathf.Clamp(density, 1, 64);
        PointsPerEdge = Density + 1;

        int numberOfPoints = PointsPerEdge * PointsPerEdge * PointsPerEdge;
        Points = new Point[numberOfPoints];

        GeneratePoints(noiseConfiguration);
    }

    public Point[] Points { get; }
    public Vector3Int Position { get; } // Global position
    public float Size { get; }
    public int Density { get; }
    public int PointsPerEdge { get; }

    public Point GetPoint(int x, int y, int z)
    {
        return Points[x + PointsPerEdge * (y + PointsPerEdge * z)];
    }

    public Vector3 GetLocalPosition(Point point)
    {
        return point.NormalizedPosition * Size;
    }

    public Vector3 GetGlobalPosition(Point point)
    {
        return (Vector3)Position + GetLocalPosition(point);
    }

    private void GeneratePoints(NoiseConfiguration noiseConfiguration)
    {
        int index = 0;

        for (int z = 0; z < PointsPerEdge; z++)
        {
            for (int y = 0; y < PointsPerEdge; y++)
            {
                for (int x = 0; x < PointsPerEdge; x++)
                {
                    Vector3 normalizedPosition = new Vector3(
                        x / (float)Density,
                        y / (float)Density,
                        z / (float)Density
                    );

                    Vector3 localPosition = normalizedPosition * Size;
                    Vector3 globalPosition = (Vector3)Position + localPosition;
                    float value = PerlinNoise3D.Fractal(globalPosition,
                        noiseConfiguration.Octaves,
                        noiseConfiguration.Frequency,
                        noiseConfiguration.Persistence,
                        noiseConfiguration.Lacunarity);

                    Points[index++] = new Point(normalizedPosition,
                        value);
                }
            }
        }
    }
}

[ExecuteAlways]
public class ChunkGenerator : MonoBehaviour
{
    [SerializeField] private Vector3Int position;
    [SerializeField, Range(1, 64)] private int density;
    [SerializeField, Min(1)] private float size;
    [SerializeField, Range(0f, 1f)] private float isoLevel = 0.5f;
    [SerializeField] private bool autoRegenerateInEditor = true;
    [SerializeField] private Material chunkMaterial;

    [Header("Noise")] [SerializeField] private int octaves = 2;
    [SerializeField] private float frequency = 1f;
    [SerializeField] private float persistence = 0.5f;
    [SerializeField] private float lacunarity = 2f;

    private ChunkDebugRenderer _debugRenderer;
    private GameObject _chunkObject;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private Material _defaultChunkMaterial;
    private Chunk _chunk;

    private void OnEnable()
    {
        if (ShouldAutoRegenerate())
        {
            RefreshChunkPreview();
        }
    }

    private void OnValidate()
    {
        if (ShouldAutoRegenerate())
        {
            RefreshChunkPreview();
        }
    }

    private void Start()
    {
        RefreshChunkPreview();
    }

    [ContextMenu("Clear Chunk")]
    private void ClearChunk()
    {
        if (_debugRenderer == null)
        {
            _debugRenderer = GetComponent<ChunkDebugRenderer>();
        }

        if (_debugRenderer != null)
        {
            _debugRenderer.Clear();
        }

        if (_meshFilter != null)
        {
            _meshFilter.sharedMesh = null;
        }

        if (_mesh != null)
        {
            DestroyGeneratedObject(_mesh);
            _mesh = null;
        }

        if (_defaultChunkMaterial != null)
        {
            DestroyGeneratedObject(_defaultChunkMaterial);
            _defaultChunkMaterial = null;
        }

        if (_chunkObject == null)
        {
            Transform existingChunk = transform.Find("Chunk");
            _chunkObject = existingChunk != null ? existingChunk.gameObject : null;
        }

        if (_chunkObject != null)
        {
            DestroyGeneratedObject(_chunkObject);
            _chunkObject = null;
        }

        _meshFilter = null;
        _meshRenderer = null;
        _chunk = default;
    }

    [ContextMenu("Regenerate Chunk")]
    private void RegenerateChunk()
    {
        ClearChunk();
        RefreshChunkPreview();
    }

    private void GenerateChunk()
    {
        _chunk = new Chunk(position, size, density, new NoiseConfiguration(octaves, frequency, persistence, lacunarity));
    }

    private void InitializeDebugRenderer()
    {
        if (_debugRenderer == null)
        {
            _debugRenderer = GetComponent<ChunkDebugRenderer>();
        }

        if (_debugRenderer == null)
        {
            return;
        }

        _debugRenderer.Initialize(_chunk, isoLevel);
    }

    private void GenerateMesh()
    {
        EnsureChunkObject();
        EnsureMesh();

        MarchingCubesMesher.Generate(_chunk, isoLevel, _mesh);
        _meshFilter.sharedMesh = _mesh;
        _meshRenderer.sharedMaterial = chunkMaterial != null
            ? chunkMaterial
            : GetDefaultChunkMaterial();
    }

    private void EnsureChunkObject()
    {
        if (_chunkObject == null)
        {
            Transform existingChunk = transform.Find("Chunk");
            _chunkObject = existingChunk != null
                ? existingChunk.gameObject
                : new GameObject("Chunk");

            _chunkObject.transform.SetParent(transform, false);
        }

        _chunkObject.transform.localPosition = _chunk.Position;
        _chunkObject.transform.localRotation = Quaternion.identity;
        _chunkObject.transform.localScale = Vector3.one;

        if (!_chunkObject.TryGetComponent(out _meshFilter))
        {
            _meshFilter = _chunkObject.AddComponent<MeshFilter>();
        }

        if (!_chunkObject.TryGetComponent(out _meshRenderer))
        {
            _meshRenderer = _chunkObject.AddComponent<MeshRenderer>();
        }
    }

    private void EnsureMesh()
    {
        if (_mesh != null)
        {
            return;
        }

        _mesh = _meshFilter.sharedMesh;

        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "Marching Cubes Chunk";
        }
    }

    private Material GetDefaultChunkMaterial()
    {
        if (_defaultChunkMaterial != null)
        {
            return _defaultChunkMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        _defaultChunkMaterial = new Material(shader);
        _defaultChunkMaterial.name = "Default Chunk Material";
        _defaultChunkMaterial.color = Color.white;

        return _defaultChunkMaterial;
    }

    private void RefreshChunkPreview()
    {
        GenerateChunk();
        InitializeDebugRenderer();
        GenerateMesh();
    }

    private bool ShouldAutoRegenerate()
    {
        return Application.isPlaying || autoRegenerateInEditor;
    }

    private static void DestroyGeneratedObject(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
