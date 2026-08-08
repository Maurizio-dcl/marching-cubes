using DefaultNamespace;
using DefaultNamespace.Terrain;
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

[ExecuteAlways]
public sealed class IslandGenerator : MonoBehaviour
{
    private const string ChunkObjectNamePrefix = "Island Chunk ";
    private const string LegacyWaterChunkObjectNamePrefix = "Water Chunk ";
    private const float NoiseEditorWidth = 30f;
    private const float NoiseEditorSurfaceStart = 10f;
    private static readonly int DownTexId = Shader.PropertyToID("_DownTex");
    private static readonly int UseDownTextureId = Shader.PropertyToID("_UseDownTexture");
    private static int s_nextRuntimeIslandId = 1;

    [Header("Chunk Grid")]
    [SerializeField] private int seed = 12345;
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
    [SerializeField] private bool useUndersideProfileCurve = true;
    [SerializeField, Min(0.001f)] private float undersideExponent = 1.8f;
    [SerializeField] private AnimationCurve undersideProfile = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField, Range(0f, 1f)] private float edgeFalloff = 0.18f;
    [SerializeField, Min(0f)] private float edgeDrop = 1.5f;

    [Header("Noise")]
    [SerializeField, Min(0f)] private float footprintNoiseFrequency = 0.18f;
    [SerializeField, Range(0f, 1f)] private float footprintNoiseStrength = 0.2f;
    [SerializeField, Min(0f)] private float undersideNoiseFrequency = 0.22f;
    [SerializeField, Min(0f)] private float undersideNoiseStrength = 1.2f;
    [Header("Layered Density Noise")]
    [SerializeField, Min(0.001f), Tooltip("World units per Vercel noise unit. 1 means one Unity unit maps to one Vercel generator unit.")]
    private float noiseWorldScale = 1f;
    [SerializeField, Min(0.001f), Tooltip("Vertical multiplier applied after world scale. 1 keeps the noise field isotropic.")]
    private float noiseVerticalScale = 1f;
    [SerializeField, Range(10f, 100f), Tooltip("Range: 10-100. Lower values produce higher-frequency detail. Implicit amplitude: 1.")]
    private float layer1Frequency = 50f;
    [SerializeField, Range(5f, 75f), Tooltip("Range: 5-75. Lower values produce higher-frequency detail. Implicit amplitude: 0.5.")]
    private float layer2Frequency = 25f;
    [SerializeField, Range(2f, 50f), Tooltip("Range: 2-50. Lower values produce higher-frequency detail. Implicit amplitude: 0.25.")]
    private float layer3Frequency = 10f;

    [Header("Connected Geometry")]
    [SerializeField] private bool removeDetachedGeometry = true;

    [Header("LOD and Scheduling")]
    [SerializeField, Tooltip("Runtime only. Editor previews are always generated at full terrain density.")]
    private bool enableChunkLod = true;
    [SerializeField, Min(0)] private int recentlyVisibleFrameHold = 90;
    [SerializeField, Min(1)] private int maxChunkBuildsPerFrame = 2;
    [SerializeField] private Transform lodFocus;
    [SerializeField] private TerrainLodLevel[] lodLevels =
    {
        new() { enterDistance = 28f, exitDistance = 34f, terrainCellsPerAxis = 16, meshUpdateIntervalFrames = 1, castShadows = true },
        new() { enterDistance = 54f, exitDistance = 64f, terrainCellsPerAxis = 8, meshUpdateIntervalFrames = 8, castShadows = false },
        new() { enterDistance = 96f, exitDistance = 112f, terrainCellsPerAxis = 4, meshUpdateIntervalFrames = 24, castShadows = false }
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
    private Material _defaultIslandMaterial;
    private Material _defaultGrassMaterial;
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

        RefreshTerrainRenderers();
    }

    private void OnValidate()
    {
        EnsureRuntimeIslandId();
        chunkSize = Mathf.Max(1, chunkSize);
        ClampNoiseLayerFrequencies();
        maxGrassHeightTexels = Mathf.Max(minGrassHeightTexels, maxGrassHeightTexels);
        ApplyBoundsConstraints();
        ResolveDefaultGrassAssets();

        if (ShouldAutoRegenerate())
        {
            RequestRefreshIslandPreview();
            return;
        }

        RefreshTerrainRenderers();
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

    [ContextMenu("Randomize Island Seed")]
    public void RandomizeIslandSeed()
    {
        seed = Random.Range(int.MinValue, int.MaxValue);
        RequestRefreshIslandPreview();
    }

    [ContextMenu("Clear Island")]
    private void ClearIsland()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (!child.name.StartsWith(ChunkObjectNamePrefix)
                && !child.name.StartsWith(LegacyWaterChunkObjectNamePrefix))
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
    }

    public float SampleDensity(Vector3 worldPosition)
    {
        Vector3 localPosition = worldPosition - _islandCenter;
        float radialDistance = new Vector2(localPosition.x, localPosition.z).magnitude;
        float localRadius = EvaluateLocalRadius(localPosition);
        float radius01 = Mathf.Clamp01(radialDistance / Mathf.Max(localRadius, 0.001f));
        float edgeDensity = localRadius - radialDistance;
        float edgeMask = InteriorFeatureMask(radius01);
        float noiseDensity = EvaluateNoiseEditorDensity(localPosition) * edgeMask - EdgeBlend(radius01) * edgeDrop;
        float bottomHeight = EvaluateBottomHeight(localPosition, radius01, 0f);
        float bottomDensity = localPosition.y - bottomHeight;

        return Mathf.Min(noiseDensity, edgeDensity, bottomDensity);
    }

    public float IsoLevel => isoLevel;
    public Vector3 GridOrigin => _gridOrigin;
    public Vector3 IslandCenter => _islandCenter;
    public float ChunkSize => chunkSize;
    public int ChunksPerAxis => CalculateChunksPerAxis();
    public int TerrainCellsPerAxis => density;
    public float TotalSize => _totalSize;
    public Bounds WorldBounds => new((Vector3)_gridOrigin + Vector3.one * (_totalSize * 0.5f), Vector3.one * _totalSize);
    private bool IsChunkLodActive => enableChunkLod && Application.isPlaying;
    private float IslandDepth => Mathf.Max(0.001f, islandRadius * 0.5f);

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
        return -EdgeBlend(radius01) * edgeDrop;
    }

    public float EvaluateBottomHeight(Vector3 islandLocalPosition, float radius01)
    {
        return EvaluateBottomHeight(islandLocalPosition, radius01, EvaluateTopHeight(islandLocalPosition, radius01));
    }

    private float EvaluateBottomHeight(Vector3 islandLocalPosition, float radius01, float topHeight)
    {
        float profile = useUndersideProfileCurve && undersideProfile != null && undersideProfile.length > 0
            ? Mathf.Clamp01(undersideProfile.Evaluate(radius01))
            : Mathf.Pow(1f - Mathf.Clamp01(radius01), undersideExponent);

        float islandDepth = IslandDepth;
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

    private void RefreshIslandPreview()
    {
        using (IslandProfiler.Refresh.Auto())
        {
            ApplyBoundsConstraints();
            ClearIsland();
            InitializeGrid();
            GenerateChunks();
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
        int chunksPerAxis = CalculateChunksPerAxis();
        _totalSize = chunksPerAxis * chunkSize;
        int origin = Mathf.FloorToInt(_totalSize * -0.5f);
        _gridOrigin = new Vector3Int(origin, origin, origin);
        _islandCenter = (Vector3)_gridOrigin + Vector3.one * (_totalSize * 0.5f);
        _islandCenter.y = 0f;
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
        ConfigureTerrainMaterial(material);
        int chunksPerAxis = CalculateChunksPerAxis();
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
                    : new TerrainLodDecision(0, true, true, 0f);

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
                RebuildGrass(mesh, chunk.View.MeshFilter, chunk.View.MeshRenderer, chunk.View.GrassRenderer);
            }
            else if (mesh.GetIndexCount(0) > 0)
            {
                Material material = islandMaterial != null ? islandMaterial : GetDefaultIslandMaterial();
                ConfigureTerrainMaterial(material);
                CreateTerrainChunkView(chunk, material);
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

    public void NotifyTerrainModified(Bounds modifiedWorldBounds)
    {
        using (IslandProfiler.DirtyTerrain.Auto())
        {
            MarkTerrainDirty(modifiedWorldBounds, true);
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

    private void RefreshTerrainRenderers()
    {
        InitializeGrid();
        ConfigureTerrainMaterial(islandMaterial != null ? islandMaterial : _defaultIslandMaterial);

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);

            if (!child.name.StartsWith(ChunkObjectNamePrefix)
                || !child.TryGetComponent(out MeshRenderer meshRenderer))
            {
                continue;
            }

            if (child.TryGetComponent(out MeshFilter meshFilter)
                && child.TryGetComponent(out TerrainGrassRenderer grassRenderer))
            {
                RebuildGrass(meshFilter.sharedMesh, meshFilter, meshRenderer, grassRenderer);
            }
        }
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

        float totalSize = CalculateChunksPerAxis() * Mathf.Max(1, chunkSize);
        float halfSize = totalSize * 0.5f;
        float padding = Mathf.Min(boundsPadding, Mathf.Max(0f, halfSize - 0.001f));
        float maxHorizontalRadius = Mathf.Max(0.001f, halfSize - padding);
        float footprintMultiplier = 1f + Mathf.Max(0f, footprintNoiseStrength);
        islandRadius = Mathf.Min(islandRadius, maxHorizontalRadius / footprintMultiplier);

    }

    private int CalculateChunksPerAxis()
    {
        float requiredSize = CalculateRequiredGridSize();
        return Mathf.Max(1, Mathf.CeilToInt(requiredSize / Mathf.Max(1, chunkSize)));
    }

    private float CalculateRequiredGridSize()
    {
        float padding = Mathf.Max(0f, boundsPadding);
        float footprintMultiplier = 1f + Mathf.Max(0f, footprintNoiseStrength);
        float horizontalExtent = Mathf.Max(0.001f, islandRadius) * footprintMultiplier + padding;

        float islandDepth = IslandDepth;
        float topLift = Mathf.Max(0.001f, islandDepth);
        float topExtent = topLift + padding;
        float bottomExtent = Mathf.Max(0.001f, islandDepth)
            + Mathf.Max(0f, undersideNoiseStrength)
            + padding;
        float verticalExtent = Mathf.Max(topExtent, bottomExtent);

        return Mathf.Max(horizontalExtent, verticalExtent) * 2f;
    }

    private static void ConfigureTerrainMaterial(Material material)
    {
        if (material == null || !material.HasProperty(UseDownTextureId) || !material.HasProperty(DownTexId))
        {
            return;
        }

        material.SetFloat(UseDownTextureId, material.GetTexture(DownTexId) != null ? 1f : 0f);
    }

    private void ClampNoiseLayerFrequencies()
    {
        layer1Frequency = Mathf.Clamp(layer1Frequency, 10f, 100f);
        layer2Frequency = Mathf.Clamp(layer2Frequency, 5f, 75f);
        layer3Frequency = Mathf.Clamp(layer3Frequency, 2f, 50f);
        noiseWorldScale = Mathf.Max(0.001f, noiseWorldScale);
        noiseVerticalScale = Mathf.Max(0.001f, noiseVerticalScale);
    }

    private float EvaluateNoiseEditorDensity(Vector3 islandLocalPosition)
    {
        Vector3 noisePosition = MapToNoiseEditorDomain(islandLocalPosition);
        float editorValue =
            SampleSimplexLayer(noisePosition, layer1Frequency, 1f, 0)
            + SampleSimplexLayer(noisePosition, layer2Frequency, 0.5f, 1)
            + SampleSimplexLayer(noisePosition, layer3Frequency, 0.25f, 2)
            + EvaluateNoiseEditorVerticalBias(noisePosition.y);

        return -editorValue;
    }

    private float SampleSignedNoise2D(float x, float z, float frequency)
    {
        return SimplexNoise3D.SampleSigned(x * frequency, 0f, z * frequency);
    }

    private Vector3 MapToNoiseEditorDomain(Vector3 islandLocalPosition)
    {
        float horizontalScale = 1f / Mathf.Max(0.001f, noiseWorldScale);
        return new Vector3(
            islandLocalPosition.x * horizontalScale,
            NoiseEditorSurfaceStart + islandLocalPosition.y * horizontalScale * noiseVerticalScale,
            islandLocalPosition.z * horizontalScale);
    }

    private static float EvaluateNoiseEditorVerticalBias(float y)
    {
        if (y <= 0f)
        {
            return -2f;
        }

        if (y < 10f)
        {
            float offset = y - 10f;
            return 0.002f * offset * offset * offset;
        }

        return (y - 10f) / 20f;
    }

    private float SampleSimplexLayer(Vector3 noisePosition, float frequency, float amplitude, int layer)
    {
        frequency = Mathf.Max(0.001f, frequency);
        return SimplexNoise3D.SampleSigned(
            (noisePosition.x + SeedOffset(23 + layer * 2)) / frequency,
            (noisePosition.y + SeedOffset(71 + layer * 2)) / frequency,
            (noisePosition.z + SeedOffset(29 + layer * 2)) / frequency) * amplitude;
    }

    private float SeedOffset(int salt)
    {
        unchecked
        {
            int mixed = seed * 73856093 ^ salt * 19349663;
            return (mixed & 0xffff) * 0.017f;
        }
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
            grassWindVariation);
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
