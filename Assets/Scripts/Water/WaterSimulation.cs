using System.Collections.Generic;
using DefaultNamespace.Terrain;
using UnityEngine;

namespace DefaultNamespace.Water
{
    [ExecuteAlways]
    public sealed class WaterSimulation : MonoBehaviour
    {
        private const string WaterChunkPrefix = "Water Chunk ";
        private const string WaterfallObjectName = "Waterfalls";
        private static readonly int WaterCellSizeId = Shader.PropertyToID("_WaterCellSize");
        private static readonly int WaterGridOriginId = Shader.PropertyToID("_WaterGridOrigin");

        [SerializeField] private WaterSimulationSettings settings = new();
        [SerializeField] private Material waterMaterial;
        [SerializeField] private MonoBehaviour outflowConsumerComponent;

        private readonly List<WaterSource> _sources = new();
        private readonly List<WaterfallSource> _waterfallSources = new();
        private WaterGrid _grid;
        private WaterGroundSampler _groundSampler;
        private WaterFlowSolver _flowSolver;
        private WaterMeshBuilder _meshBuilder;
        private WaterChunk[] _chunks;
        private ITerrainDensityField _terrain;
        private IWaterOutflowConsumer _outflowConsumer;
        private WaterOutflow[] _worldOutflows;
        private GameObject _waterfallObject;
        private Mesh _waterfallMesh;
        private MaterialPropertyBlock _waterPropertyBlock;
        private Material _defaultMaterial;
        private float _accumulator;
        private readonly List<Vector3> _waterfallVertices = new();
        private readonly List<Vector3> _waterfallNormals = new();
        private readonly List<Vector2> _waterfallUvs = new();
        private readonly List<int> _waterfallIndices = new();

        public WaterGrid Grid => _grid;

        public void Clear()
        {
            ClearWaterObjects();
            _grid = null;
            _groundSampler = null;
            _flowSolver = null;
            _meshBuilder = null;
            _chunks = null;
            _terrain = null;
            _worldOutflows = null;
            _waterfallObject = null;
            _waterfallMesh = null;
            _waterPropertyBlock = null;
            _waterfallSources.Clear();
            _accumulator = 0f;
        }

        public void Initialize(
            ITerrainDensityField terrain,
            int chunksPerAxis,
            float chunkWorldSize,
            IReadOnlyList<LakeWaterBody> lakes,
            IReadOnlyList<RiverWaterBody> rivers)
        {
            using (IslandProfiler.WaterInitialize.Auto())
            {
                InitializeInternal(terrain, chunksPerAxis, chunkWorldSize, lakes, rivers);
            }
        }

        private void InitializeInternal(
            ITerrainDensityField terrain,
            int chunksPerAxis,
            float chunkWorldSize,
            IReadOnlyList<LakeWaterBody> lakes,
            IReadOnlyList<RiverWaterBody> rivers)
        {
            _terrain = terrain;
            _outflowConsumer = outflowConsumerComponent as IWaterOutflowConsumer;

            ClearWaterObjects();

            if (settings == null)
            {
                settings = new WaterSimulationSettings();
            }

            int cellsPerChunk = settings.cellsPerTerrainChunkAxis;
            _grid = new WaterGrid(terrain.WorldBounds, chunksPerAxis, chunkWorldSize, cellsPerChunk);
            _groundSampler = new WaterGroundSampler(terrain, _grid, settings);
            _flowSolver = new WaterFlowSolver(_grid, settings, _grid.ChunksPerAxis * _grid.ChunksPerAxis * 8);
            _meshBuilder = new WaterMeshBuilder(_grid, settings);
            _chunks = new WaterChunk[_grid.ChunksPerAxis * _grid.ChunksPerAxis];
            _worldOutflows = new WaterOutflow[_grid.ChunksPerAxis * _grid.ChunksPerAxis * 8];

            CreateChunks();
            CreateWaterfallRenderer();
            _groundSampler.RecalculateAll();
            WaterLakeInitializer.Initialize(_grid, _terrain.WorldBounds, lakes, rivers);
            BuildRiverWaterfallSources(rivers);
            MarkAllChunksDirtyAndActive();
            RebuildDirtyMeshes();
            UpdateWaterfalls(null, 0);
        }

        public void SetChunkRuntimeState(
            Vector2Int coordinate,
            bool renderAllowed,
            bool simulationAllowed,
            int meshUpdateIntervalFrames,
            int simulationIntervalFrames)
        {
            if (_grid == null || _chunks == null)
            {
                return;
            }

            if (coordinate.x < 0 || coordinate.y < 0 || coordinate.x >= _grid.ChunksPerAxis || coordinate.y >= _grid.ChunksPerAxis)
            {
                return;
            }

            WaterChunk chunk = _chunks[coordinate.x + coordinate.y * _grid.ChunksPerAxis];
            bool renderChanged = chunk.IsRenderAllowed != renderAllowed;
            chunk.IsRenderAllowed = renderAllowed;
            chunk.IsSimulationAllowed = simulationAllowed;
            chunk.MeshUpdateIntervalFrames = Mathf.Max(1, meshUpdateIntervalFrames);
            chunk.SimulationIntervalFrames = Mathf.Max(1, simulationIntervalFrames);

            if (chunk.MeshRenderer != null)
            {
                chunk.MeshRenderer.enabled = renderAllowed;
            }

            if (!simulationAllowed)
            {
                chunk.IsActive = false;
            }

            if (renderChanged && renderAllowed)
            {
                chunk.IsMeshDirty = true;
            }
        }

        public void AddSource(WaterSource source)
        {
            _sources.Add(source);
            MarkBoundsDirty(new Bounds(new Vector3(source.WorldXZ.x, _terrain.WorldBounds.center.y, source.WorldXZ.y), Vector3.one * source.Radius * 2f));
        }

        public void ClearSources()
        {
            _sources.Clear();
        }

        public void NotifyTerrainChanged(Bounds modifiedWorldBounds)
        {
            if (_grid == null || _groundSampler == null)
            {
                return;
            }

            _groundSampler.Recalculate(modifiedWorldBounds);
            MarkBoundsDirty(modifiedWorldBounds);
        }

        public bool TrySampleWater(
            Vector3 worldPosition,
            out float surfaceHeight,
            out float depth,
            out Vector2 flowVelocity)
        {
            surfaceHeight = 0f;
            depth = 0f;
            flowVelocity = Vector2.zero;

            Vector3 localPosition = transform.InverseTransformPoint(worldPosition);

            if (_grid == null || !_grid.TryWorldToCell(localPosition, out int x, out int z))
            {
                return false;
            }

            int index = _grid.Index(x, z);
            depth = _grid.WaterDepths[index];

            if (!_grid.HasGround(index) || depth <= settings.renderDepthThreshold)
            {
                return false;
            }

            Vector3 localSurface = new(localPosition.x, _grid.GroundHeights[index] + depth, localPosition.z);
            surfaceHeight = transform.TransformPoint(localSurface).y;
            flowVelocity = _grid.FlowVelocities[index];
            return true;
        }

        private void FixedUpdate()
        {
            if (!Application.isPlaying || _grid == null)
            {
                return;
            }

            _accumulator += Time.fixedDeltaTime;
            float step = Mathf.Max(0.001f, settings.fixedSimulationStep);

            while (_accumulator >= step)
            {
                Simulate(step);
                _accumulator -= step;
            }

            RebuildDirtyMeshes();
        }

        private void Simulate(float step)
        {
            using (IslandProfiler.WaterSimulate.Auto())
            {
                SimulateInternal(step);
            }
        }

        private void SimulateInternal(float step)
        {
            int substeps = Mathf.Max(1, settings.simulationSubsteps);
            float substep = step / substeps;
            bool changed = false;

            for (int i = 0; i < _sources.Count; i++)
            {
                ApplySource(_sources[i], step);
            }

            for (int i = 0; i < substeps; i++)
            {
                if (!TryGetActiveCellRect(out int minX, out int maxX, out int minZ, out int maxZ))
                {
                    UpdateWaterfalls(null, 0);
                    break;
                }

                changed |= _flowSolver.Step(substep, minX, maxX, minZ, maxZ);

                if (_flowSolver.OutflowCount > 0 && _outflowConsumer != null)
                {
                    _outflowConsumer.ConsumeOutflows(
                        ConvertOutflowsToWorld(_flowSolver.Outflows, _flowSolver.OutflowCount),
                        _flowSolver.OutflowCount);
                }

                UpdateWaterfalls(_flowSolver.Outflows, _flowSolver.OutflowCount);
            }

            if (changed || _sources.Count > 0)
            {
                MarkWetChunksDirty();
            }
            else
            {
                DeactivateStableChunks();
            }
        }

        private void ApplySource(WaterSource source, float deltaTime)
        {
            if (source.VolumePerSecond <= 0f || source.Radius <= 0f)
            {
                return;
            }

            Bounds sourceBounds = new(
                new Vector3(source.WorldXZ.x, _terrain.WorldBounds.center.y, source.WorldXZ.y),
                new Vector3(source.Radius * 2f, _terrain.WorldBounds.size.y, source.Radius * 2f));
            _grid.WorldBoundsToCellRect(sourceBounds, 0, out int minX, out int maxX, out int minZ, out int maxZ);
            float radiusSqr = source.Radius * source.Radius;
            float totalWeight = 0f;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 cell = _grid.CellCenterXZ(x, z);

                    if ((cell - source.WorldXZ).sqrMagnitude <= radiusSqr)
                    {
                        totalWeight += 1f;
                    }
                }
            }

            if (totalWeight <= 0f)
            {
                return;
            }

            float perCell = source.VolumePerSecond * deltaTime / totalWeight;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 cell = _grid.CellCenterXZ(x, z);

                    if ((cell - source.WorldXZ).sqrMagnitude > radiusSqr)
                    {
                        continue;
                    }

                    int index = _grid.Index(x, z);

                    if (!_grid.HasGround(index))
                    {
                        continue;
                    }

                    float currentSurface = _grid.GroundHeights[index] + _grid.WaterDepths[index];

                    if (currentSurface >= source.MaximumSurfaceHeight)
                    {
                        continue;
                    }

                    _grid.WaterDepths[index] += Mathf.Min(perCell, source.MaximumSurfaceHeight - currentSurface);
                }
            }

            MarkBoundsDirty(sourceBounds);
        }

        private void CreateChunks()
        {
            Material material = waterMaterial != null ? waterMaterial : GetDefaultWaterMaterial();
            Vector3 worldMin = _grid.WorldBounds.min;

            for (int z = 0; z < _grid.ChunksPerAxis; z++)
            {
                for (int x = 0; x < _grid.ChunksPerAxis; x++)
                {
                    Bounds bounds = new(
                        new Vector3(
                            worldMin.x + (x + 0.5f) * _grid.ChunkWorldSize,
                            _grid.WorldBounds.center.y,
                            worldMin.z + (z + 0.5f) * _grid.ChunkWorldSize),
                        new Vector3(_grid.ChunkWorldSize, _grid.WorldBounds.size.y, _grid.ChunkWorldSize));
                    WaterChunk chunk = new(new Vector2Int(x, z), bounds);
                    GameObject chunkObject = new(WaterChunkPrefix + x + " " + z);
                    chunkObject.transform.SetParent(transform, false);
                    chunkObject.transform.localPosition = bounds.min;
                    chunkObject.transform.localRotation = Quaternion.identity;
                    chunkObject.transform.localScale = Vector3.one;

                    Mesh mesh = new();
                    MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
                    MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
                    meshFilter.sharedMesh = mesh;
                    meshRenderer.sharedMaterial = material;
                    ApplyWaterRenderProperties(meshRenderer);

                    chunk.Mesh = mesh;
                    chunk.MeshFilter = meshFilter;
                    chunk.MeshRenderer = meshRenderer;
                    _chunks[x + z * _grid.ChunksPerAxis] = chunk;
                }
            }
        }

        private void CreateWaterfallRenderer()
        {
            Material material = waterMaterial != null ? waterMaterial : GetDefaultWaterMaterial();
            _waterfallObject = new GameObject(WaterfallObjectName);
            _waterfallObject.transform.SetParent(transform, false);
            _waterfallObject.transform.localPosition = Vector3.zero;
            _waterfallObject.transform.localRotation = Quaternion.identity;
            _waterfallObject.transform.localScale = Vector3.one;
            MeshFilter meshFilter = _waterfallObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = _waterfallObject.AddComponent<MeshRenderer>();
            _waterfallMesh = new Mesh { name = WaterfallObjectName };
            meshFilter.sharedMesh = _waterfallMesh;
            meshRenderer.sharedMaterial = material;
            ApplyWaterRenderProperties(meshRenderer);
        }

        private void ApplyWaterRenderProperties(Renderer renderer)
        {
            if (renderer == null || _grid == null)
            {
                return;
            }

            _waterPropertyBlock ??= new MaterialPropertyBlock();
            Vector3 worldGridOrigin = transform.TransformPoint(_grid.WorldBounds.min);
            float worldCellSizeX = transform.TransformVector(new Vector3(_grid.CellSize, 0f, 0f)).magnitude;
            float worldCellSizeZ = transform.TransformVector(new Vector3(0f, 0f, _grid.CellSize)).magnitude;
            float worldCellSize = Mathf.Max(0.0001f, (worldCellSizeX + worldCellSizeZ) * 0.5f);
            renderer.GetPropertyBlock(_waterPropertyBlock);
            _waterPropertyBlock.SetFloat(WaterCellSizeId, worldCellSize);
            _waterPropertyBlock.SetVector(
                WaterGridOriginId,
                new Vector4(worldGridOrigin.x, 0f, worldGridOrigin.z, 0f));
            renderer.SetPropertyBlock(_waterPropertyBlock);
            _waterPropertyBlock.Clear();
        }

        private void UpdateWaterfalls(WaterOutflow[] outflows, int count)
        {
            if (_waterfallMesh == null)
            {
                return;
            }

            _waterfallVertices.Clear();
            _waterfallNormals.Clear();
            _waterfallUvs.Clear();
            _waterfallIndices.Clear();

            if ((outflows == null || count <= 0) && _waterfallSources.Count == 0)
            {
                _waterfallMesh.Clear();
                return;
            }

            int maxQuads = Mathf.Max(0, settings.maxWaterfallQuads);
            float minimumOutflow = Mathf.Max(0f, settings.waterfallMinimumOutflow);
            float dropHeight = Mathf.Max(0f, settings.waterfallDropHeight);
            float baseWidth = Mathf.Max(0.001f, settings.waterfallWidth);
            int quadCount = 0;

            for (int i = 0; i < _waterfallSources.Count && quadCount < maxQuads; i++)
            {
                WaterfallSource source = _waterfallSources[i];

                if (source.Direction.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                AddWaterfallQuad(source.Position, source.Direction, Mathf.Max(baseWidth, source.Width), dropHeight, 1f);
                quadCount++;
            }

            for (int i = 0; outflows != null && i < count && quadCount < maxQuads; i++)
            {
                WaterOutflow outflow = outflows[i];

                if (outflow.Amount < minimumOutflow || outflow.Direction.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                AddWaterfallQuad(outflow.Position, outflow.Direction, baseWidth, dropHeight, outflow.Amount);
                quadCount++;
            }

            _waterfallMesh.Clear();

            if (_waterfallIndices.Count == 0)
            {
                return;
            }

            _waterfallMesh.indexFormat = _waterfallVertices.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _waterfallMesh.SetVertices(_waterfallVertices);
            _waterfallMesh.SetNormals(_waterfallNormals);
            _waterfallMesh.SetUVs(0, _waterfallUvs);
            _waterfallMesh.SetTriangles(_waterfallIndices, 0);
            _waterfallMesh.RecalculateBounds();
        }

        private void AddWaterfallQuad(Vector3 position, Vector2 direction2D, float baseWidth, float dropHeight, float amount)
        {
            direction2D.Normalize();
            Vector3 direction = new(direction2D.x, 0f, direction2D.y);
            Vector3 tangent = new(-direction.z, 0f, direction.x);
            float width = baseWidth * Mathf.Lerp(0.75f, 1.5f, Mathf.Clamp01(amount));
            Vector3 centerTop = position + direction * (_grid.CellSize * 0.45f);
            Vector3 centerBottom = centerTop + Vector3.down * dropHeight + direction * (_grid.CellSize * 0.35f);
            Vector3 halfWidth = tangent * (width * 0.5f);
            int start = _waterfallVertices.Count;

            _waterfallVertices.Add(centerTop - halfWidth);
            _waterfallVertices.Add(centerTop + halfWidth);
            _waterfallVertices.Add(centerBottom - halfWidth);
            _waterfallVertices.Add(centerBottom + halfWidth);

            for (int i = 0; i < 4; i++)
            {
                _waterfallNormals.Add(direction);
            }

            _waterfallUvs.Add(new Vector2(0f, 0f));
            _waterfallUvs.Add(new Vector2(1f, 0f));
            _waterfallUvs.Add(new Vector2(0f, dropHeight));
            _waterfallUvs.Add(new Vector2(1f, dropHeight));

            _waterfallIndices.Add(start);
            _waterfallIndices.Add(start + 2);
            _waterfallIndices.Add(start + 1);
            _waterfallIndices.Add(start + 1);
            _waterfallIndices.Add(start + 2);
            _waterfallIndices.Add(start + 3);
        }

        private void BuildRiverWaterfallSources(IReadOnlyList<RiverWaterBody> rivers)
        {
            _waterfallSources.Clear();

            if (rivers == null)
            {
                return;
            }

            for (int i = 0; i < rivers.Count; i++)
            {
                RiverWaterBody river = rivers[i];
                Vector2 direction = river.EndWorldXZ - river.StartWorldXZ;

                if (direction.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                Vector3 position = new(river.EndWorldXZ.x, river.EndSurfaceHeight, river.EndWorldXZ.y);
                _waterfallSources.Add(new WaterfallSource(position, direction.normalized, Mathf.Max(_grid.CellSize, river.Width)));
            }
        }

        private void MarkAllChunksDirtyAndActive()
        {
            for (int i = 0; i < _chunks.Length; i++)
            {
                _chunks[i].IsActive = true;
                _chunks[i].IsMeshDirty = true;
                _chunks[i].IsSimulationDirty = true;
            }
        }

        private void MarkWetChunksDirty()
        {
            for (int z = 0; z < _grid.CellsPerAxis; z++)
            {
                for (int x = 0; x < _grid.CellsPerAxis; x++)
                {
                    int index = _grid.Index(x, z);

                    if (_grid.WaterDepths[index] <= settings.minimumWaterDepth)
                    {
                        continue;
                    }

                    int chunkX = _grid.CellToChunk(x);
                    int chunkZ = _grid.CellToChunk(z);
                    MarkChunkAndNeighbours(chunkX, chunkZ);
                }
            }
        }

        private void MarkChunkAndNeighbours(int chunkX, int chunkZ)
        {
            int chunkCount = _grid.ChunksPerAxis;

            for (int z = Mathf.Max(0, chunkZ - 1); z <= Mathf.Min(chunkCount - 1, chunkZ + 1); z++)
            {
                for (int x = Mathf.Max(0, chunkX - 1); x <= Mathf.Min(chunkCount - 1, chunkX + 1); x++)
                {
                    WaterChunk chunk = _chunks[x + z * chunkCount];
                    chunk.IsActive = chunk.IsSimulationAllowed;
                    chunk.IsMeshDirty = true;
                }
            }
        }

        private bool TryGetActiveCellRect(out int minX, out int maxX, out int minZ, out int maxZ)
        {
            minX = _grid.CellsPerAxis - 1;
            maxX = 0;
            minZ = _grid.CellsPerAxis - 1;
            maxZ = 0;
            bool found = false;

            for (int i = 0; i < _chunks.Length; i++)
            {
                WaterChunk chunk = _chunks[i];

                if (!chunk.IsActive || !chunk.IsSimulationAllowed)
                {
                    continue;
                }

                if (Time.frameCount - chunk.LastSimulationFrame < chunk.SimulationIntervalFrames)
                {
                    continue;
                }

                int chunkMinX = chunk.Coordinate.x * _grid.CellsPerChunkAxis;
                int chunkMinZ = chunk.Coordinate.y * _grid.CellsPerChunkAxis;
                int chunkMaxX = chunkMinX + _grid.CellsPerChunkAxis - 1;
                int chunkMaxZ = chunkMinZ + _grid.CellsPerChunkAxis - 1;
                minX = Mathf.Min(minX, chunkMinX);
                maxX = Mathf.Max(maxX, chunkMaxX);
                minZ = Mathf.Min(minZ, chunkMinZ);
                maxZ = Mathf.Max(maxZ, chunkMaxZ);
                chunk.LastSimulationFrame = Time.frameCount;
                found = true;
            }

            if (!found)
            {
                return false;
            }

            minX = Mathf.Max(0, minX - 1);
            maxX = Mathf.Min(_grid.CellsPerAxis - 1, maxX + 1);
            minZ = Mathf.Max(0, minZ - 1);
            maxZ = Mathf.Min(_grid.CellsPerAxis - 1, maxZ + 1);
            return true;
        }

        private void DeactivateStableChunks()
        {
            for (int i = 0; i < _chunks.Length; i++)
            {
                _chunks[i].IsActive = false;
                _chunks[i].IsSimulationDirty = false;
            }
        }

        private void MarkBoundsDirty(Bounds bounds)
        {
            if (_grid == null || _chunks == null)
            {
                return;
            }

            _grid.WorldBoundsToCellRect(bounds, 1, out int minX, out int maxX, out int minZ, out int maxZ);
            int minChunkX = _grid.CellToChunk(minX);
            int maxChunkX = _grid.CellToChunk(maxX);
            int minChunkZ = _grid.CellToChunk(minZ);
            int maxChunkZ = _grid.CellToChunk(maxZ);

            for (int z = Mathf.Max(0, minChunkZ - 1); z <= Mathf.Min(_grid.ChunksPerAxis - 1, maxChunkZ + 1); z++)
            {
                for (int x = Mathf.Max(0, minChunkX - 1); x <= Mathf.Min(_grid.ChunksPerAxis - 1, maxChunkX + 1); x++)
                {
                    WaterChunk chunk = _chunks[x + z * _grid.ChunksPerAxis];
                    chunk.IsActive = chunk.IsSimulationAllowed;
                    chunk.IsMeshDirty = true;
                    chunk.IsSimulationDirty = true;
                }
            }
        }

        private void RebuildDirtyMeshes()
        {
            if (_meshBuilder == null || _chunks == null)
            {
                return;
            }

            for (int i = 0; i < _chunks.Length; i++)
            {
                WaterChunk chunk = _chunks[i];

                if (!chunk.IsMeshDirty || chunk.Mesh == null || !chunk.IsRenderAllowed)
                {
                    continue;
                }

                if (Time.frameCount - chunk.LastMeshFrame < chunk.MeshUpdateIntervalFrames)
                {
                    continue;
                }

                using (IslandProfiler.WaterMeshBuild.Auto())
                {
                    _meshBuilder.Build(chunk, chunk.Mesh);
                }

                chunk.IsMeshDirty = false;
                chunk.LastMeshFrame = Time.frameCount;
            }
        }

        private WaterOutflow[] ConvertOutflowsToWorld(WaterOutflow[] localOutflows, int count)
        {
            if (_worldOutflows == null || _worldOutflows.Length < count)
            {
                _worldOutflows = new WaterOutflow[count];
            }

            for (int i = 0; i < count; i++)
            {
                WaterOutflow local = localOutflows[i];
                _worldOutflows[i] = new WaterOutflow
                {
                    Position = transform.TransformPoint(local.Position),
                    Direction = local.Direction,
                    Amount = local.Amount
                };
            }

            return _worldOutflows;
        }

        private Material GetDefaultWaterMaterial()
        {
            if (_defaultMaterial != null)
            {
                return _defaultMaterial;
            }

            Shader shader = Shader.Find("Custom/Stylized Water");

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            _defaultMaterial = new Material(shader);
            _defaultMaterial.name = "Default Water Material";
            ApplyDefaultWaterMaterialProperties(_defaultMaterial);
            return _defaultMaterial;
        }

        private static void ApplyDefaultWaterMaterialProperties(Material material)
        {
            if (material == null || !material.HasProperty("_ShallowColor"))
            {
                return;
            }

            material.SetColor("_ShallowColor", new Color(0.12f, 0.58f, 0.66f, 1f));
            material.SetColor("_DeepColor", new Color(0.02f, 0.16f, 0.31f, 1f));
            material.SetFloat("_Transparency", 0.86f);
            material.SetFloat("_DepthFadeDistance", 2.8f);
            material.SetFloat("_DepthBlendSmoothness", 0.65f);
            material.SetFloat("_DepthBlendPower", 1.2f);
            material.SetFloat("_PixelNoiseResolution", 32f);
            material.SetFloat("_PixelNoiseStrength", 0.16f);
            material.SetFloat("_WaterCellSize", 0.25f);
            material.SetVector("_WaterGridOrigin", Vector4.zero);
            material.SetFloat("_WaterfallScrollSpeed", 1.4f);
            material.SetFloat("_WaterfallAlpha", 0.82f);
            material.SetFloat("_RefractionStrength", 0.12f);
            material.SetFloat("_RefractionScale", 0.55f);
            material.SetFloat("_RefractionSpeed", 0.08f);
        }

        private void ClearWaterObjects()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);

                if (!child.name.StartsWith(WaterChunkPrefix)
                    && child.name != WaterfallObjectName)
                {
                    continue;
                }

                if (child.TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
                {
                    DestroyGeneratedObject(meshFilter.sharedMesh);
                }

                DestroyGeneratedObject(child.gameObject);
            }
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
}
