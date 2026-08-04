using System;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public sealed class TerrainGrassRenderer : MonoBehaviour
{
    private const int BladeVertexCount = 6;
    private const int GrassBladeStride = 40;
    private const int TerrainTriangleStride = 76;
    private const int ArgsStride = sizeof(uint) * 4;
    private const string GenerateGrassKernelName = "GenerateGrass";
    private static readonly int TrianglesId = Shader.PropertyToID("_Triangles");
    private static readonly int GrassColorsId = Shader.PropertyToID("_GrassColors");
    private static readonly int GrassBladesId = Shader.PropertyToID("_GrassBlades");
    private static readonly int TriangleCountId = Shader.PropertyToID("_TriangleCount");
    private static readonly int GrassColorCountId = Shader.PropertyToID("_GrassColorCount");
    private static readonly int MaxCandidatesPerTriangleId = Shader.PropertyToID("_MaxCandidatesPerTriangle");
    private static readonly int BoundaryPaddingTexelsId = Shader.PropertyToID("_BoundaryPaddingTexels");
    private static readonly int MinHeightTexelsId = Shader.PropertyToID("_MinHeightTexels");
    private static readonly int MaxHeightTexelsId = Shader.PropertyToID("_MaxHeightTexels");
    private static readonly int TextureScaleId = Shader.PropertyToID("_TextureScale");
    private static readonly int UpTextureSizeId = Shader.PropertyToID("_UpTextureSize");
    private static readonly int SlopeThresholdId = Shader.PropertyToID("_SlopeThreshold");
    private static readonly int BlendWidthId = Shader.PropertyToID("_BlendWidth");
    private static readonly int BoundaryNoiseScaleId = Shader.PropertyToID("_BoundaryNoiseScale");
    private static readonly int BoundaryNoiseStrengthId = Shader.PropertyToID("_BoundaryNoiseStrength");
    private static readonly int WaterTopShoreWidthId = Shader.PropertyToID("_WaterTopShoreWidth");
    private static readonly int TerrainWaterBodyCountId = Shader.PropertyToID("_TerrainWaterBodyCount");
    private static readonly int TerrainWaterBodiesId = Shader.PropertyToID("_TerrainWaterBodies");
    private static readonly int TerrainWaterBodyShapeDataId = Shader.PropertyToID("_TerrainWaterBodyShapeData");
    private static readonly int TerrainWaterWorldToIslandId = Shader.PropertyToID("_TerrainWaterWorldToIsland");
    private static readonly int TerrainWaterIslandCenterId = Shader.PropertyToID("_TerrainWaterIslandCenter");
    private static readonly int TerrainWaterNoiseSeedOffsetsId = Shader.PropertyToID("_TerrainWaterNoiseSeedOffsets");
    private static readonly int GrassDensityId = Shader.PropertyToID("_GrassDensity");
    private static readonly int BladeWidthWSId = Shader.PropertyToID("_BladeWidthWS");
    private static readonly int BladeHeightUnitWSId = Shader.PropertyToID("_BladeHeightUnitWS");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int WindEnabledId = Shader.PropertyToID("_WindEnabled");
    private static readonly int WindDirectionId = Shader.PropertyToID("_WindDirection");
    private static readonly int WindStrengthId = Shader.PropertyToID("_WindStrength");
    private static readonly int WindSpeedId = Shader.PropertyToID("_WindSpeed");
    private static readonly int WindVariationId = Shader.PropertyToID("_WindVariation");

    private ComputeBuffer _triangleBuffer;
    private ComputeBuffer _colorBuffer;
    private ComputeBuffer _bladeBuffer;
    private ComputeBuffer _argsBuffer;
    private MaterialPropertyBlock _propertyBlock;
    private Material _renderMaterial;
    private Bounds _bounds;
    private bool _hasDrawableGrass;
    private bool _windEnabled;
    private Vector2 _windDirection = Vector2.right;
    private float _windStrength;
    private float _windSpeed = 1f;
    private float _windVariation = 1f;

    private void LateUpdate()
    {
        Draw();
    }

    private void OnDisable()
    {
        ReleaseBuffers();
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

    public void Clear()
    {
        _hasDrawableGrass = false;
        _renderMaterial = null;
        ReleaseBuffers();
    }

    public void Rebuild(
        Mesh mesh,
        Transform meshTransform,
        Material terrainMaterial,
        ComputeShader computeShader,
        Material renderMaterial,
        bool enabled,
        float density,
        int boundaryPaddingTexels,
        int minHeightTexels,
        int maxHeightTexels,
        int maxBlades,
        int maxCandidatesPerTriangle,
        int seed,
        Color[] colors,
        bool windEnabled,
        Vector2 windDirection,
        float windStrength,
        float windSpeed,
        float windVariation,
        int waterBodyCount = 0,
        Vector4[] waterBodies = null,
        Vector4[] waterBodyShapeData = null,
        Matrix4x4 waterWorldToIsland = default,
        Vector4 waterIslandCenter = default,
        Vector4 waterNoiseSeedOffsets = default)
    {
        Clear();
        _renderMaterial = renderMaterial;
        _windEnabled = windEnabled;
        _windDirection = windDirection.sqrMagnitude > 0.0001f
            ? windDirection.normalized
            : Vector2.right;
        _windStrength = Mathf.Max(0f, windStrength);
        _windSpeed = Mathf.Max(0f, windSpeed);
        _windVariation = Mathf.Max(0f, windVariation);

        if (!enabled
            || mesh == null
            || computeShader == null
            || renderMaterial == null
            || terrainMaterial == null
            || density <= 0.0f
            || maxBlades <= 0)
        {
            return;
        }

        if (!SystemInfo.supportsComputeShaders || !computeShader.HasKernel(GenerateGrassKernelName))
        {
            Debug.LogWarning(
                $"Grass rendering skipped because compute shader kernel '{GenerateGrassKernelName}' is unavailable.",
                this);
            return;
        }

        int generateGrassKernel = computeShader.FindKernel(GenerateGrassKernelName);
        Texture upTexture = terrainMaterial.GetTexture("_UpTex");

        if (upTexture == null || colors == null || colors.Length == 0)
        {
            return;
        }

        int[] indices = mesh.triangles;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;

        if (indices.Length < 3 || vertices.Length == 0 || normals.Length != vertices.Length)
        {
            return;
        }

        TerrainTriangle[] triangles = BuildTriangles(indices, vertices, normals, meshTransform.localToWorldMatrix);

        if (triangles.Length == 0)
        {
            return;
        }

        _triangleBuffer = new ComputeBuffer(triangles.Length, TerrainTriangleStride, ComputeBufferType.Structured);
        _triangleBuffer.SetData(triangles);

        Vector4[] colorData = new Vector4[colors.Length];

        for (int i = 0; i < colors.Length; i++)
        {
            Color color = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? colors[i].linear
                : colors[i];
            colorData[i] = color;
        }

        _colorBuffer = new ComputeBuffer(colorData.Length, sizeof(float) * 4, ComputeBufferType.Structured);
        _colorBuffer.SetData(colorData);

        _bladeBuffer = new ComputeBuffer(maxBlades, GrassBladeStride, ComputeBufferType.Append);
        _bladeBuffer.SetCounterValue(0);

        _argsBuffer = new ComputeBuffer(1, ArgsStride, ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(new uint[] { BladeVertexCount, 0, 0, 0 });

        float textureScale = Mathf.Max(terrainMaterial.GetFloat("_TextureScale"), 0.0001f);
        float texelWorldSize = 1.0f / (textureScale * Mathf.Max(upTexture.width, 1));
        int safeMinHeight = Mathf.Max(1, minHeightTexels);
        int safeMaxHeight = Mathf.Max(safeMinHeight, maxHeightTexels);

        computeShader.SetBuffer(generateGrassKernel, TrianglesId, _triangleBuffer);
        computeShader.SetBuffer(generateGrassKernel, GrassColorsId, _colorBuffer);
        computeShader.SetBuffer(generateGrassKernel, GrassBladesId, _bladeBuffer);
        computeShader.SetInt(TriangleCountId, triangles.Length);
        computeShader.SetInt(GrassColorCountId, colorData.Length);
        computeShader.SetInt(MaxCandidatesPerTriangleId, Mathf.Max(1, maxCandidatesPerTriangle));
        computeShader.SetInt(BoundaryPaddingTexelsId, Mathf.Max(0, boundaryPaddingTexels));
        computeShader.SetInt(MinHeightTexelsId, safeMinHeight);
        computeShader.SetInt(MaxHeightTexelsId, safeMaxHeight);
        computeShader.SetFloat(TextureScaleId, textureScale);
        computeShader.SetVector(UpTextureSizeId, new Vector4(upTexture.width, upTexture.height, 0.0f, 0.0f));
        computeShader.SetFloat(SlopeThresholdId, terrainMaterial.GetFloat("_SlopeThreshold"));
        computeShader.SetFloat(BlendWidthId, terrainMaterial.GetFloat("_BlendWidth"));
        computeShader.SetFloat(BoundaryNoiseScaleId, terrainMaterial.GetFloat("_BoundaryNoiseScale"));
        computeShader.SetFloat(BoundaryNoiseStrengthId, terrainMaterial.GetFloat("_BoundaryNoiseStrength"));
        computeShader.SetFloat(
            WaterTopShoreWidthId,
            terrainMaterial.HasProperty("_WaterTopShoreWidth")
                ? terrainMaterial.GetFloat("_WaterTopShoreWidth")
                : 0f);
        computeShader.SetInt(TerrainWaterBodyCountId, Mathf.Max(0, waterBodyCount));

        if (waterBodyCount > 0 && waterBodies != null && waterBodyShapeData != null)
        {
            computeShader.SetVectorArray(TerrainWaterBodiesId, waterBodies);
            computeShader.SetVectorArray(TerrainWaterBodyShapeDataId, waterBodyShapeData);
            computeShader.SetMatrix(TerrainWaterWorldToIslandId, waterWorldToIsland);
            computeShader.SetVector(TerrainWaterIslandCenterId, waterIslandCenter);
            computeShader.SetVector(TerrainWaterNoiseSeedOffsetsId, waterNoiseSeedOffsets);
        }

        computeShader.SetFloat(GrassDensityId, Mathf.Clamp01(density));
        computeShader.SetFloat(BladeWidthWSId, texelWorldSize);
        computeShader.SetFloat(BladeHeightUnitWSId, texelWorldSize);
        computeShader.SetInt(SeedId, seed);

        int threadGroups = Mathf.CeilToInt(triangles.Length / 64.0f);

        try
        {
            computeShader.Dispatch(generateGrassKernel, Mathf.Max(1, threadGroups), 1, 1);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"Grass rendering skipped because '{computeShader.name}' failed to dispatch kernel '{GenerateGrassKernelName}': {exception.Message}",
                this);
            Clear();
            return;
        }

        ComputeBuffer.CopyCount(_bladeBuffer, _argsBuffer, sizeof(uint));

        _bounds = mesh.bounds;
        _bounds = TransformBounds(meshTransform.localToWorldMatrix, _bounds);
        _bounds.Expand(safeMaxHeight * texelWorldSize * 2.0f);

        _propertyBlock ??= new MaterialPropertyBlock();
        _propertyBlock.SetBuffer(GrassBladesId, _bladeBuffer);
        ApplyWindProperties();
        _hasDrawableGrass = true;
    }

    public void Draw()
    {
        if (!_hasDrawableGrass || _renderMaterial == null || _bladeBuffer == null || _argsBuffer == null)
        {
            return;
        }

        _propertyBlock ??= new MaterialPropertyBlock();
        _propertyBlock.SetBuffer(GrassBladesId, _bladeBuffer);
        ApplyWindProperties();
        Graphics.DrawProceduralIndirect(
            _renderMaterial,
            _bounds,
            MeshTopology.Triangles,
            _argsBuffer,
            0,
            null,
            _propertyBlock,
            ShadowCastingMode.Off,
            true,
            gameObject.layer);
    }

    private void ApplyWindProperties()
    {
        _propertyBlock.SetFloat(WindEnabledId, _windEnabled ? 1f : 0f);
        _propertyBlock.SetVector(WindDirectionId, new Vector4(_windDirection.x, _windDirection.y, 0f, 0f));
        _propertyBlock.SetFloat(WindStrengthId, _windStrength);
        _propertyBlock.SetFloat(WindSpeedId, _windSpeed);
        _propertyBlock.SetFloat(WindVariationId, _windVariation);
    }

    private static TerrainTriangle[] BuildTriangles(
        int[] indices,
        Vector3[] vertices,
        Vector3[] normals,
        Matrix4x4 localToWorld)
    {
        TerrainTriangle[] triangles = new TerrainTriangle[indices.Length / 3];
        int triangleCount = 0;

        for (int i = 0; i < indices.Length; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];
            Vector3 p0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
            Vector3 p1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
            Vector3 p2 = localToWorld.MultiplyPoint3x4(vertices[i2]);
            float area = Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;

            if (area <= 0.000001f)
            {
                continue;
            }

            triangles[triangleCount++] = new TerrainTriangle
            {
                P0 = p0,
                P1 = p1,
                P2 = p2,
                N0 = localToWorld.MultiplyVector(normals[i0]).normalized,
                N1 = localToWorld.MultiplyVector(normals[i1]).normalized,
                N2 = localToWorld.MultiplyVector(normals[i2]).normalized,
                Area = area
            };
        }

        if (triangleCount == triangles.Length)
        {
            return triangles;
        }

        Array.Resize(ref triangles, triangleCount);
        return triangles;
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
        Vector3 extents = bounds.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0.0f, 0.0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0.0f, extents.y, 0.0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0.0f, 0.0f, extents.z));
        extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);
        return new Bounds(center, extents * 2.0f);
    }

    private void ReleaseBuffers()
    {
        _triangleBuffer?.Release();
        _colorBuffer?.Release();
        _bladeBuffer?.Release();
        _argsBuffer?.Release();
        _triangleBuffer = null;
        _colorBuffer = null;
        _bladeBuffer = null;
        _argsBuffer = null;
    }

    private struct TerrainTriangle
    {
        public Vector3 P0;
        public Vector3 P1;
        public Vector3 P2;
        public Vector3 N0;
        public Vector3 N1;
        public Vector3 N2;
        public float Area;
    }
}
