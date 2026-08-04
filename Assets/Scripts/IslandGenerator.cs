using DefaultNamespace;
using DefaultNamespace.Water;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum TerrainFeatureType
{
    Hill,
    Mountain,
    Terrace,
    Basin,
    Lake
}

public enum IslandBoundsMode
{
    ClampShapeToChunkGrid,
    AllowOverflow
}

[System.Serializable]
public struct TerrainFeature
{
    public bool enabled;
    public Vector2 position;
    [Min(0.001f)] public float radius;
    public float height;
    [Min(0.001f)] public float sharpness;
    public TerrainFeatureType type;
    [Range(0f, 1f)] public float plateauRadius;
    [Min(0f)] public float shapeNoiseFrequency;
    [Range(0f, 1f)] public float shapeNoiseStrength;
    [Min(0f)] public float shoreWidth;
    public float waterSurfaceHeight;
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

[ExecuteAlways]
public sealed class IslandGenerator : MonoBehaviour, ITerrainDensityField
{
    private const string ChunkObjectNamePrefix = "Island Chunk ";
    private const int MaxTerrainWaterBodyShaderCount = 16;
    private static readonly int TerrainWaterBodyCountId = Shader.PropertyToID("_TerrainWaterBodyCount");
    private static readonly int TerrainWaterBodiesId = Shader.PropertyToID("_TerrainWaterBodies");
    private static readonly int TerrainWaterBodyShapeDataId = Shader.PropertyToID("_TerrainWaterBodyShapeData");
    private static readonly int TerrainWaterWorldToIslandId = Shader.PropertyToID("_TerrainWaterWorldToIsland");
    private static readonly int TerrainWaterIslandCenterId = Shader.PropertyToID("_TerrainWaterIslandCenter");
    private static readonly int TerrainWaterNoiseSeedOffsetsId = Shader.PropertyToID("_TerrainWaterNoiseSeedOffsets");

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
    [SerializeField] private List<TerrainFeature> terrainFeatures = new();
    [SerializeField] private Vector2Int randomFeatureCountRange = new(3, 7);
    [SerializeField, Min(0f)] private float hillWeight = 0.55f;
    [SerializeField, Min(0f)] private float mountainWeight = 0.3f;
    [SerializeField, Min(0f)] private float terraceWeight = 0.15f;
    [SerializeField, Min(0f)] private float basinWeight = 0.1f;
    [SerializeField, Min(0f)] private float lakeWeight = 0.12f;
    [SerializeField] private Vector2 featureRadiusRange = new(3f, 7f);
    [SerializeField] private Vector2 featureHeightRange = new(1.5f, 5f);
    [SerializeField] private Vector2 featureSharpnessRange = new(1f, 4f);
    [SerializeField, Range(0f, 1f)] private float featureOverlap = 0.55f;
    [SerializeField, Range(0f, 1f)] private float featureEdgePadding = 0.18f;
    [SerializeField, Min(1)] private int terraceStepCount = 4;
    [SerializeField, Range(0f, 1f)] private float terraceSmoothing = 0.15f;

    [Header("Connected Geometry")]
    [SerializeField] private bool removeDetachedGeometry = true;

    [Header("Water")]
    [SerializeField] private bool generateWater = true;

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
    private readonly Vector4[] _terrainWaterBodyShaderData = new Vector4[MaxTerrainWaterBodyShaderCount];
    private readonly Vector4[] _terrainWaterBodyShapeShaderData = new Vector4[MaxTerrainWaterBodyShaderCount];
    private TerrainFeature[] _features = System.Array.Empty<TerrainFeature>();
    private GeneratedWaterBody[] _waterBodies = System.Array.Empty<GeneratedWaterBody>();
    private MaterialPropertyBlock _terrainPropertyBlock;
    private Material _defaultIslandMaterial;
    private Material _defaultGrassMaterial;
    private float _totalSize;
    private Vector3Int _gridOrigin;
    private Vector3 _islandCenter;
#if UNITY_EDITOR
    private bool _hasPendingEditorRefresh;
#endif

    private void OnEnable()
    {
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
        chunksPerAxis = Mathf.Max(1, chunksPerAxis);
        chunkSize = Mathf.Max(1, chunkSize);
        featureRadiusRange = SortMinMax(featureRadiusRange, 0.001f);
        featureHeightRange = SortMinMax(featureHeightRange, 0f);
        featureSharpnessRange = SortMinMax(featureSharpnessRange, 0.001f);
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
        RequestRefreshIslandPreview();
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
        terrainFeatures.Add(CreateDefaultTerrainFeature(TerrainFeatureType.Hill, Vector2.zero));
        RequestRefreshIslandPreview();
    }

    public void AddTerrainFeature(TerrainFeatureType featureType)
    {
        terrainFeatures.Add(CreateDefaultTerrainFeature(featureType, Vector2.zero));
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

        terrainFeatures.Clear();
        int count = Random.Range(randomFeatureCountRange.x, randomFeatureCountRange.y + 1);
        int generationSeed = Random.Range(int.MinValue, int.MaxValue);
        GenerateRandomFeatures(terrainFeatures, 0, count, generationSeed);
        RequestRefreshIslandPreview();
    }

    [ContextMenu("Generate Seed Terrain Features")]
    public void GenerateSeedTerrainFeatures()
    {
        ApplyBoundsConstraints();
        InitializeGrid();

        terrainFeatures.Clear();
        System.Random random = new(seed);
        int count = random.Next(randomFeatureCountRange.x, randomFeatureCountRange.y + 1);
        GenerateRandomFeatures(terrainFeatures, 0, count, seed);
        RequestRefreshIslandPreview();
    }

    [ContextMenu("Clear Editable Terrain Features")]
    public void ClearEditableTerrainFeatures()
    {
        terrainFeatures.Clear();
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
        _features = System.Array.Empty<TerrainFeature>();
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

        for (int i = 0; i < _features.Length; i++)
        {
            TerrainFeature feature = _features[i];
            Vector2 offset = islandLocalXZ - feature.position;
            float distance01 = EvaluateFeatureDistance01(offset, feature);

            if (distance01 >= 1f)
            {
                continue;
            }

            float smoothFalloff = Mathf.SmoothStep(1f, 0f, distance01);

            switch (feature.type)
            {
                case TerrainFeatureType.Hill:
                    positiveHeight = Mathf.Max(positiveHeight, smoothFalloff * feature.height);
                    break;
                case TerrainFeatureType.Mountain:
                    positiveHeight = Mathf.Max(
                        positiveHeight,
                        Mathf.Pow(1f - distance01, feature.sharpness) * feature.height);
                    break;
                case TerrainFeatureType.Terrace:
                    float terrace = EvaluateTerraceFalloff(distance01, feature);
                    positiveHeight = Mathf.Max(positiveHeight, QuantizeTerrace(terrace) * feature.height);
                    break;
                case TerrainFeatureType.Basin:
                case TerrainFeatureType.Lake:
                    basinDepth = Mathf.Max(basinDepth, EvaluateBasinFalloff(distance01, feature) * feature.height);
                    break;
            }
        }

        return positiveHeight - basinDepth;
    }

    private float EvaluateFeatureDistance01(Vector2 offset, TerrainFeature feature)
    {
        float radius = Mathf.Max(0.001f, feature.radius);

        if (feature.shapeNoiseStrength > 0f && feature.shapeNoiseFrequency > 0f)
        {
            float noise = SampleSignedNoise2D(
                offset.x + feature.position.x + SeedOffset(53),
                offset.y + feature.position.y + SeedOffset(59),
                feature.shapeNoiseFrequency);
            radius *= Mathf.Max(0.001f, 1f + noise * feature.shapeNoiseStrength);
        }

        return offset.magnitude / radius;
    }

    private float EvaluateTerraceFalloff(float distance01, TerrainFeature feature)
    {
        float plateauRadius = Mathf.Clamp01(feature.plateauRadius);

        if (distance01 <= plateauRadius)
        {
            return 1f;
        }

        float edge01 = Mathf.InverseLerp(1f, plateauRadius, distance01);
        return Mathf.Pow(Mathf.SmoothStep(0f, 1f, edge01), feature.sharpness);
    }

    private float EvaluateBasinFalloff(float distance01, TerrainFeature feature)
    {
        float plateauRadius = Mathf.Clamp01(feature.plateauRadius);

        if (distance01 <= plateauRadius)
        {
            return 1f;
        }

        float edge01 = Mathf.InverseLerp(1f, plateauRadius, distance01);
        return Mathf.Pow(Mathf.SmoothStep(0f, 1f, edge01), feature.sharpness);
    }

    private void RefreshIslandPreview()
    {
        ApplyBoundsConstraints();
        ClearIsland();
        InitializeGrid();
        GenerateFeatures();
        GenerateChunks();
        GenerateWater();
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
        _totalSize = chunksPerAxis * chunkSize;
        int origin = Mathf.FloorToInt(_totalSize * -0.5f);
        _gridOrigin = new Vector3Int(origin, origin, origin);
        _islandCenter = (Vector3)_gridOrigin + Vector3.one * (_totalSize * 0.5f);
        _islandCenter.y = baseSurfaceHeight;
    }

    private void GenerateChunks()
    {
        Material material = islandMaterial != null ? islandMaterial : GetDefaultIslandMaterial();
        List<GeneratedChunkMesh> generatedChunks = new(chunksPerAxis * chunksPerAxis * chunksPerAxis);

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
                    Chunk chunk = new(chunkPosition, chunkSize, density, SampleDensity);
                    Mesh mesh = new();
                    mesh.name = $"Island Chunk {x} {y} {z}";
                    MarchingCubesMesher.Generate(chunk, isoLevel, mesh, interpolate);

                    generatedChunks.Add(new GeneratedChunkMesh(chunkPosition, mesh));
                }
            }
        }

        RemoveDetachedComponents(generatedChunks);

        for (int i = 0; i < generatedChunks.Count; i++)
        {
            GeneratedChunkMesh generatedChunk = generatedChunks[i];
            Mesh mesh = generatedChunk.Mesh;

            if (mesh == null || mesh.triangles.Length == 0)
            {
                DestroyGeneratedObject(mesh);
                continue;
            }

            GameObject chunkObject = new(mesh.name);
            chunkObject.transform.SetParent(transform, false);
            chunkObject.transform.localPosition = generatedChunk.Position;
            chunkObject.transform.localRotation = Quaternion.identity;
            chunkObject.transform.localScale = Vector3.one;

            MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
            TerrainGrassRenderer grassRenderer = chunkObject.AddComponent<TerrainGrassRenderer>();
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = material;
            ApplyTerrainWaterBodies(meshRenderer);
            RebuildGrass(mesh, meshFilter, meshRenderer, grassRenderer);

            _meshes.Add(mesh);
            _chunkObjects.Add(chunkObject);
        }
    }

    private void GenerateFeatures()
    {
        List<TerrainFeature> features = new(terrainFeatures.Count);
        List<GeneratedWaterBody> waterBodies = new(terrainFeatures.Count);

        for (int i = 0; i < terrainFeatures.Count; i++)
        {
            TerrainFeature feature = terrainFeatures[i];

            if (!feature.enabled)
            {
                continue;
            }

            feature.radius = Mathf.Max(0.001f, feature.radius);
            feature.sharpness = Mathf.Max(0.001f, feature.sharpness);
            feature.plateauRadius = Mathf.Clamp01(feature.plateauRadius);
            feature.shapeNoiseFrequency = Mathf.Max(0f, feature.shapeNoiseFrequency);
            feature.shapeNoiseStrength = Mathf.Clamp01(feature.shapeNoiseStrength);
            feature.shoreWidth = Mathf.Max(0f, feature.shoreWidth);

            if (feature.type == TerrainFeatureType.Lake)
            {
                feature.plateauRadius = Mathf.Clamp01((feature.radius - feature.shoreWidth) / feature.radius);
            }

            features.Add(feature);

            if (feature.type == TerrainFeatureType.Lake)
            {
                waterBodies.Add(new GeneratedWaterBody(
                    feature.position,
                    feature.radius,
                    feature.shoreWidth,
                    feature.waterSurfaceHeight,
                    feature.shapeNoiseFrequency,
                    feature.shapeNoiseStrength));
            }
        }

        _features = features.ToArray();
        _waterBodies = waterBodies.ToArray();
        UpdateTerrainWaterBodyShaderData();
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

    private void ApplyTerrainWaterBodies(Renderer terrainRenderer)
    {
        if (terrainRenderer == null)
        {
            return;
        }

        int count = Mathf.Min(_waterBodies.Length, MaxTerrainWaterBodyShaderCount);
        _terrainPropertyBlock ??= new MaterialPropertyBlock();
        terrainRenderer.GetPropertyBlock(_terrainPropertyBlock);
        _terrainPropertyBlock.SetInt(TerrainWaterBodyCountId, count);
        _terrainPropertyBlock.SetVectorArray(TerrainWaterBodiesId, _terrainWaterBodyShaderData);
        _terrainPropertyBlock.SetVectorArray(TerrainWaterBodyShapeDataId, _terrainWaterBodyShapeShaderData);
        _terrainPropertyBlock.SetMatrix(TerrainWaterWorldToIslandId, transform.worldToLocalMatrix);
        _terrainPropertyBlock.SetVector(TerrainWaterIslandCenterId, new Vector4(_islandCenter.x, _islandCenter.z, 0f, 0f));
        _terrainPropertyBlock.SetVector(
            TerrainWaterNoiseSeedOffsetsId,
            new Vector4(SeedOffset(53), SeedOffset(59), 0f, 0f));
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

            return;
        }

        if (!TryGetComponent(out WaterSimulation waterSimulation))
        {
            waterSimulation = gameObject.AddComponent<WaterSimulation>();
        }

        LakeWaterBody[] lakes = new LakeWaterBody[_waterBodies.Length];

        for (int i = 0; i < _waterBodies.Length; i++)
        {
            GeneratedWaterBody body = _waterBodies[i];
            lakes[i] = new LakeWaterBody(
                new Vector2(_islandCenter.x + body.LocalPosition.x, _islandCenter.z + body.LocalPosition.y),
                body.Radius,
                body.ShoreWidth,
                _islandCenter.y + body.SurfaceHeight);
        }

        waterSimulation.Initialize(this, chunksPerAxis, chunkSize, lakes);
    }

    private void GenerateRandomFeatures(List<TerrainFeature> features, int existingFeatureCount, int count, int generationSeed)
    {
        if (count <= 0)
        {
            return;
        }

        System.Random random = new(generationSeed);
        int attempts = count * 32;
        float edgePadding = Mathf.Max(0f, islandRadius * featureEdgePadding);
        Vector2 usableRadiusRange = ClampFeatureRadiusRangeForPlacement(edgePadding);

        for (int attempt = 0; attempt < attempts && features.Count < existingFeatureCount + count; attempt++)
        {
            float radius = RandomRange(random, usableRadiusRange);
            float placementRadius = Mathf.Max(0f, islandRadius - edgePadding - radius);
            float distance = Mathf.Sqrt((float)random.NextDouble()) * placementRadius;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector2 position = new(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);

            if (OverlapsExistingFeature(position, radius, features))
            {
                continue;
            }

            TerrainFeatureType type = PickFeatureType(random);
            TerrainFeature feature = CreateDefaultTerrainFeature(type);
            feature.position = position;
            feature.radius = radius;
            feature.height = RandomRange(random, featureHeightRange);
            feature.sharpness = RandomRange(random, featureSharpnessRange);
            features.Add(feature);
        }

        while (features.Count < existingFeatureCount + count)
        {
            float radius = RandomRange(random, usableRadiusRange);
            float placementRadius = Mathf.Max(0f, islandRadius - edgePadding - radius);
            float distance = Mathf.Sqrt((float)random.NextDouble()) * placementRadius;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            TerrainFeatureType type = PickFeatureType(random);
            TerrainFeature feature = CreateDefaultTerrainFeature(type);
            feature.position = new Vector2(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);
            feature.radius = radius;
            feature.height = RandomRange(random, featureHeightRange);
            feature.sharpness = RandomRange(random, featureSharpnessRange);
            features.Add(feature);
        }
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

    private bool OverlapsExistingFeature(Vector2 position, float radius, List<TerrainFeature> features)
    {
        for (int i = 0; i < features.Count; i++)
        {
            TerrainFeature feature = features[i];
            float minimumDistance = (radius + feature.radius) * Mathf.Clamp01(featureOverlap);

            if ((position - feature.position).sqrMagnitude < minimumDistance * minimumDistance)
            {
                return true;
            }
        }

        return false;
    }

    private TerrainFeature CreateDefaultTerrainFeature(TerrainFeatureType featureType, Vector2 position)
    {
        TerrainFeature feature = CreateDefaultTerrainFeature(featureType);
        feature.position = position;
        return feature;
    }

    public static TerrainFeature CreateDefaultTerrainFeature(TerrainFeatureType featureType)
    {
        TerrainFeature feature = new()
        {
            enabled = true,
            type = featureType,
            position = Vector2.zero
        };

        ApplyDefaultTerrainFeatureValues(ref feature);
        return feature;
    }

    public static void ApplyDefaultTerrainFeatureValues(ref TerrainFeature feature)
    {
        switch (feature.type)
        {
            case TerrainFeatureType.Hill:
                feature.radius = 5f;
                feature.height = 2.5f;
                feature.sharpness = 1.5f;
                feature.plateauRadius = 0f;
                feature.shapeNoiseFrequency = 0.25f;
                feature.shapeNoiseStrength = 0.1f;
                break;
            case TerrainFeatureType.Mountain:
                feature.radius = 5f;
                feature.height = 5.5f;
                feature.sharpness = 3.5f;
                feature.plateauRadius = 0f;
                feature.shapeNoiseFrequency = 0.35f;
                feature.shapeNoiseStrength = 0.15f;
                break;
            case TerrainFeatureType.Terrace:
                feature.radius = 5.5f;
                feature.height = 3f;
                feature.sharpness = 1.8f;
                feature.plateauRadius = 0.35f;
                feature.shapeNoiseFrequency = 0.2f;
                feature.shapeNoiseStrength = 0.08f;
                break;
            case TerrainFeatureType.Basin:
                feature.radius = 5.5f;
                feature.height = 2.5f;
                feature.sharpness = 1.6f;
                feature.plateauRadius = 0.25f;
                feature.shapeNoiseFrequency = 0.25f;
                feature.shapeNoiseStrength = 0.12f;
                feature.shoreWidth = 1.2f;
                feature.waterSurfaceHeight = 0f;
                break;
            case TerrainFeatureType.Lake:
                feature.radius = 4.5f;
                feature.height = 2.2f;
                feature.sharpness = 1.8f;
                feature.plateauRadius = 0.35f;
                feature.shapeNoiseFrequency = 0.18f;
                feature.shapeNoiseStrength = 0.08f;
                feature.shoreWidth = 1.4f;
                feature.waterSurfaceHeight = -0.35f;
                break;
        }
    }

    private TerrainFeatureType PickFeatureType(System.Random random)
    {
        float total = hillWeight + mountainWeight + terraceWeight + basinWeight + lakeWeight;

        if (total <= 0f)
        {
            return TerrainFeatureType.Hill;
        }

        float value = (float)random.NextDouble() * total;

        if (value < hillWeight)
        {
            return TerrainFeatureType.Hill;
        }

        value -= hillWeight;

        if (value < mountainWeight)
        {
            return TerrainFeatureType.Mountain;
        }

        value -= mountainWeight;

        if (value < terraceWeight)
        {
            return TerrainFeatureType.Terrace;
        }

        value -= terraceWeight;

        return value < basinWeight
            ? TerrainFeatureType.Basin
            : TerrainFeatureType.Lake;
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

        for (int i = 0; i < terrainFeatures.Count; i++)
        {
            TerrainFeature feature = terrainFeatures[i];

            if (feature.enabled)
            {
                maxHeight = Mathf.Max(maxHeight, Mathf.Max(0f, feature.height));
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

        for (int i = 0; i < terrainFeatures.Count; i++)
        {
            TerrainFeature feature = terrainFeatures[i];
            feature.height = Mathf.Min(feature.height, maxHeight);
            terrainFeatures[i] = feature;
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
            transform.worldToLocalMatrix,
            new Vector4(_islandCenter.x, _islandCenter.z, 0f, 0f),
            new Vector4(SeedOffset(53), SeedOffset(59), 0f, 0f));
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
        public GeneratedChunkMesh(Vector3Int position, Mesh mesh)
        {
            Position = position;
            Mesh = mesh;
            FirstTriangleIndex = 0;
            TriangleCount = 0;
        }

        public Vector3Int Position { get; }
        public Mesh Mesh { get; }
        public int FirstTriangleIndex { get; set; }
        public int TriangleCount { get; set; }
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
[CustomPropertyDrawer(typeof(TerrainFeature))]
public sealed class TerrainFeatureDrawer : PropertyDrawer
{
    private const float VerticalSpacing = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 11f + VerticalSpacing * 10f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty enabledProperty = property.FindPropertyRelative("enabled");
        SerializedProperty typeProperty = property.FindPropertyRelative("type");
        SerializedProperty positionProperty = property.FindPropertyRelative("position");
        SerializedProperty radiusProperty = property.FindPropertyRelative("radius");
        SerializedProperty heightProperty = property.FindPropertyRelative("height");
        SerializedProperty sharpnessProperty = property.FindPropertyRelative("sharpness");
        SerializedProperty plateauRadiusProperty = property.FindPropertyRelative("plateauRadius");
        SerializedProperty shapeNoiseFrequencyProperty = property.FindPropertyRelative("shapeNoiseFrequency");
        SerializedProperty shapeNoiseStrengthProperty = property.FindPropertyRelative("shapeNoiseStrength");
        SerializedProperty shoreWidthProperty = property.FindPropertyRelative("shoreWidth");
        SerializedProperty waterSurfaceHeightProperty = property.FindPropertyRelative("waterSurfaceHeight");

        if (radiusProperty.floatValue <= 0f || sharpnessProperty.floatValue <= 0f)
        {
            ApplyDefaults(property);
        }

        EditorGUI.BeginProperty(position, label, property);

        Rect line = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(line, enabledProperty, label);

        line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(line, typeProperty);

        if (EditorGUI.EndChangeCheck())
        {
            ApplyDefaults(property);
        }

        line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        EditorGUI.PropertyField(line, positionProperty);
        line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        EditorGUI.PropertyField(line, radiusProperty);
        line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        EditorGUI.PropertyField(line, heightProperty);
        line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        EditorGUI.PropertyField(line, sharpnessProperty);
        line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        EditorGUI.PropertyField(line, plateauRadiusProperty);
        line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        EditorGUI.PropertyField(line, shapeNoiseFrequencyProperty);
        line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        EditorGUI.PropertyField(line, shapeNoiseStrengthProperty);
        line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        EditorGUI.PropertyField(line, shoreWidthProperty);
        line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        EditorGUI.PropertyField(line, waterSurfaceHeightProperty);

        EditorGUI.EndProperty();
    }

    private static void ApplyDefaults(SerializedProperty property)
    {
        SerializedProperty typeProperty = property.FindPropertyRelative("type");
        SerializedProperty enabledProperty = property.FindPropertyRelative("enabled");
        TerrainFeature feature = IslandGenerator.CreateDefaultTerrainFeature(
            (TerrainFeatureType)typeProperty.enumValueIndex);

        enabledProperty.boolValue = true;
        property.FindPropertyRelative("radius").floatValue = feature.radius;
        property.FindPropertyRelative("height").floatValue = feature.height;
        property.FindPropertyRelative("sharpness").floatValue = feature.sharpness;
        property.FindPropertyRelative("plateauRadius").floatValue = feature.plateauRadius;
        property.FindPropertyRelative("shapeNoiseFrequency").floatValue = feature.shapeNoiseFrequency;
        property.FindPropertyRelative("shapeNoiseStrength").floatValue = feature.shapeNoiseStrength;
        property.FindPropertyRelative("shoreWidth").floatValue = feature.shoreWidth;
        property.FindPropertyRelative("waterSurfaceHeight").floatValue = feature.waterSurfaceHeight;
    }
}

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

            if (property.name == "terrainFeatures")
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
            if (GUILayout.Button("Add Hill"))
            {
                Run(generator, () => generator.AddTerrainFeature(TerrainFeatureType.Hill));
            }

            if (GUILayout.Button("Add Mountain"))
            {
                Run(generator, () => generator.AddTerrainFeature(TerrainFeatureType.Mountain));
            }

            if (GUILayout.Button("Add Terrace"))
            {
                Run(generator, () => generator.AddTerrainFeature(TerrainFeatureType.Terrace));
            }

            if (GUILayout.Button("Add Basin"))
            {
                Run(generator, () => generator.AddTerrainFeature(TerrainFeatureType.Basin));
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
