using DefaultNamespace;
using Unity.VisualScripting;
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

        int pointsPerEdge = Density + 1;
        int numberOfPoints = pointsPerEdge * pointsPerEdge * pointsPerEdge;
        Points = new Point[numberOfPoints];

        GeneratePoints(pointsPerEdge, noiseConfiguration);
    }

    public Point[] Points { get; }
    public Vector3Int Position { get; } // Global position
    public float Size { get; }
    public int Density { get; }

    public Vector3 GetLocalPosition(Point point)
    {
        return point.NormalizedPosition * Size;
    }

    public Vector3 GetGlobalPosition(Point point)
    {
        return Position + GetLocalPosition(point);
    }

    private void GeneratePoints(int pointsPerEdge, NoiseConfiguration noiseConfiguration)
    {
        int index = 0;

        for (int z = 0; z < pointsPerEdge; z++)
        {
            for (int y = 0; y < pointsPerEdge; y++)
            {
                for (int x = 0; x < pointsPerEdge; x++)
                {
                    Vector3 normalizedPosition = new Vector3(
                        x / (float)Density,
                        y / (float)Density,
                        z / (float)Density
                    );

                    Vector3 localPosition = normalizedPosition * Size;
                    Vector3 globalPosition = Position + localPosition;
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

public class ChunkGenerator : MonoBehaviour
{
    [SerializeField] private Vector3Int position;
    [SerializeField, Range(1, 64)] private int density;
    [SerializeField, Min(1)] private float size;

    [Header("Noise")] [SerializeField] private int octaves = 2;
    [SerializeField] private float frequency = 1f;
    [SerializeField] private float persistence = 0.5f;
    [SerializeField] private float lacunarity = 2f;

    private ChunkDebugRenderer _debugRenderer;
    private Chunk _chunk;
    private GameObject _chunkGo;

    private void OnValidate()
    {
        RefreshChunkPreview();
    }

    private void Start()
    {
        RefreshChunkPreview();
        InstantiateChunk();
    }

    private void GenerateChunk()
    {
        _chunk = new Chunk(position, size, density, new NoiseConfiguration(octaves, frequency, persistence, lacunarity));
    }

    private void InstantiateChunk()
    {
        _chunkGo = new GameObject("Chunk");
        _chunkGo.transform.parent = transform;
        _chunkGo.transform.localPosition = _chunk.Position;
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

        _debugRenderer.Initialize(_chunk);
    }

    private void RefreshChunkPreview()
    {
        GenerateChunk();
        InitializeDebugRenderer();
    }
}