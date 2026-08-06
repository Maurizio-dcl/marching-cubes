using DefaultNamespace;
using DefaultNamespace.Terrain;
using DefaultNamespace.Water;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum IslandBoundsMode
{
    ClampShapeToChunkGrid,
    AllowOverflow
}

[System.Serializable]
public struct TerrainShapeNoise
{
    [Min(0f)] public float shapeNoiseFrequency;
    [Range(0f, 1f)] public float shapeNoiseStrength;
}

[System.Serializable]
public struct TerraceFeature
{
    public bool enabled;
    public Vector2 position;
    [Min(0.001f)] public float radius;
    [Min(0f)] public float height;
    [Min(0.001f)] public float sharpness;
    [Range(0f, 1f)] public float plateauRadius;
    public TerrainShapeNoise shapeNoise;
}

[System.Serializable]
public struct BasinFeature
{
    public bool enabled;
    public Vector2 position;
    [Min(0.001f)] public float radius;
    [Min(0f)] public float depth;
    [Min(0.001f)] public float sharpness;
    [Range(0f, 1f)] public float plateauRadius;
    public TerrainShapeNoise shapeNoise;
}

[System.Serializable]
public struct LakeFeature
{
    public bool enabled;
    public Vector2 position;
    [Min(0.001f)] public float radius;
    [Min(0f)] public float basinDepth;
    [Min(0.001f)] public float sharpness;
    [Min(0f)] public float shoreWidth;
    public float waterSurfaceHeight;
    public TerrainShapeNoise shapeNoise;
}

public enum RiverStartMode
{
    EdgeToEdge,
    LakeToEdge
}

[System.Serializable]
public struct RiverFeature
{
    public bool enabled;
    public RiverStartMode startMode;
    [Min(0)] public int sourceLakeIndex;
    public Vector2 startPosition;
    public Vector2 endPosition;
    [Min(0.001f)] public float width;
    [Min(0f)] public float depth;
    [Min(0.001f)] public float bankSharpness;
    public float waterSurfaceHeight;
    public float endWaterSurfaceHeight;
    [Min(0f)] public float meanderFrequency;
    [Range(0f, 1f)] public float meanderStrength;
}

public readonly struct GeneratedWaterBody
{
    public GeneratedWaterBody(
        Vector2 localPosition,
        float radius,
        float shoreWidth,
        float surfaceHeight,
        float shapeNoiseFrequency,
        float shapeNoiseStrength)
    {
        LocalPosition = localPosition;
        Radius = radius;
        ShoreWidth = shoreWidth;
        SurfaceHeight = surfaceHeight;
        ShapeNoiseFrequency = shapeNoiseFrequency;
        ShapeNoiseStrength = shapeNoiseStrength;
    }

    public Vector2 LocalPosition { get; }
    public float Radius { get; }
    public float ShoreWidth { get; }
    public float SurfaceHeight { get; }
    public float ShapeNoiseFrequency { get; }
    public float ShapeNoiseStrength { get; }
}

public readonly struct GeneratedRiver
{
    public GeneratedRiver(
        Vector2 localStart,
        Vector2 localEnd,
        float width,
        float depth,
        float bankSharpness,
        float startSurfaceHeight,
        float endSurfaceHeight,
        float meanderFrequency,
        float meanderStrength)
    {
        LocalStart = localStart;
        LocalEnd = localEnd;
        Width = width;
        Depth = depth;
        BankSharpness = bankSharpness;
        StartSurfaceHeight = startSurfaceHeight;
        EndSurfaceHeight = endSurfaceHeight;
        MeanderFrequency = meanderFrequency;
        MeanderStrength = meanderStrength;
    }

    public Vector2 LocalStart { get; }
    public Vector2 LocalEnd { get; }
    public float Width { get; }
    public float Depth { get; }
    public float BankSharpness { get; }
    public float StartSurfaceHeight { get; }
    public float EndSurfaceHeight { get; }
    public float MeanderFrequency { get; }
    public float MeanderStrength { get; }
}

[ExecuteAlways]
public sealed class IslandGenerator : MonoBehaviour, ITerrainDensityField
{
    private const string ChunkObjectNamePrefix = "Island Chunk ";
    private const int MaxTerrainWaterBodyShaderCount = 16;
    private const int MaxTerrainWaterRiverShaderCount = 16;
    private static readonly int TerrainWaterBodyCountId = Shader.PropertyToID("_TerrainWaterBodyCount");
    private static readonly int TerrainWaterBodiesId = Shader.PropertyToID("_TerrainWaterBodies");
    private static readonly int TerrainWaterBodyShapeDataId = Shader.PropertyToID("_TerrainWaterBodyShapeData");
    private static readonly int TerrainWaterRiverCountId = Shader.PropertyToID("_TerrainWaterRiverCount");
    private static readonly int TerrainWaterRiversId = Shader.PropertyToID("_TerrainWaterRivers");
    private static readonly int TerrainWaterRiverDataId = Shader.PropertyToID("_TerrainWaterRiverData");
    private static readonly int TerrainWaterRiverShapeDataId = Shader.PropertyToID("_TerrainWaterRiverShapeData");
    private static readonly int TerrainWaterWorldToIslandId = Shader.PropertyToID("_TerrainWaterWorldToIsland");
    private static readonly int TerrainWaterIslandCenterId = Shader.PropertyToID("_TerrainWaterIslandCenter");
    private static readonly int TerrainWaterNoiseSeedOffsetsId = Shader.PropertyToID("_TerrainWaterNoiseSeedOffsets");
    private static int s_nextRuntimeIslandId = 1;

    [Header("Chunk Grid")]
    [SerializeField] private int seed = 12345;
    [SerializeField, Min(1)] private int chunksPerAxis = 2;
    [SerializeField, Range(1, 64)] private int density = 16;
    [SerializeField, Min(1)] private int chunkSize = 8;
    [SerializeField, Range(-32f, 32f)] private float isoLevel = 0f;
    [SerializeField] private IslandBoundsMode boundsMode = IslandBoundsMode.ClampShapeToChunkGrid;
    [SerializeField, Min(0f)] private float boundsPadding = 1f;
    [SerializeField] private bool autoRegenerateInEditor = true;
    [SerializeField] private bool interpolate = true;
    [SerializeField] private Material islandMaterial;

    [Header("Island Shape")]
    [SerializeField, Min(0.001f)] private float islandRadius = 7f;
    [SerializeField, Min(0.001f)] private float islandDepth = 7f;
    [SerializeField] private float baseSurfaceHeight;
    [SerializeField] private bool useUndersideProfileCurve = true;
    [SerializeField, Min(0.001f)] private float undersideExponent = 1.8f;
    [SerializeField] private AnimationCurve undersideProfile = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField, Range(0f, 1f)] private float edgeFalloff = 0.18f;
    [SerializeField, Min(0f)] private float edgeDrop = 1.5f;

    [Header("Noise")]
    [SerializeField, Min(0f)] private float footprintNoiseFrequency = 0.18f;
    [SerializeField, Range(0f, 1f)] private float footprintNoiseStrength = 0.2f;
    [SerializeField, Min(0f)] private float topNoiseFrequency = 0.12f;
    [SerializeField, Min(0f)] private float topNoiseStrength = 0.6f;
    [SerializeField, Min(0f)] private float undersideNoiseFrequency = 0.22f;
    [SerializeField, Min(0f)] private float undersideNoiseStrength = 1.2f;

    [Header("Terrain Features")]
    [SerializeField] private List<TerraceFeature> terraces = new();
    [SerializeField] private List<BasinFeature> basins = new();
    [SerializeField] private List<LakeFeature> lakes = new();
    [SerializeField] private List<RiverFeature> rivers = new();
    [SerializeField] private Vector2Int randomFeatureCountRange = new(3, 7);
    [SerializeField, Min(0f)] private float terraceWeight = 0.15f;
    [SerializeField, Min(0f)] private float basinWeight = 0.1f;
    [SerializeField, Min(0f)] private float lakeWeight = 0.12f;
    [SerializeField, Min(0f)] private float riverWeight = 0.08f;
    [SerializeField] private Vector2 featureRadiusRange = new(3f, 7f);
    [SerializeField] private Vector2 featureHeightRange = new(1.5f, 5f);
    [SerializeField] private Vector2 featureSharpnessRange = new(1f, 4f);
    [SerializeField] private Vector2 riverWidthRange = new(0.7f, 1.4f);
    [SerializeField] private Vector2 riverDepthRange = new(0.4f, 1.4f);
    [SerializeField, Range(0f, 1f)] private float featureOverlap = 0.55f;
    [SerializeField, Range(0f, 1f)] private float featureEdgePadding = 0.18f;
    [SerializeField, Min(1)] private int terraceStepCount = 4;
    [SerializeField, Range(0f, 1f)] private float terraceSmoothing = 0.15f;

    [Header("Connected Geometry")]
    [SerializeField] private bool removeDetachedGeometry = true;

    [Header("Water")]
    [SerializeField] private bool generateWater = true;

    [Header("LOD and Scheduling")]
    [SerializeField, Tooltip("Runtime only. Editor previews are always generated at full terrain density.")]
    private bool enableChunkLod = true;
    [SerializeField, Min(0)] private int recentlyVisibleFrameHold = 90;
    [SerializeField, Min(1)] private int maxChunkBuildsPerFrame = 2;
    [SerializeField] private Transform lodFocus;
    [SerializeField] private TerrainLodLevel[] lodLevels =
    {
        new() { enterDistance = 28f, exitDistance = 34f, terrainCellsPerAxis = 16, waterCellsPerTerrainChunkAxis = 32, meshUpdateIntervalFrames = 1, simulationIntervalFrames = 1, simulateWater = true, renderWater = true, castShadows = true },
        new() { enterDistance = 54f, exitDistance = 64f, terrainCellsPerAxis = 8, waterCellsPerTerrainChunkAxis = 16, meshUpdateIntervalFrames = 8, simulationIntervalFrames = 2, simulateWater = true, renderWater = true, castShadows = false },
        new() { enterDistance = 96f, exitDistance = 112f, terrainCellsPerAxis = 4, waterCellsPerTerrainChunkAxis = 8, meshUpdateIntervalFrames = 24, simulationIntervalFrames = 8, simulateWater = false, renderWater = false, castShadows = false }
    };

    [Header("Debug")]
    [SerializeField] private IslandChunkDebugSettings chunkDebug = new();

    [Header("Grass")]
    [SerializeField] private bool renderGrass;
    [SerializeField] private ComputeShader grassComputeShader;
    [SerializeField] private Material grassMaterial;
    [SerializeField, Range(0f, 1f)] private float grassDensity = 0.35f;
    [SerializeField, Min(0)] private int grassBoundaryPaddingTexels = 1;
    [SerializeField, Min(1)] private int minGrassHeightTexels = 2;
    [SerializeField, Min(1)] private int maxGrassHeightTexels = 5;
    [SerializeField, Min(1)] private int maxGrassBladesPerChunk = 65536;
    [SerializeField, Min(1)] private int maxGrassCandidatesPerTriangle = 64;
    [SerializeField] private int grassSeed = 1;
    [SerializeField] private bool grassWindEnabled = true;
    [SerializeField] private Vector2 grassWindDirection = new(1f, 0.25f);
    [SerializeField, Min(0f)] private float grassWindStrength = 0.12f;
    [SerializeField, Min(0f)] private float grassWindSpeed = 1.5f;
    [SerializeField, Min(0f)] private float grassWindVariation = 0.85f;
    [SerializeField] private Color[] grassColors =
    {
        new(0.33f, 0.68f, 0.24f, 1f),
        new(0.24f, 0.55f, 0.18f, 1f),
        new(0.47f, 0.77f, 0.29f, 1f)
    };

    private readonly List<GameObject> _chunkObjects = new();
    private readonly List<Mesh> _meshes = new();
    private readonly List<TerrainChunkRuntimeData> _terrainChunks = new();
    private readonly Dictionary<Vector3Int, TerrainChunkRuntimeData> _terrainChunksByCoordinate = new();
    private readonly IslandWorkScheduler _workScheduler = new();
    private readonly Vector4[] _terrainWaterBodyShaderData = new Vector4[MaxTerrainWaterBodyShaderCount];
    private readonly Vector4[] _terrainWaterBodyShapeShaderData = new Vector4[MaxTerrainWaterBodyShaderCount];
    private readonly Vector4[] _terrainWaterRiverShaderData = new Vector4[MaxTerrainWaterRiverShaderCount];
    private readonly Vector4[] _terrainWaterRiverData = new Vector4[MaxTerrainWaterRiverShaderCount];
    private readonly Vector4[] _terrainWaterRiverShapeData = new Vector4[MaxTerrainWaterRiverShaderCount];
    private TerraceFeature[] _terraces = System.Array.Empty<TerraceFeature>();
    private BasinFeature[] _basins = System.Array.Empty<BasinFeature>();
    private LakeFeature[] _lakes = System.Array.Empty<LakeFeature>();
    private GeneratedRiver[] _rivers = System.Array.Empty<GeneratedRiver>();
    private GeneratedWaterBody[] _waterBodies = System.Array.Empty<GeneratedWaterBody>();
    private MaterialPropertyBlock _terrainPropertyBlock;
    private Material _defaultIslandMaterial;
    private Material _defaultGrassMaterial;
    private WaterSimulation _waterSimulation;
    private TerrainLodSelector _lodSelector;
    private Plane[] _frustumPlanes = new Plane[6];
    private int _runtimeIslandId;
    private float _totalSize;
    private Vector3Int _gridOrigin;
    private Vector3 _islandCenter;
#if UNITY_EDITOR
    private bool _hasPendingEditorRefresh;
#endif

    private void OnEnable()
    {
        EnsureRuntimeIslandId();
        ResolveDefaultGrassAssets();

        if (ShouldAutoRegenerate())
        {
            RequestRefreshIslandPreview();
            return;
        }

        RefreshTerrainWaterBodyRenderers();
    }

    private void OnValidate()
    {
        EnsureRuntimeIslandId();
        chunksPerAxis = Mathf.Max(1, chunksPerAxis);
        chunkSize = Mathf.Max(1, chunkSize);
        featureRadiusRange = SortMinMax(featureRadiusRange, 0.001f);
        featureHeightRange = SortMinMax(featureHeightRange, 0f);
        featureSharpnessRange = SortMinMax(featureSharpnessRange, 0.001f);
        riverWidthRange = SortMinMax(riverWidthRange, 0.001f);
        riverDepthRange = SortMinMax(riverDepthRange, 0f);
        randomFeatureCountRange = SortMinMax(randomFeatureCountRange, 0);
        maxGrassHeightTexels = Mathf.Max(minGrassHeightTexels, maxGrassHeightTexels);
        ApplyBoundsConstraints();
        ResolveDefaultGrassAssets();

        if (ShouldAutoRegenerate())
        {
            RequestRefreshIslandPreview();
            return;
        }

        RefreshTerrainWaterBodyRenderers();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        if (_hasPendingEditorRefresh)
        {
            EditorApplication.delayCall -= RefreshIslandPreviewFromEditorDelay;
            _hasPendingEditorRefresh = false;
        }
#endif
    }

    private void Start()
    {
        EnsureRuntimeIslandId();
        RequestRefreshIslandPreview();
    }

    private void Update()
    {
        UpdateLodAndScheduledWork();
    }

    [ContextMenu("Regenerate Island")]
    private void RegenerateIsland()
    {
        ClearIsland();
        RefreshIslandPreview();
    }

    [ContextMenu("Add Default Terrain Feature")]
    public void AddDefaultTerrainFeature()
    {
        terraces.Add(CreateDefaultTerrace(Vector2.zero));
        RequestRefreshIslandPreview();
    }

    public void AddTerraceFeature()
    {
        terraces.Add(CreateDefaultTerrace(Vector2.zero));
        RequestRefreshIslandPreview();
    }

    public void AddBasinFeature()
    {
        basins.Add(CreateDefaultBasin(Vector2.zero));
        RequestRefreshIslandPreview();
    }

    public void AddLakeFeature()
    {
        lakes.Add(CreateDefaultLake(Vector2.zero));
        RequestRefreshIslandPreview();
    }

    public void AddRiverFeature()
    {
        rivers.Add(CreateDefaultRiver());
        RequestRefreshIslandPreview();
    }

    [ContextMenu("Randomize Island Seed")]
    public void RandomizeIslandSeed()
    {
        seed = Random.Range(int.MinValue, int.MaxValue);
        RequestRefreshIslandPreview();
    }

    [ContextMenu("Generate Random Terrain Features")]
    public void GenerateRandomTerrainFeatures()
    {
        ApplyBoundsConstraints();
        InitializeGrid();

        ClearFeatureLists();
        int count = Random.Range(randomFeatureCountRange.x, randomFeatureCountRange.y + 1);
        int generationSeed = Random.Range(int.MinValue, int.MaxValue);
        GenerateRandomFeatures(count, generationSeed);
        RequestRefreshIslandPreview();
    }

    [ContextMenu("Generate Seed Terrain Features")]
    public void GenerateSeedTerrainFeatures()
    {
        ApplyBoundsConstraints();
        InitializeGrid();

        ClearFeatureLists();
        System.Random random = new(seed);
        int count = random.Next(randomFeatureCountRange.x, randomFeatureCountRange.y + 1);
        GenerateRandomFeatures(count, seed);
        RequestRefreshIslandPreview();
    }

    [ContextMenu("Clear Editable Terrain Features")]
    public void ClearEditableTerrainFeatures()
    {
        ClearFeatureLists();
        RequestRefreshIslandPreview();
    }

    [ContextMenu("Clear Island")]
    private void ClearIsland()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (!child.name.StartsWith(ChunkObjectNamePrefix))
            {
                continue;
            }

            if (child.TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
            {
                DestroyGeneratedObject(meshFilter.sharedMesh);
                meshFilter.sharedMesh = null;
            }

            DestroyGeneratedObject(child.gameObject);
        }

        for (int i = _chunkObjects.Count - 1; i >= 0; i--)
        {
            DestroyGeneratedObject(_chunkObjects[i]);
        }

        for (int i = _meshes.Count - 1; i >= 0; i--)
        {
            DestroyGeneratedObject(_meshes[i]);
        }

        if (_defaultIslandMaterial != null)
        {
            DestroyGeneratedObject(_defaultIslandMaterial);
            _defaultIslandMaterial = null;
        }

        if (_defaultGrassMaterial != null)
        {
            DestroyGeneratedObject(_defaultGrassMaterial);
            _defaultGrassMaterial = null;
        }

        _chunkObjects.Clear();
        _meshes.Clear();
        _terrainChunks.Clear();
        _terrainChunksByCoordinate.Clear();
        _workScheduler.Clear();
        _waterSimulation = null;
        _terraces = System.Array.Empty<TerraceFeature>();
        _basins = System.Array.Empty<BasinFeature>();
        _lakes = System.Array.Empty<LakeFeature>();
        _rivers = System.Array.Empty<GeneratedRiver>();
        _waterBodies = System.Array.Empty<GeneratedWaterBody>();
    }

    public float SampleDensity(Vector3 worldPosition)
    {
        Vector3 localPosition = worldPosition - _islandCenter;
        float radialDistance = new Vector2(localPosition.x, localPosition.z).magnitude;
        float localRadius = EvaluateLocalRadius(localPosition);
        float radius01 = Mathf.Clamp01(radialDistance / Mathf.Max(localRadius, 0.001f));
        float topHeight = EvaluateTopHeight(localPosition, radius01);
        float bottomHeight = EvaluateBottomHeight(localPosition, radius01);
        float verticalDensity = Mathf.Min(topHeight - worldPosition.y, worldPosition.y - bottomHeight);
        float edgeDensity = localRadius - radialDistance;

        return Mathf.Min(verticalDensity, edgeDensity);
    }

    public float IsoLevel => isoLevel;
    public Vector3 GridOrigin => _gridOrigin;
    public Vector3 IslandCenter => _islandCenter;
    public float ChunkSize => chunkSize;
    public int ChunksPerAxis => chunksPerAxis;
    public int TerrainCellsPerAxis => density;
    public float TotalSize => _totalSize;
    public Bounds WorldBounds => new((Vector3)_gridOrigin + Vector3.one * (_totalSize * 0.5f), Vector3.one * _totalSize);
    public GeneratedWaterBody[] WaterBodies => _waterBodies;
    private bool IsChunkLodActive => enableChunkLod && Application.isPlaying;

    public float EvaluateLocalRadius(Vector3 islandLocalPosition)
    {
        if (footprintNoiseStrength <= 0f || footprintNoiseFrequency <= 0f)
        {
            return islandRadius;
        }

        Vector2 direction = new(islandLocalPosition.x, islandLocalPosition.z);

        if (direction.sqrMagnitude > 0.000001f)
        {
            direction.Normalize();
        }

        float angleSampleX = direction.x * 3.17f + SeedOffset(11);
        float angleSampleZ = direction.y * 3.17f + SeedOffset(17);
        float noise = SampleSignedNoise2D(angleSampleX, angleSampleZ, footprintNoiseFrequency);
        float multiplier = 1f + noise * footprintNoiseStrength;

        return Mathf.Max(0.001f, islandRadius * multiplier);
    }

    public float EvaluateTopHeight(Vector3 islandLocalPosition, float radius01)
    {
        float unfeaturedTopHeight = EvaluateBaseTopHeight(islandLocalPosition, radius01);
        float featureHeight = EvaluateTerrainFeatures(new Vector2(islandLocalPosition.x, islandLocalPosition.z))
            * InteriorFeatureMask(radius01);

        return unfeaturedTopHeight + featureHeight;
    }

    public float EvaluateBottomHeight(Vector3 islandLocalPosition, float radius01)
    {
        return EvaluateBottomHeight(islandLocalPosition, radius01, EvaluateBaseTopHeight(islandLocalPosition, radius01));
    }

    private float EvaluateBaseTopHeight(Vector3 islandLocalPosition, float radius01)
    {
        float height = baseSurfaceHeight;

        if (topNoiseStrength > 0f && topNoiseFrequency > 0f)
        {
            float interiorMask = InteriorFeatureMask(radius01);
            height += SampleSignedNoise2D(
                islandLocalPosition.x + SeedOffset(23),
                islandLocalPosition.z + SeedOffset(29),
                topNoiseFrequency) * topNoiseStrength * interiorMask;
        }

        height -= EdgeBlend(radius01) * edgeDrop;

        return height;
    }

    private float EvaluateBottomHeight(Vector3 islandLocalPosition, float radius01, float topHeight)
    {
        float profile = useUndersideProfileCurve && undersideProfile != null && undersideProfile.length > 0
            ? Mathf.Clamp01(undersideProfile.Evaluate(radius01))
            : Mathf.Pow(1f - Mathf.Clamp01(radius01), undersideExponent);

        float depth = islandDepth * profile;
        float undersideNoise = 0f;

        if (undersideNoiseStrength > 0f && undersideNoiseFrequency > 0f)
        {
            float mask = Mathf.SmoothStep(0f, 1f, 1f - radius01);
            undersideNoise = SampleSignedNoise2D(
                islandLocalPosition.x + SeedOffset(37),
                islandLocalPosition.z + SeedOffset(41),
                undersideNoiseFrequency) * undersideNoiseStrength * mask;
        }

        float bottomHeight = topHeight - depth + undersideNoise;
        float minimumGap = Mathf.Lerp(0.1f, islandDepth, Mathf.SmoothStep(0f, 1f, 1f - radius01));

        return Mathf.Min(bottomHeight, topHeight - minimumGap);
    }

    public float EvaluateTerrainFeatures(Vector2 islandLocalXZ)
    {
        float positiveHeight = 0f;
        float basinDepth = 0f;

        for (int i = 0; i < _terraces.Length; i++)
        {
            TerraceFeature terrace = _terraces[i];
            Vector2 offset = islandLocalXZ - terrace.position;
            float distance01 = EvaluateFeatureDistance01(offset, terrace.position, terrace.radius, terrace.shapeNoise);

            if (distance01 >= 1f)
            {
                continue;
            }

            float terraceFalloff = EvaluatePlateauFalloff(distance01, terrace.plateauRadius, terrace.sharpness);
            positiveHeight = Mathf.Max(positiveHeight, QuantizeTerrace(terraceFalloff) * terrace.height);
        }

        for (int i = 0; i < _basins.Length; i++)
        {
            BasinFeature basin = _basins[i];
            Vector2 offset = islandLocalXZ - basin.position;
            float distance01 = EvaluateFeatureDistance01(offset, basin.position, basin.radius, basin.shapeNoise);

            if (distance01 < 1f)
            {
                basinDepth = Mathf.Max(
                    basinDepth,
                    EvaluatePlateauFalloff(distance01, basin.plateauRadius, basin.sharpness) * basin.depth);
            }
        }

        for (int i = 0; i < _lakes.Length; i++)
        {
            LakeFeature lake = _lakes[i];
            Vector2 offset = islandLocalXZ - lake.position;
            float distance01 = EvaluateFeatureDistance01(offset, lake.position, lake.radius, lake.shapeNoise);

            if (distance01 < 1f)
            {
                float plateauRadius = Mathf.Clamp01((lake.radius - lake.shoreWidth) / lake.radius);
                basinDepth = Mathf.Max(
                    basinDepth,
                    EvaluatePlateauFalloff(distance01, plateauRadius, lake.sharpness) * lake.basinDepth);
            }
        }

        for (int i = 0; i < _rivers.Length; i++)
        {
            GeneratedRiver river = _rivers[i];
            float riverFalloff = EvaluateRiverFalloff(islandLocalXZ, river);
            basinDepth = Mathf.Max(basinDepth, riverFalloff * river.Depth);
        }

        return positiveHeight - basinDepth;
    }

    private float EvaluateFeatureDistance01(Vector2 offset, Vector2 position, float featureRadius, TerrainShapeNoise shapeNoise)
    {
        float radius = Mathf.Max(0.001f, featureRadius);

        if (shapeNoise.shapeNoiseStrength > 0f && shapeNoise.shapeNoiseFrequency > 0f)
        {
            float noise = SampleSignedNoise2D(
                offset.x + position.x + SeedOffset(53),
                offset.y + position.y + SeedOffset(59),
                shapeNoise.shapeNoiseFrequency);
            radius *= Mathf.Max(0.001f, 1f + noise * shapeNoise.shapeNoiseStrength);
        }

        return offset.magnitude / radius;
    }

    private float EvaluatePlateauFalloff(float distance01, float featurePlateauRadius, float featureSharpness)
    {
        float plateauRadius = Mathf.Clamp01(featurePlateauRadius);

        if (distance01 <= plateauRadius)
        {
            return 1f;
        }

        float edge01 = Mathf.InverseLerp(1f, plateauRadius, distance01);
        return Mathf.Pow(Mathf.SmoothStep(0f, 1f, edge01), Mathf.Max(0.001f, featureSharpness));
    }

    private float EvaluateRiverFalloff(Vector2 islandLocalXZ, GeneratedRiver river)
    {
        float width = Mathf.Max(0.001f, river.Width);
        float distance = DistanceToRiverCenterLine(islandLocalXZ, river, out _);
        float distance01 = Mathf.Clamp01(distance / width);
        return Mathf.Pow(Mathf.SmoothStep(1f, 0f, distance01), Mathf.Max(0.001f, river.BankSharpness));
    }

    private float DistanceToRiverCenterLine(Vector2 islandLocalXZ, GeneratedRiver river, out float t)
    {
        Vector2 start = river.LocalStart;
        Vector2 end = river.LocalEnd;
        Vector2 segment = end - start;
        float lengthSqr = segment.sqrMagnitude;

        if (lengthSqr <= 0.000001f)
        {
            t = 0f;
            return (islandLocalXZ - start).magnitude;
        }

        t = Mathf.Clamp01(Vector2.Dot(islandLocalXZ - start, segment) / lengthSqr);
        Vector2 center = Vector2.Lerp(start, end, t);

        if (river.MeanderStrength > 0f && river.MeanderFrequency > 0f)
        {
            Vector2 normal = new Vector2(-segment.y, segment.x).normalized;
            float meander = SampleSignedNoise2D(
                center.x + SeedOffset(67),
                center.y + SeedOffset(71),
                river.MeanderFrequency);
            center += normal * (meander * river.Width * river.MeanderStrength);
        }

        return (islandLocalXZ - center).magnitude;
    }

    private void RefreshIslandPreview()
    {
        using (IslandProfiler.Refresh.Auto())
        {
            ApplyBoundsConstraints();
            ClearIsland();
            InitializeGrid();
            GenerateFeatures();
            GenerateChunks();
            GenerateWater();
        }
    }

    private void RequestRefreshIslandPreview()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (_hasPendingEditorRefresh)
            {
                return;
            }

            _hasPendingEditorRefresh = true;
            EditorApplication.delayCall += RefreshIslandPreviewFromEditorDelay;
            return;
        }
#endif

        RefreshIslandPreview();
    }

#if UNITY_EDITOR
    private void RefreshIslandPreviewFromEditorDelay()
    {
        EditorApplication.delayCall -= RefreshIslandPreviewFromEditorDelay;
        _hasPendingEditorRefresh = false;

        if (this == null || !isActiveAndEnabled || !autoRegenerateInEditor)
        {
            return;
        }

        RefreshIslandPreview();
    }
#endif

    private void InitializeGrid()
    {
        EnsureRuntimeIslandId();
        _totalSize = chunksPerAxis * chunkSize;
        int origin = Mathf.FloorToInt(_totalSize * -0.5f);
        _gridOrigin = new Vector3Int(origin, origin, origin);
        _islandCenter = (Vector3)_gridOrigin + Vector3.one * (_totalSize * 0.5f);
        _islandCenter.y = baseSurfaceHeight;
    }

    private void EnsureRuntimeIslandId()
    {
        if (_runtimeIslandId != 0)
        {
            return;
        }

        _runtimeIslandId = s_nextRuntimeIslandId++;
    }

    private void GenerateChunks()
    {
        using (IslandProfiler.GenerateChunks.Auto())
        {
            GenerateChunksInternal();
        }
    }

    private void GenerateChunksInternal()
    {
        Material material = islandMaterial != null ? islandMaterial : GetDefaultIslandMaterial();
        List<GeneratedChunkMesh> generatedChunks = new(chunksPerAxis * chunksPerAxis * chunksPerAxis);
        Camera camera = IsChunkLodActive ? ResolveLodCamera() : null;
        Vector3 focus = IsChunkLodActive ? ResolveLodFocus(camera) : Vector3.zero;
        _lodSelector = new TerrainLodSelector(lodLevels, recentlyVisibleFrameHold);

        for (int z = 0; z < chunksPerAxis; z++)
        {
            for (int y = 0; y < chunksPerAxis; y++)
            {
                for (int x = 0; x < chunksPerAxis; x++)
                {
                    Vector3Int chunkPosition = _gridOrigin + new Vector3Int(
                        x * chunkSize,
                        y * chunkSize,
                        z * chunkSize);
                    Vector3Int coordinate = new(x, y, z);
                    Bounds bounds = new((Vector3)chunkPosition + Vector3.one * (chunkSize * 0.5f), Vector3.one * chunkSize);
                    TerrainChunkRuntimeData chunkData = new(new TerrainChunkId(_runtimeIslandId, coordinate), chunkPosition, bounds);
                    int lod = IsChunkLodActive
                        ? SelectInitialLod(chunkData, camera, focus)
                        : 0;
                    chunkData.DesiredLod = lod;
                    chunkData.CurrentLod = lod;
                    _terrainChunks.Add(chunkData);
                    _terrainChunksByCoordinate.Add(coordinate, chunkData);

                    Chunk chunk = new(chunkPosition, chunkSize, GetTerrainDensityForLod(lod), SampleDensity);
                    chunkData.Chunk = chunk;
                    Mesh mesh = new();
                    mesh.name = $"Island Chunk {x} {y} {z}";
                    using (IslandProfiler.MeshExtraction.Auto())
                    {
                        MarchingCubesMesher.Generate(chunk, isoLevel, mesh, interpolate);
                    }
                    chunkData.Mesh = mesh;
                    chunkData.ClearDirty();

                    generatedChunks.Add(new GeneratedChunkMesh(chunkPosition, mesh, chunkData));
                }
            }
        }

        RemoveDetachedComponents(generatedChunks);

        for (int i = 0; i < generatedChunks.Count; i++)
        {
            GeneratedChunkMesh generatedChunk = generatedChunks[i];
            Mesh mesh = generatedChunk.Mesh;

            if (mesh == null || mesh.GetIndexCount(0) == 0)
            {
                DestroyGeneratedObject(mesh);
                continue;
            }

            GameObject chunkObject = new(mesh.name);
            chunkObject.transform.SetParent(transform, false);
            chunkObject.transform.localPosition = generatedChunk.Position;
            chunkObject.transform.localRotation = Quaternion.identity;
            chunkObject.transform.localScale = Vector3.one;

            TerrainChunkView chunkView = chunkObject.AddComponent<TerrainChunkView>();
            chunkView.Initialize(generatedChunk.Data);
            generatedChunk.Data.View = chunkView;
            IslandChunkDebugDrawer debugDrawer = chunkObject.AddComponent<IslandChunkDebugDrawer>();
            debugDrawer.Initialize(chunkView, chunkDebug);
            MeshFilter meshFilter = chunkView.MeshFilter;
            MeshRenderer meshRenderer = chunkView.MeshRenderer;
            TerrainGrassRenderer grassRenderer = chunkView.GrassRenderer;
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = _lodSelector.GetLevel(generatedChunk.Data.CurrentLod).castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            ApplyTerrainWaterBodies(meshRenderer);
            RebuildGrass(mesh, meshFilter, meshRenderer, grassRenderer);

            _meshes.Add(mesh);
            _chunkObjects.Add(chunkObject);
        }
    }

    private int SelectInitialLod(TerrainChunkRuntimeData chunk, Camera camera, Vector3 focus)
    {
        if (_lodSelector == null)
        {
            return 0;
        }

        if (camera != null)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
        }

        TerrainLodDecision decision = _lodSelector.Evaluate(chunk, camera, _frustumPlanes, focus, Time.frameCount);
        chunk.IsVisible = decision.Visible;
        chunk.DistanceToCamera = decision.Distance;
        return decision.Lod;
    }

    private int GetTerrainDensityForLod(int lod)
    {
        if (!IsChunkLodActive || _lodSelector == null)
        {
            return density;
        }

        return Mathf.Clamp(_lodSelector.GetLevel(lod).terrainCellsPerAxis, 1, density);
    }

    private void UpdateLodAndScheduledWork()
    {
        if (!Application.isPlaying || _terrainChunks.Count == 0)
        {
            return;
        }

        using (IslandProfiler.LODUpdate.Auto())
        {
            Camera camera = ResolveLodCamera();
            Vector3 focus = ResolveLodFocus(camera);

            if (_lodSelector == null)
            {
                _lodSelector = new TerrainLodSelector(lodLevels, recentlyVisibleFrameHold);
            }

            if (camera != null)
            {
                GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
            }

            for (int i = 0; i < _terrainChunks.Count; i++)
            {
                TerrainChunkRuntimeData chunk = _terrainChunks[i];
                TerrainLodDecision decision = IsChunkLodActive
                    ? _lodSelector.Evaluate(chunk, camera, _frustumPlanes, focus, Time.frameCount)
                    : new TerrainLodDecision(0, true, true, true, true, 0f);

                chunk.DistanceToCamera = decision.Distance;
                chunk.IsVisible = decision.Visible;

                if (decision.Visible)
                {
                    chunk.LastVisibleFrame = Time.frameCount;
                }

                chunk.WasRecentlyVisible = decision.Render && !decision.Visible;
                ApplyChunkVisibility(chunk, decision);

                if (chunk.DesiredLod != decision.Lod)
                {
                    chunk.DesiredLod = decision.Lod;
                    chunk.MarkDirty(chunk.Bounds, false);
                }

                if (chunk.IsDirty)
                {
                    _workScheduler.Enqueue(chunk);
                }
            }

            int budget = Mathf.Max(1, maxChunkBuildsPerFrame);

            for (int i = 0; i < budget; i++)
            {
                TerrainChunkRuntimeData chunk = _workScheduler.DequeueHighestPriority();

                if (chunk == null)
                {
                    break;
                }

                RebuildTerrainChunk(chunk);
            }
        }
    }

    private void ApplyChunkVisibility(TerrainChunkRuntimeData chunk, TerrainLodDecision decision)
    {
        if (chunk.View == null)
        {
            return;
        }

        chunk.View.ApplyVisibility(decision.Render);

        if (chunk.View.MeshRenderer != null)
        {
            chunk.View.MeshRenderer.shadowCastingMode = _lodSelector.GetLevel(decision.Lod).castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        if (_waterSimulation != null)
        {
            TerrainLodLevel level = _lodSelector.GetLevel(decision.Lod);
            _waterSimulation.SetChunkRuntimeState(
                new Vector2Int(chunk.Id.Coordinate.x, chunk.Id.Coordinate.z),
                decision.RenderWater,
                decision.SimulateWater,
                level.meshUpdateIntervalFrames,
                level.simulationIntervalFrames);
        }
    }

    private void RebuildTerrainChunk(TerrainChunkRuntimeData chunk)
    {
        if (chunk == null)
        {
            return;
        }

        using (IslandProfiler.BuildChunk.Auto())
        {
            int lod = IsChunkLodActive ? chunk.DesiredLod : 0;
            int sampleDensity = GetTerrainDensityForLod(lod);
            Chunk rebuiltChunk = new(chunk.Origin, chunkSize, sampleDensity, SampleDensity);
            Mesh mesh = chunk.Mesh;

            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = $"Island Chunk {chunk.Id.Coordinate.x} {chunk.Id.Coordinate.y} {chunk.Id.Coordinate.z}";
                chunk.Mesh = mesh;
                _meshes.Add(mesh);
            }

            using (IslandProfiler.MeshExtraction.Auto())
            {
                MarchingCubesMesher.Generate(rebuiltChunk, isoLevel, mesh, interpolate);
            }

            chunk.Chunk = rebuiltChunk;
            chunk.CurrentLod = lod;
            chunk.ClearDirty();

            if (chunk.View != null)
            {
                chunk.View.ApplyMesh(mesh);
                ApplyTerrainWaterBodies(chunk.View.MeshRenderer);
                RebuildGrass(mesh, chunk.View.MeshFilter, chunk.View.MeshRenderer, chunk.View.GrassRenderer);
            }
            else if (mesh.GetIndexCount(0) > 0)
            {
                CreateTerrainChunkView(chunk, islandMaterial != null ? islandMaterial : GetDefaultIslandMaterial());
            }
        }
    }

    private void CreateTerrainChunkView(TerrainChunkRuntimeData chunk, Material material)
    {
        GameObject chunkObject = new($"Island Chunk {chunk.Id.Coordinate.x} {chunk.Id.Coordinate.y} {chunk.Id.Coordinate.z}");
        chunkObject.transform.SetParent(transform, false);
        chunkObject.transform.localPosition = chunk.Origin;
        chunkObject.transform.localRotation = Quaternion.identity;
        chunkObject.transform.localScale = Vector3.one;

        TerrainChunkView chunkView = chunkObject.AddComponent<TerrainChunkView>();
        chunkView.Initialize(chunk);
        chunk.View = chunkView;

        IslandChunkDebugDrawer debugDrawer = chunkObject.AddComponent<IslandChunkDebugDrawer>();
        debugDrawer.Initialize(chunkView, chunkDebug);

        chunkView.MeshFilter.sharedMesh = chunk.Mesh;
        chunkView.MeshRenderer.sharedMaterial = material;
        chunkView.MeshRenderer.shadowCastingMode = _lodSelector.GetLevel(chunk.CurrentLod).castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        ApplyTerrainWaterBodies(chunkView.MeshRenderer);
        RebuildGrass(chunk.Mesh, chunkView.MeshFilter, chunkView.MeshRenderer, chunkView.GrassRenderer);

        _chunkObjects.Add(chunkObject);
    }

    private Camera ResolveLodCamera()
    {
        if (Camera.main != null)
        {
            return Camera.main;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
        {
            return SceneView.lastActiveSceneView.camera;
        }
#endif

        return null;
    }

    private Vector3 ResolveLodFocus(Camera camera)
    {
        if (lodFocus != null)
        {
            return lodFocus.position;
        }

        if (camera != null)
        {
            return camera.transform.position;
        }

        return transform.TransformPoint(_islandCenter);
    }

    private void GenerateFeatures()
    {
        using (IslandProfiler.GenerateFeatures.Auto())
        {
            GenerateFeaturesInternal();
        }
    }

    private void GenerateFeaturesInternal()
    {
        List<TerraceFeature> runtimeTerraces = new(terraces.Count);
        List<BasinFeature> runtimeBasins = new(basins.Count);
        List<LakeFeature> runtimeLakes = new(lakes.Count);
        List<GeneratedWaterBody> waterBodies = new(lakes.Count);

        for (int i = 0; i < terraces.Count; i++)
        {
            TerraceFeature terrace = SanitizeTerrace(terraces[i]);

            if (terrace.enabled)
            {
                runtimeTerraces.Add(terrace);
            }
        }

        for (int i = 0; i < basins.Count; i++)
        {
            BasinFeature basin = SanitizeBasin(basins[i]);

            if (basin.enabled)
            {
                runtimeBasins.Add(basin);
            }
        }

        for (int i = 0; i < lakes.Count; i++)
        {
            LakeFeature lake = SanitizeLake(lakes[i]);

            if (!lake.enabled)
            {
                continue;
            }

            runtimeLakes.Add(lake);
            waterBodies.Add(new GeneratedWaterBody(
                lake.position,
                lake.radius,
                lake.shoreWidth,
                lake.waterSurfaceHeight,
                lake.shapeNoise.shapeNoiseFrequency,
                lake.shapeNoise.shapeNoiseStrength));
        }

        _terraces = runtimeTerraces.ToArray();
        _basins = runtimeBasins.ToArray();
        _lakes = runtimeLakes.ToArray();
        _waterBodies = waterBodies.ToArray();
        _rivers = BuildRuntimeRivers(_lakes);
        UpdateTerrainWaterBodyShaderData();
        UpdateTerrainWaterRiverShaderData();
    }

    public void NotifyTerrainModified(Bounds modifiedWorldBounds)
    {
        using (IslandProfiler.DirtyTerrain.Auto())
        {
            MarkTerrainDirty(modifiedWorldBounds, true);

            if (_waterSimulation == null)
            {
                TryGetComponent(out _waterSimulation);
            }

            if (_waterSimulation != null)
            {
                _waterSimulation.NotifyTerrainChanged(modifiedWorldBounds);
            }
        }
    }

    public void MarkTerrainDirty(Bounds modifiedWorldBounds, bool isModification = false)
    {
        if (_terrainChunks.Count == 0)
        {
            return;
        }

        Bounds paddedBounds = modifiedWorldBounds;
        paddedBounds.Expand(chunkSize / Mathf.Max(1f, density));

        for (int i = 0; i < _terrainChunks.Count; i++)
        {
            TerrainChunkRuntimeData chunk = _terrainChunks[i];

            if (!chunk.Bounds.Intersects(paddedBounds))
            {
                continue;
            }

            chunk.MarkDirty(paddedBounds, isModification);
            _workScheduler.Enqueue(chunk);
        }
    }

    private void UpdateTerrainWaterBodyShaderData()
    {
        int count = Mathf.Min(_waterBodies.Length, MaxTerrainWaterBodyShaderCount);

        for (int i = 0; i < count; i++)
        {
            GeneratedWaterBody body = _waterBodies[i];
            float waterSurfaceY = transform.TransformPoint(new Vector3(
                _islandCenter.x,
                _islandCenter.y + body.SurfaceHeight,
                _islandCenter.z)).y;
            _terrainWaterBodyShaderData[i] = new Vector4(
                body.LocalPosition.x,
                body.LocalPosition.y,
                Mathf.Max(0.001f, body.Radius),
                waterSurfaceY);
            _terrainWaterBodyShapeShaderData[i] = new Vector4(
                Mathf.Max(0f, body.ShapeNoiseFrequency),
                Mathf.Clamp01(body.ShapeNoiseStrength),
                0f,
                0f);
        }

        for (int i = count; i < MaxTerrainWaterBodyShaderCount; i++)
        {
            _terrainWaterBodyShaderData[i] = Vector4.zero;
            _terrainWaterBodyShapeShaderData[i] = Vector4.zero;
        }
    }

    private void UpdateTerrainWaterRiverShaderData()
    {
        int count = Mathf.Min(_rivers.Length, MaxTerrainWaterRiverShaderCount);

        for (int i = 0; i < count; i++)
        {
            GeneratedRiver river = _rivers[i];
            float startSurfaceY = transform.TransformPoint(new Vector3(
                _islandCenter.x,
                _islandCenter.y + river.StartSurfaceHeight,
                _islandCenter.z)).y;
            float endSurfaceY = transform.TransformPoint(new Vector3(
                _islandCenter.x,
                _islandCenter.y + river.EndSurfaceHeight,
                _islandCenter.z)).y;
            _terrainWaterRiverShaderData[i] = new Vector4(
                river.LocalStart.x,
                river.LocalStart.y,
                river.LocalEnd.x,
                river.LocalEnd.y);
            _terrainWaterRiverData[i] = new Vector4(
                Mathf.Max(0.001f, river.Width),
                startSurfaceY,
                endSurfaceY,
                0f);
            _terrainWaterRiverShapeData[i] = new Vector4(
                Mathf.Max(0f, river.MeanderFrequency),
                Mathf.Clamp01(river.MeanderStrength),
                0f,
                0f);
        }

        for (int i = count; i < MaxTerrainWaterRiverShaderCount; i++)
        {
            _terrainWaterRiverShaderData[i] = Vector4.zero;
            _terrainWaterRiverData[i] = Vector4.zero;
            _terrainWaterRiverShapeData[i] = Vector4.zero;
        }
    }

    private void ApplyTerrainWaterBodies(Renderer terrainRenderer)
    {
        if (terrainRenderer == null)
        {
            return;
        }

        int count = Mathf.Min(_waterBodies.Length, MaxTerrainWaterBodyShaderCount);
        int riverCount = Mathf.Min(_rivers.Length, MaxTerrainWaterRiverShaderCount);
        _terrainPropertyBlock ??= new MaterialPropertyBlock();
        terrainRenderer.GetPropertyBlock(_terrainPropertyBlock);
        _terrainPropertyBlock.SetInt(TerrainWaterBodyCountId, count);
        _terrainPropertyBlock.SetVectorArray(TerrainWaterBodiesId, _terrainWaterBodyShaderData);
        _terrainPropertyBlock.SetVectorArray(TerrainWaterBodyShapeDataId, _terrainWaterBodyShapeShaderData);
        _terrainPropertyBlock.SetInt(TerrainWaterRiverCountId, riverCount);
        _terrainPropertyBlock.SetVectorArray(TerrainWaterRiversId, _terrainWaterRiverShaderData);
        _terrainPropertyBlock.SetVectorArray(TerrainWaterRiverDataId, _terrainWaterRiverData);
        _terrainPropertyBlock.SetVectorArray(TerrainWaterRiverShapeDataId, _terrainWaterRiverShapeData);
        _terrainPropertyBlock.SetMatrix(TerrainWaterWorldToIslandId, transform.worldToLocalMatrix);
        _terrainPropertyBlock.SetVector(TerrainWaterIslandCenterId, new Vector4(_islandCenter.x, _islandCenter.z, 0f, 0f));
        _terrainPropertyBlock.SetVector(
            TerrainWaterNoiseSeedOffsetsId,
            new Vector4(SeedOffset(53), SeedOffset(59), SeedOffset(67), SeedOffset(71)));
        terrainRenderer.SetPropertyBlock(_terrainPropertyBlock);
        _terrainPropertyBlock.Clear();
    }

    private void RefreshTerrainWaterBodyRenderers()
    {
        InitializeGrid();
        GenerateFeatures();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);

            if (!child.name.StartsWith(ChunkObjectNamePrefix)
                || !child.TryGetComponent(out MeshRenderer meshRenderer))
            {
                continue;
            }

            ApplyTerrainWaterBodies(meshRenderer);

            if (child.TryGetComponent(out MeshFilter meshFilter)
                && child.TryGetComponent(out TerrainGrassRenderer grassRenderer))
            {
                RebuildGrass(meshFilter.sharedMesh, meshFilter, meshRenderer, grassRenderer);
            }
        }
    }

    private void GenerateWater()
    {
        if (!generateWater)
        {
            if (TryGetComponent(out WaterSimulation existingWaterSimulation))
            {
                existingWaterSimulation.Clear();
            }

            _waterSimulation = null;
            return;
        }

        if (!TryGetComponent(out WaterSimulation waterSimulation))
        {
            waterSimulation = gameObject.AddComponent<WaterSimulation>();
        }

        _waterSimulation = waterSimulation;
        LakeWaterBody[] lakes = new LakeWaterBody[_waterBodies.Length];
        RiverWaterBody[] riverBodies = new RiverWaterBody[_rivers.Length];

        for (int i = 0; i < _waterBodies.Length; i++)
        {
            GeneratedWaterBody body = _waterBodies[i];
            lakes[i] = new LakeWaterBody(
                new Vector2(_islandCenter.x + body.LocalPosition.x, _islandCenter.z + body.LocalPosition.y),
                body.Radius,
                body.ShoreWidth,
                _islandCenter.y + body.SurfaceHeight);
        }

        for (int i = 0; i < _rivers.Length; i++)
        {
            GeneratedRiver river = _rivers[i];
            riverBodies[i] = new RiverWaterBody(
                new Vector2(_islandCenter.x + river.LocalStart.x, _islandCenter.z + river.LocalStart.y),
                new Vector2(_islandCenter.x + river.LocalEnd.x, _islandCenter.z + river.LocalEnd.y),
                river.Width,
                _islandCenter.y + river.StartSurfaceHeight,
                _islandCenter.y + river.EndSurfaceHeight,
                new Vector2(_islandCenter.x, _islandCenter.z),
                new Vector2(SeedOffset(67), SeedOffset(71)),
                river.MeanderFrequency,
                river.MeanderStrength);
        }

        waterSimulation.Initialize(this, chunksPerAxis, chunkSize, lakes, riverBodies);
    }

    private TerraceFeature SanitizeTerrace(TerraceFeature terrace)
    {
        terrace.radius = Mathf.Max(0.001f, terrace.radius);
        terrace.height = Mathf.Max(0f, terrace.height);
        terrace.sharpness = Mathf.Max(0.001f, terrace.sharpness);
        terrace.plateauRadius = Mathf.Clamp01(terrace.plateauRadius);
        terrace.shapeNoise = SanitizeShapeNoise(terrace.shapeNoise);
        return terrace;
    }

    private BasinFeature SanitizeBasin(BasinFeature basin)
    {
        basin.radius = Mathf.Max(0.001f, basin.radius);
        basin.depth = Mathf.Max(0f, basin.depth);
        basin.sharpness = Mathf.Max(0.001f, basin.sharpness);
        basin.plateauRadius = Mathf.Clamp01(basin.plateauRadius);
        basin.shapeNoise = SanitizeShapeNoise(basin.shapeNoise);
        return basin;
    }

    private LakeFeature SanitizeLake(LakeFeature lake)
    {
        lake.radius = Mathf.Max(0.001f, lake.radius);
        lake.basinDepth = Mathf.Max(0f, lake.basinDepth);
        lake.sharpness = Mathf.Max(0.001f, lake.sharpness);
        lake.shoreWidth = Mathf.Clamp(lake.shoreWidth, 0f, lake.radius);
        lake.shapeNoise = SanitizeShapeNoise(lake.shapeNoise);
        return lake;
    }

    private static TerrainShapeNoise SanitizeShapeNoise(TerrainShapeNoise shapeNoise)
    {
        shapeNoise.shapeNoiseFrequency = Mathf.Max(0f, shapeNoise.shapeNoiseFrequency);
        shapeNoise.shapeNoiseStrength = Mathf.Clamp01(shapeNoise.shapeNoiseStrength);
        return shapeNoise;
    }

    private GeneratedRiver[] BuildRuntimeRivers(IReadOnlyList<LakeFeature> runtimeLakes)
    {
        if (rivers.Count == 0)
        {
            return System.Array.Empty<GeneratedRiver>();
        }

        List<GeneratedRiver> runtimeRivers = new(rivers.Count);

        for (int i = 0; i < rivers.Count; i++)
        {
            RiverFeature river = rivers[i];

            if (!river.enabled)
            {
                continue;
            }

            river.width = Mathf.Max(0.001f, river.width);
            river.depth = Mathf.Max(0f, river.depth);
            river.bankSharpness = Mathf.Max(0.001f, river.bankSharpness);
            river.meanderFrequency = Mathf.Max(0f, river.meanderFrequency);
            river.meanderStrength = Mathf.Clamp01(river.meanderStrength);

            Vector2 start = river.startPosition;
            float startSurfaceHeight = river.waterSurfaceHeight;

            if (river.startMode == RiverStartMode.LakeToEdge && runtimeLakes.Count > 0)
            {
                int lakeIndex = Mathf.Clamp(river.sourceLakeIndex, 0, runtimeLakes.Count - 1);
                LakeFeature lake = runtimeLakes[lakeIndex];
                start = lake.position;
                startSurfaceHeight = lake.waterSurfaceHeight;
            }
            else
            {
                start = ProjectToIslandEdge(start);
            }

            Vector2 end = ProjectToIslandEdge(river.endPosition);

            if ((end - start).sqrMagnitude <= 0.0001f)
            {
                end = ProjectToIslandEdge(start + Vector2.right);
            }

            runtimeRivers.Add(new GeneratedRiver(
                start,
                end,
                river.width,
                river.depth,
                river.bankSharpness,
                startSurfaceHeight,
                river.endWaterSurfaceHeight,
                river.meanderFrequency,
                river.meanderStrength));
        }

        return runtimeRivers.ToArray();
    }

    private Vector2 ProjectToIslandEdge(Vector2 localPosition)
    {
        if (localPosition.sqrMagnitude <= 0.000001f)
        {
            return Vector2.right * islandRadius;
        }

        return localPosition.normalized * Mathf.Max(0.001f, islandRadius);
    }

    private void GenerateRandomFeatures(int count, int generationSeed)
    {
        if (count <= 0)
        {
            return;
        }

        System.Random random = new(generationSeed);
        int attempts = count * 32;
        float edgePadding = Mathf.Max(0f, islandRadius * featureEdgePadding);
        Vector2 usableRadiusRange = ClampFeatureRadiusRangeForPlacement(edgePadding);
        List<FeaturePlacement> placements = new(count);

        for (int attempt = 0; attempt < attempts && placements.Count < count; attempt++)
        {
            float radius = RandomRange(random, usableRadiusRange);
            float placementRadius = Mathf.Max(0f, islandRadius - edgePadding - radius);
            float distance = Mathf.Sqrt((float)random.NextDouble()) * placementRadius;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector2 position = new(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);

            if (OverlapsExistingFeature(position, radius, placements))
            {
                continue;
            }

            AddRandomFeature(random, position, radius);
            placements.Add(new FeaturePlacement(position, radius));
        }

        while (placements.Count < count)
        {
            float radius = RandomRange(random, usableRadiusRange);
            float placementRadius = Mathf.Max(0f, islandRadius - edgePadding - radius);
            float distance = Mathf.Sqrt((float)random.NextDouble()) * placementRadius;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector2 position = new(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);
            AddRandomFeature(random, position, radius);
            placements.Add(new FeaturePlacement(position, radius));
        }
    }

    private void AddRandomFeature(System.Random random, Vector2 position, float radius)
    {
        switch (PickFeatureKind(random))
        {
            case RandomFeatureKind.Terrace:
                TerraceFeature terrace = CreateDefaultTerrace(position);
                terrace.radius = radius;
                terrace.height = RandomRange(random, featureHeightRange);
                terrace.sharpness = RandomRange(random, featureSharpnessRange);
                terraces.Add(terrace);
                break;
            case RandomFeatureKind.Basin:
                BasinFeature basin = CreateDefaultBasin(position);
                basin.radius = radius;
                basin.depth = RandomRange(random, featureHeightRange);
                basin.sharpness = RandomRange(random, featureSharpnessRange);
                basins.Add(basin);
                break;
            case RandomFeatureKind.Lake:
                LakeFeature lake = CreateDefaultLake(position);
                lake.radius = radius;
                lake.basinDepth = RandomRange(random, featureHeightRange);
                lake.sharpness = RandomRange(random, featureSharpnessRange);
                lakes.Add(lake);
                break;
            case RandomFeatureKind.River:
                rivers.Add(CreateRandomRiver(random));
                break;
        }
    }

    private RiverFeature CreateRandomRiver(System.Random random)
    {
        float startAngle = (float)random.NextDouble() * Mathf.PI * 2f;
        float endAngle = startAngle + Mathf.PI + ((float)random.NextDouble() - 0.5f) * Mathf.PI * 0.75f;
        RiverFeature river = CreateDefaultRiver();
        river.width = RandomRange(random, riverWidthRange);
        river.depth = RandomRange(random, riverDepthRange);
        river.startPosition = new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle)) * islandRadius;
        river.endPosition = new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * islandRadius;

        if (lakes.Count > 0 && random.NextDouble() < 0.5)
        {
            river.startMode = RiverStartMode.LakeToEdge;
            river.sourceLakeIndex = random.Next(0, lakes.Count);
        }

        return river;
    }

    private Vector2 ClampFeatureRadiusRangeForPlacement(float edgePadding)
    {
        float maximumRadius = Mathf.Max(0.001f, islandRadius - edgePadding);
        float min = Mathf.Min(featureRadiusRange.x, maximumRadius);
        float max = Mathf.Min(featureRadiusRange.y, maximumRadius);

        if (max < min)
        {
            max = min;
        }

        return new Vector2(min, max);
    }

    private bool OverlapsExistingFeature(Vector2 position, float radius, List<FeaturePlacement> features)
    {
        for (int i = 0; i < features.Count; i++)
        {
            FeaturePlacement feature = features[i];
            float minimumDistance = (radius + feature.Radius) * Mathf.Clamp01(featureOverlap);

            if ((position - feature.Position).sqrMagnitude < minimumDistance * minimumDistance)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearFeatureLists()
    {
        terraces.Clear();
        basins.Clear();
        lakes.Clear();
        rivers.Clear();
    }

    public static TerraceFeature CreateDefaultTerrace(Vector2 position)
    {
        return new TerraceFeature
        {
            enabled = true,
            position = position,
            radius = 5.5f,
            height = 3f,
            sharpness = 1.8f,
            plateauRadius = 0.35f,
            shapeNoise = new TerrainShapeNoise
            {
                shapeNoiseFrequency = 0.2f,
                shapeNoiseStrength = 0.08f
            }
        };
    }

    public static BasinFeature CreateDefaultBasin(Vector2 position)
    {
        return new BasinFeature
        {
            enabled = true,
            position = position,
            radius = 5.5f,
            depth = 2.5f,
            sharpness = 1.6f,
            plateauRadius = 0.25f,
            shapeNoise = new TerrainShapeNoise
            {
                shapeNoiseFrequency = 0.25f,
                shapeNoiseStrength = 0.12f
            }
        };
    }

    public static LakeFeature CreateDefaultLake(Vector2 position)
    {
        return new LakeFeature
        {
            enabled = true,
            position = position,
            radius = 4.5f,
            basinDepth = 2.2f,
            sharpness = 1.8f,
            shoreWidth = 1.4f,
            waterSurfaceHeight = -0.35f,
            shapeNoise = new TerrainShapeNoise
            {
                shapeNoiseFrequency = 0.18f,
                shapeNoiseStrength = 0.08f
            }
        };
    }

    public static RiverFeature CreateDefaultRiver()
    {
        return new RiverFeature
        {
            enabled = true,
            startMode = RiverStartMode.EdgeToEdge,
            sourceLakeIndex = 0,
            startPosition = new Vector2(-6f, 0f),
            endPosition = new Vector2(6f, 0f),
            width = 1f,
            depth = 0.9f,
            bankSharpness = 1.6f,
            waterSurfaceHeight = -0.2f,
            endWaterSurfaceHeight = -0.6f,
            meanderFrequency = 0.18f,
            meanderStrength = 0.35f
        };
    }

    private RandomFeatureKind PickFeatureKind(System.Random random)
    {
        float total = terraceWeight + basinWeight + lakeWeight + riverWeight;

        if (total <= 0f)
        {
            return RandomFeatureKind.Terrace;
        }

        float value = (float)random.NextDouble() * total;

        if (value < terraceWeight)
        {
            return RandomFeatureKind.Terrace;
        }

        value -= terraceWeight;

        if (value < basinWeight)
        {
            return RandomFeatureKind.Basin;
        }

        value -= basinWeight;

        if (value < lakeWeight)
        {
            return RandomFeatureKind.Lake;
        }

        return RandomFeatureKind.River;
    }

    private void RemoveDetachedComponents(List<GeneratedChunkMesh> generatedChunks)
    {
        if (!removeDetachedGeometry)
        {
            return;
        }

        int totalTriangleCount = 0;

        for (int i = 0; i < generatedChunks.Count; i++)
        {
            GeneratedChunkMesh chunk = generatedChunks[i];
            chunk.FirstTriangleIndex = totalTriangleCount;
            chunk.TriangleCount = chunk.Mesh != null ? chunk.Mesh.triangles.Length / 3 : 0;
            generatedChunks[i] = chunk;
            totalTriangleCount += chunk.TriangleCount;
        }

        if (totalTriangleCount <= 1)
        {
            return;
        }

        DisjointSet components = new(totalTriangleCount);
        Dictionary<VertexKey, int> firstTriangleByVertex = new(totalTriangleCount * 2);

        for (int chunkIndex = 0; chunkIndex < generatedChunks.Count; chunkIndex++)
        {
            GeneratedChunkMesh chunk = generatedChunks[chunkIndex];

            if (chunk.TriangleCount == 0)
            {
                continue;
            }

            Vector3[] vertices = chunk.Mesh.vertices;
            int[] triangles = chunk.Mesh.triangles;

            for (int triangleIndex = 0; triangleIndex < chunk.TriangleCount; triangleIndex++)
            {
                int globalTriangle = chunk.FirstTriangleIndex + triangleIndex;
                int triangleStart = triangleIndex * 3;

                for (int corner = 0; corner < 3; corner++)
                {
                    Vector3 worldVertex = (Vector3)chunk.Position + vertices[triangles[triangleStart + corner]];
                    VertexKey vertexKey = new(worldVertex);

                    if (firstTriangleByVertex.TryGetValue(vertexKey, out int connectedTriangle))
                    {
                        components.Union(globalTriangle, connectedTriangle);
                    }
                    else
                    {
                        firstTriangleByVertex.Add(vertexKey, globalTriangle);
                    }
                }
            }
        }

        float[] componentAreas = new float[totalTriangleCount];

        for (int chunkIndex = 0; chunkIndex < generatedChunks.Count; chunkIndex++)
        {
            GeneratedChunkMesh chunk = generatedChunks[chunkIndex];

            if (chunk.TriangleCount == 0)
            {
                continue;
            }

            Vector3[] vertices = chunk.Mesh.vertices;
            int[] triangles = chunk.Mesh.triangles;

            for (int triangleIndex = 0; triangleIndex < chunk.TriangleCount; triangleIndex++)
            {
                int triangleStart = triangleIndex * 3;
                Vector3 a = vertices[triangles[triangleStart]];
                Vector3 b = vertices[triangles[triangleStart + 1]];
                Vector3 c = vertices[triangles[triangleStart + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                int root = components.Find(chunk.FirstTriangleIndex + triangleIndex);
                componentAreas[root] += area;
            }
        }

        int largestComponent = 0;
        float largestArea = 0f;

        for (int i = 0; i < componentAreas.Length; i++)
        {
            if (componentAreas[i] > largestArea)
            {
                largestArea = componentAreas[i];
                largestComponent = i;
            }
        }

        for (int i = 0; i < generatedChunks.Count; i++)
        {
            KeepOnlyComponent(generatedChunks[i], components, largestComponent);
        }
    }

    private static void KeepOnlyComponent(
        GeneratedChunkMesh chunk,
        DisjointSet components,
        int component)
    {
        if (chunk.TriangleCount == 0)
        {
            return;
        }

        Vector3[] vertices = chunk.Mesh.vertices;
        int[] triangles = chunk.Mesh.triangles;
        Dictionary<int, int> remappedVertices = new(vertices.Length);
        List<Vector3> keptVertices = new(vertices.Length);
        List<int> keptTriangles = new(triangles.Length);

        for (int triangleIndex = 0; triangleIndex < chunk.TriangleCount; triangleIndex++)
        {
            if (components.Find(chunk.FirstTriangleIndex + triangleIndex) != component)
            {
                continue;
            }

            int triangleStart = triangleIndex * 3;

            for (int corner = 0; corner < 3; corner++)
            {
                int oldVertexIndex = triangles[triangleStart + corner];

                if (!remappedVertices.TryGetValue(oldVertexIndex, out int newVertexIndex))
                {
                    newVertexIndex = keptVertices.Count;
                    remappedVertices.Add(oldVertexIndex, newVertexIndex);
                    keptVertices.Add(vertices[oldVertexIndex]);
                }

                keptTriangles.Add(newVertexIndex);
            }
        }

        chunk.Mesh.Clear();

        if (keptTriangles.Count == 0)
        {
            return;
        }

        chunk.Mesh.SetVertices(keptVertices);
        chunk.Mesh.SetTriangles(keptTriangles, 0);
        chunk.Mesh.RecalculateNormals();
        chunk.Mesh.RecalculateBounds();
    }

    private float QuantizeTerrace(float value)
    {
        int steps = Mathf.Max(1, terraceStepCount);
        float scaled = Mathf.Clamp01(value) * steps;
        float lower = Mathf.Floor(scaled) / steps;
        float upper = Mathf.Ceil(scaled) / steps;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Repeat(scaled, 1f));

        return Mathf.Lerp(lower, upper, Mathf.Clamp01(terraceSmoothing) * t);
    }

    private float EdgeBlend(float radius01)
    {
        float falloff = Mathf.Max(0.0001f, edgeFalloff);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - falloff, 1f, radius01));
    }

    private float InteriorFeatureMask(float radius01)
    {
        return 1f - EdgeBlend(radius01);
    }

    private void ApplyBoundsConstraints()
    {
        if (boundsMode != IslandBoundsMode.ClampShapeToChunkGrid)
        {
            return;
        }

        float totalSize = Mathf.Max(1, chunksPerAxis) * Mathf.Max(1, chunkSize);
        float halfSize = totalSize * 0.5f;
        float padding = Mathf.Min(boundsPadding, Mathf.Max(0f, halfSize - 0.001f));
        float maxHorizontalRadius = Mathf.Max(0.001f, halfSize - padding);
        float footprintMultiplier = 1f + Mathf.Max(0f, footprintNoiseStrength);
        islandRadius = Mathf.Min(islandRadius, maxHorizontalRadius / footprintMultiplier);

        float undersideLift = Mathf.Max(0f, undersideNoiseStrength);
        float availableHeight = Mathf.Max(0.001f, totalSize - padding * 2f);
        float topNoiseLift = Mathf.Max(0f, topNoiseStrength);
        float maxFeatureLift = Mathf.Max(0f, availableHeight - topNoiseLift - undersideLift - 0.001f);
        ClampFeatureHeights(maxFeatureLift);

        float maxTopLift = topNoiseLift + CalculateMaxFeatureHeight();
        float maxDepth = Mathf.Max(0.001f, availableHeight - maxTopLift - undersideLift);
        islandDepth = Mathf.Min(islandDepth, maxDepth);

        float minimumBaseHeight = -halfSize + padding + islandDepth + undersideLift;
        float maximumBaseHeight = halfSize - padding - maxTopLift;
        baseSurfaceHeight = Mathf.Clamp(baseSurfaceHeight, minimumBaseHeight, maximumBaseHeight);
    }

    private float CalculateMaxFeatureHeight()
    {
        float maxHeight = 0f;

        for (int i = 0; i < terraces.Count; i++)
        {
            TerraceFeature feature = terraces[i];

            if (feature.enabled)
            {
                maxHeight = Mathf.Max(maxHeight, Mathf.Max(0f, feature.height));
            }
        }

        for (int i = 0; i < basins.Count; i++)
        {
            BasinFeature feature = basins[i];

            if (feature.enabled)
            {
                maxHeight = Mathf.Max(maxHeight, Mathf.Max(0f, feature.depth));
            }
        }

        for (int i = 0; i < lakes.Count; i++)
        {
            LakeFeature feature = lakes[i];

            if (feature.enabled)
            {
                maxHeight = Mathf.Max(maxHeight, Mathf.Max(0f, feature.basinDepth));
            }
        }

        for (int i = 0; i < rivers.Count; i++)
        {
            RiverFeature feature = rivers[i];

            if (feature.enabled)
            {
                maxHeight = Mathf.Max(maxHeight, Mathf.Max(0f, feature.depth));
            }
        }

        return maxHeight;
    }

    private void ClampFeatureHeights(float maxHeight)
    {
        maxHeight = Mathf.Max(0f, maxHeight);
        featureHeightRange = new Vector2(
            Mathf.Min(featureHeightRange.x, maxHeight),
            Mathf.Min(featureHeightRange.y, maxHeight));
        featureHeightRange = SortMinMax(featureHeightRange, 0f);

        for (int i = 0; i < terraces.Count; i++)
        {
            TerraceFeature feature = terraces[i];
            feature.height = Mathf.Min(feature.height, maxHeight);
            terraces[i] = feature;
        }

        for (int i = 0; i < basins.Count; i++)
        {
            BasinFeature feature = basins[i];
            feature.depth = Mathf.Min(feature.depth, maxHeight);
            basins[i] = feature;
        }

        for (int i = 0; i < lakes.Count; i++)
        {
            LakeFeature feature = lakes[i];
            feature.basinDepth = Mathf.Min(feature.basinDepth, maxHeight);
            lakes[i] = feature;
        }

        for (int i = 0; i < rivers.Count; i++)
        {
            RiverFeature feature = rivers[i];
            feature.depth = Mathf.Min(feature.depth, maxHeight);
            rivers[i] = feature;
        }
    }

    private float SampleSignedNoise2D(float x, float z, float frequency)
    {
        return PerlinNoise3D.SampleSigned(x * frequency, 0f, z * frequency);
    }

    private float SeedOffset(int salt)
    {
        unchecked
        {
            int mixed = seed * 73856093 ^ salt * 19349663;
            return (mixed & 0xffff) * 0.017f;
        }
    }

    private static float RandomRange(System.Random random, Vector2 range)
    {
        return Mathf.Lerp(range.x, range.y, (float)random.NextDouble());
    }

    private static Vector2 SortMinMax(Vector2 range, float minimum)
    {
        float min = Mathf.Max(minimum, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return new Vector2(min, max);
    }

    private static Vector2Int SortMinMax(Vector2Int range, int minimum)
    {
        int min = Mathf.Max(minimum, Mathf.Min(range.x, range.y));
        int max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return new Vector2Int(min, max);
    }

    private void RebuildGrass(
        Mesh mesh,
        MeshFilter meshFilter,
        MeshRenderer meshRenderer,
        TerrainGrassRenderer grassRenderer)
    {
        if (grassRenderer == null)
        {
            return;
        }

        Material terrainMaterial = meshRenderer != null
            ? meshRenderer.sharedMaterial
            : islandMaterial;

        grassRenderer.Rebuild(
            mesh,
            meshFilter.transform,
            terrainMaterial,
            grassComputeShader,
            GetGrassMaterial(),
            renderGrass,
            grassDensity,
            grassBoundaryPaddingTexels,
            minGrassHeightTexels,
            maxGrassHeightTexels,
            maxGrassBladesPerChunk,
            maxGrassCandidatesPerTriangle,
            grassSeed,
            grassColors,
            grassWindEnabled,
            grassWindDirection,
            grassWindStrength,
            grassWindSpeed,
            grassWindVariation,
            Mathf.Min(_waterBodies.Length, MaxTerrainWaterBodyShaderCount),
            _terrainWaterBodyShaderData,
            _terrainWaterBodyShapeShaderData,
            Mathf.Min(_rivers.Length, MaxTerrainWaterRiverShaderCount),
            _terrainWaterRiverShaderData,
            _terrainWaterRiverData,
            _terrainWaterRiverShapeData,
            transform.worldToLocalMatrix,
            new Vector4(_islandCenter.x, _islandCenter.z, 0f, 0f),
            new Vector4(SeedOffset(53), SeedOffset(59), SeedOffset(67), SeedOffset(71)));
    }

    private Material GetDefaultIslandMaterial()
    {
        if (_defaultIslandMaterial != null)
        {
            return _defaultIslandMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        _defaultIslandMaterial = new Material(shader);
        _defaultIslandMaterial.name = "Default Island Material";
        _defaultIslandMaterial.color = Color.white;

        return _defaultIslandMaterial;
    }

    private Material GetGrassMaterial()
    {
        if (grassMaterial != null)
        {
            return grassMaterial;
        }

        if (_defaultGrassMaterial != null)
        {
            return _defaultGrassMaterial;
        }

        Shader shader = Shader.Find("Custom/Terrain Grass");

        if (shader == null)
        {
            return null;
        }

        _defaultGrassMaterial = new Material(shader);
        _defaultGrassMaterial.name = "Default Island Grass Material";
        return _defaultGrassMaterial;
    }

    private void ResolveDefaultGrassAssets()
    {
#if UNITY_EDITOR
        if (grassComputeShader == null)
        {
            grassComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/TerrainGrass.compute");
        }

#endif
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

    private struct GeneratedChunkMesh
    {
        public GeneratedChunkMesh(Vector3Int position, Mesh mesh, TerrainChunkRuntimeData data)
        {
            Position = position;
            Mesh = mesh;
            Data = data;
            FirstTriangleIndex = 0;
            TriangleCount = 0;
        }

        public Vector3Int Position { get; }
        public Mesh Mesh { get; }
        public TerrainChunkRuntimeData Data { get; }
        public int FirstTriangleIndex { get; set; }
        public int TriangleCount { get; set; }
    }

    private enum RandomFeatureKind
    {
        Terrace,
        Basin,
        Lake,
        River
    }

    private readonly struct FeaturePlacement
    {
        public FeaturePlacement(Vector2 position, float radius)
        {
            Position = position;
            Radius = radius;
        }

        public Vector2 Position { get; }
        public float Radius { get; }
    }

    private readonly struct VertexKey
    {
        private const float Scale = 10000f;
        private readonly int _x;
        private readonly int _y;
        private readonly int _z;

        public VertexKey(Vector3 position)
        {
            _x = Mathf.RoundToInt(position.x * Scale);
            _y = Mathf.RoundToInt(position.y * Scale);
            _z = Mathf.RoundToInt(position.z * Scale);
        }

        public override bool Equals(object obj)
        {
            return obj is VertexKey other
                && _x == other._x
                && _y == other._y
                && _z == other._z;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _x;
                hash = (hash * 397) ^ _y;
                hash = (hash * 397) ^ _z;
                return hash;
            }
        }
    }

    private sealed class DisjointSet
    {
        private readonly int[] _parent;
        private readonly byte[] _rank;

        public DisjointSet(int count)
        {
            _parent = new int[count];
            _rank = new byte[count];

            for (int i = 0; i < count; i++)
            {
                _parent[i] = i;
            }
        }

        public int Find(int value)
        {
            int parent = _parent[value];

            if (parent == value)
            {
                return value;
            }

            int root = Find(parent);
            _parent[value] = root;
            return root;
        }

        public void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);

            if (rootA == rootB)
            {
                return;
            }

            if (_rank[rootA] < _rank[rootB])
            {
                _parent[rootA] = rootB;
                return;
            }

            if (_rank[rootA] > _rank[rootB])
            {
                _parent[rootB] = rootA;
                return;
            }

            _parent[rootB] = rootA;
            _rank[rootA]++;
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(IslandGenerator))]
public sealed class IslandGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        IslandGenerator generator = (IslandGenerator)target;
        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;

        while (property.NextVisible(enterChildren))
        {
            using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
            {
                EditorGUILayout.PropertyField(property, true);
            }

            if (property.name == "rivers")
            {
                DrawFeatureActions(generator);
            }

            enterChildren = false;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawFeatureActions(IslandGenerator generator)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Feature Actions", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Terrace"))
            {
                Run(generator, generator.AddTerraceFeature);
            }

            if (GUILayout.Button("Add Basin"))
            {
                Run(generator, generator.AddBasinFeature);
            }

            if (GUILayout.Button("Add Lake"))
            {
                Run(generator, generator.AddLakeFeature);
            }

            if (GUILayout.Button("Add River"))
            {
                Run(generator, generator.AddRiverFeature);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Randomize Seed"))
            {
                Run(generator, generator.RandomizeIslandSeed);
            }

            if (GUILayout.Button("Generate Random"))
            {
                Run(generator, generator.GenerateRandomTerrainFeatures);
            }

            if (GUILayout.Button("Generate From Seed"))
            {
                Run(generator, generator.GenerateSeedTerrainFeatures);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clear Features"))
            {
                Run(generator, generator.ClearEditableTerrainFeatures);
            }
        }
    }

    private static void Run(IslandGenerator generator, System.Action action)
    {
        Undo.RecordObject(generator, "Update Island Features");
        action();
        EditorUtility.SetDirty(generator);
    }
}
#endif
