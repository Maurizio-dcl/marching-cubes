using DefaultNamespace;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum TerrainFeatureType
{
    Hill,
    Mountain,
    Terrace
}

[System.Serializable]
public struct TerrainFeature
{
    public Vector2 position;
    [Min(0.001f)] public float radius;
    public float height;
    [Min(0.001f)] public float sharpness;
    public TerrainFeatureType type;
}

[ExecuteAlways]
public sealed class IslandGenerator : MonoBehaviour
{
    private const string ChunkObjectNamePrefix = "Island Chunk ";

    [Header("Chunk Grid")]
    [SerializeField] private int seed = 12345;
    [SerializeField, Min(1)] private int chunksPerAxis = 2;
    [SerializeField, Range(1, 64)] private int density = 16;
    [SerializeField, Min(1)] private int chunkSize = 8;
    [SerializeField, Range(-32f, 32f)] private float isoLevel = 0f;
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
    [SerializeField] private bool useExplicitFeatureCount;
    [SerializeField, Min(0)] private int featureCount = 5;
    [SerializeField, Min(0f)] private float featuresPerChunkColumn = 1.2f;
    [SerializeField, Min(0f)] private float hillWeight = 0.55f;
    [SerializeField, Min(0f)] private float mountainWeight = 0.3f;
    [SerializeField, Min(0f)] private float terraceWeight = 0.15f;
    [SerializeField] private Vector2 featureRadiusRange = new(1.25f, 3.25f);
    [SerializeField] private Vector2 featureHeightRange = new(0.75f, 3.25f);
    [SerializeField] private Vector2 featureSharpnessRange = new(1f, 3.5f);
    [SerializeField, Min(1)] private int terraceStepCount = 4;
    [SerializeField, Range(0f, 1f)] private float terraceSmoothing = 0.15f;

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
    [SerializeField] private Color[] grassColors =
    {
        new(0.33f, 0.68f, 0.24f, 1f),
        new(0.24f, 0.55f, 0.18f, 1f),
        new(0.47f, 0.77f, 0.29f, 1f)
    };

    private readonly List<GameObject> _chunkObjects = new();
    private readonly List<Mesh> _meshes = new();
    private TerrainFeature[] _features = System.Array.Empty<TerrainFeature>();
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
        }
    }

    private void OnValidate()
    {
        chunksPerAxis = Mathf.Max(1, chunksPerAxis);
        chunkSize = Mathf.Max(1, chunkSize);
        featureRadiusRange = SortMinMax(featureRadiusRange, 0.001f);
        featureHeightRange = SortMinMax(featureHeightRange, 0f);
        featureSharpnessRange = SortMinMax(featureSharpnessRange, 0.001f);
        maxGrassHeightTexels = Mathf.Max(minGrassHeightTexels, maxGrassHeightTexels);
        ResolveDefaultGrassAssets();

        if (ShouldAutoRegenerate())
        {
            RequestRefreshIslandPreview();
        }
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
        float bottomHeight = EvaluateBottomHeight(localPosition, radius01, topHeight);
        float verticalDensity = Mathf.Min(topHeight - worldPosition.y, worldPosition.y - bottomHeight);
        float edgeDensity = localRadius - radialDistance;

        return Mathf.Min(verticalDensity, edgeDensity);
    }

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
        float height = baseSurfaceHeight;

        if (topNoiseStrength > 0f && topNoiseFrequency > 0f)
        {
            height += SampleSignedNoise2D(
                islandLocalPosition.x + SeedOffset(23),
                islandLocalPosition.z + SeedOffset(29),
                topNoiseFrequency) * topNoiseStrength;
        }

        height += EvaluateTerrainFeatures(new Vector2(islandLocalPosition.x, islandLocalPosition.z));
        height -= EdgeBlend(radius01) * edgeDrop;

        return height;
    }

    public float EvaluateBottomHeight(Vector3 islandLocalPosition, float radius01)
    {
        return EvaluateBottomHeight(
            islandLocalPosition,
            radius01,
            EvaluateTopHeight(islandLocalPosition, radius01));
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
        float height = 0f;

        for (int i = 0; i < _features.Length; i++)
        {
            TerrainFeature feature = _features[i];
            float distance01 = Vector2.Distance(islandLocalXZ, feature.position) / feature.radius;

            if (distance01 >= 1f)
            {
                continue;
            }

            float smoothFalloff = Mathf.SmoothStep(1f, 0f, distance01);

            switch (feature.type)
            {
                case TerrainFeatureType.Hill:
                    height += smoothFalloff * feature.height;
                    break;
                case TerrainFeatureType.Mountain:
                    height += Mathf.Pow(1f - distance01, feature.sharpness) * feature.height;
                    break;
                case TerrainFeatureType.Terrace:
                    float terrace = Mathf.Pow(smoothFalloff, feature.sharpness);
                    height += QuantizeTerrace(terrace) * feature.height;
                    break;
            }
        }

        return height;
    }

    private void RefreshIslandPreview()
    {
        ClearIsland();
        InitializeGrid();
        GenerateFeatures();
        GenerateChunks();
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

                    GameObject chunkObject = new($"Island Chunk {x} {y} {z}");
                    chunkObject.transform.SetParent(transform, false);
                    chunkObject.transform.localPosition = chunk.Position;
                    chunkObject.transform.localRotation = Quaternion.identity;
                    chunkObject.transform.localScale = Vector3.one;

                    MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
                    MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
                    TerrainGrassRenderer grassRenderer = chunkObject.AddComponent<TerrainGrassRenderer>();
                    meshFilter.sharedMesh = mesh;
                    meshRenderer.sharedMaterial = material;
                    RebuildGrass(mesh, meshFilter, meshRenderer, grassRenderer);

                    _meshes.Add(mesh);
                    _chunkObjects.Add(chunkObject);
                }
            }
        }
    }

    private void GenerateFeatures()
    {
        int count = useExplicitFeatureCount
            ? featureCount
            : Mathf.RoundToInt(chunksPerAxis * chunksPerAxis * featuresPerChunkColumn);

        if (count <= 0)
        {
            _features = System.Array.Empty<TerrainFeature>();
            return;
        }

        List<TerrainFeature> features = new(count);
        System.Random random = new(seed);
        int attempts = count * 24;
        float edgePadding = Mathf.Max(featureRadiusRange.y + 0.5f, islandRadius * 0.16f);

        for (int attempt = 0; attempt < attempts && features.Count < count; attempt++)
        {
            float radius = RandomRange(random, featureRadiusRange);
            float placementRadius = Mathf.Max(0f, islandRadius - edgePadding - radius);
            float distance = Mathf.Sqrt((float)random.NextDouble()) * placementRadius;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector2 position = new(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);

            if (OverlapsExistingFeature(position, radius, features))
            {
                continue;
            }

            features.Add(new TerrainFeature
            {
                position = position,
                radius = radius,
                height = RandomRange(random, featureHeightRange),
                sharpness = RandomRange(random, featureSharpnessRange),
                type = PickFeatureType(random)
            });
        }

        _features = features.ToArray();
    }

    private bool OverlapsExistingFeature(Vector2 position, float radius, List<TerrainFeature> features)
    {
        for (int i = 0; i < features.Count; i++)
        {
            TerrainFeature feature = features[i];
            float minimumDistance = (radius + feature.radius) * 0.65f;

            if ((position - feature.position).sqrMagnitude < minimumDistance * minimumDistance)
            {
                return true;
            }
        }

        return false;
    }

    private TerrainFeatureType PickFeatureType(System.Random random)
    {
        float total = hillWeight + mountainWeight + terraceWeight;

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

        return value < mountainWeight
            ? TerrainFeatureType.Mountain
            : TerrainFeatureType.Terrace;
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
            grassColors);
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
}
