using UnityEngine;

public readonly struct Point
{
    public Point(Vector3 normalizedPosition, float value)
    {
        NormalizedPosition = normalizedPosition;
        Value = value;
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
    public delegate float DensitySampler(Vector3 worldPosition);

    public Chunk(Vector3Int position, float size, int density, NoiseConfiguration noiseConfiguration)
        : this(position, size, density, worldPosition => PerlinNoise3D.Fractal(worldPosition,
            noiseConfiguration.Octaves,
            noiseConfiguration.Frequency,
            noiseConfiguration.Persistence,
            noiseConfiguration.Lacunarity))
    {
    }

    public Chunk(Vector3Int position, float size, int density, DensitySampler densitySampler)
    {
        Position = position;
        Size = Mathf.Max(1, size);
        Density = Mathf.Clamp(density, 1, 64);
        PointsPerEdge = Density + 1;

        int numberOfPoints = PointsPerEdge * PointsPerEdge * PointsPerEdge;
        Points = new Point[numberOfPoints];

        GeneratePoints(densitySampler);
    }

    public Point[] Points { get; }
    public Vector3Int Position { get; }
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

    private void GeneratePoints(DensitySampler densitySampler)
    {
        if (densitySampler == null)
        {
            throw new System.ArgumentNullException(nameof(densitySampler));
        }

        int index = 0;

        for (int z = 0; z < PointsPerEdge; z++)
        {
            for (int y = 0; y < PointsPerEdge; y++)
            {
                for (int x = 0; x < PointsPerEdge; x++)
                {
                    Vector3 normalizedPosition = new(
                        x / (float)Density,
                        y / (float)Density,
                        z / (float)Density);

                    Vector3 localPosition = normalizedPosition * Size;
                    Vector3 globalPosition = (Vector3)Position + localPosition;
                    float value = densitySampler(globalPosition);

                    Points[index++] = new Point(normalizedPosition, value);
                }
            }
        }
    }
}
