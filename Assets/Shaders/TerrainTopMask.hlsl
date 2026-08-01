#ifndef TERRAIN_TOP_MASK_INCLUDED
#define TERRAIN_TOP_MASK_INCLUDED

float TerrainHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float TerrainValueNoise(float3 p)
{
    float3 cell = floor(p);
    float3 local = frac(p);
    local = local * local * (3.0 - 2.0 * local);

    float c000 = TerrainHash(cell + float3(0, 0, 0));
    float c100 = TerrainHash(cell + float3(1, 0, 0));
    float c010 = TerrainHash(cell + float3(0, 1, 0));
    float c110 = TerrainHash(cell + float3(1, 1, 0));
    float c001 = TerrainHash(cell + float3(0, 0, 1));
    float c101 = TerrainHash(cell + float3(1, 0, 1));
    float c011 = TerrainHash(cell + float3(0, 1, 1));
    float c111 = TerrainHash(cell + float3(1, 1, 1));

    float x00 = lerp(c000, c100, local.x);
    float x10 = lerp(c010, c110, local.x);
    float x01 = lerp(c001, c101, local.x);
    float x11 = lerp(c011, c111, local.x);
    float y0 = lerp(x00, x10, local.y);
    float y1 = lerp(x01, x11, local.y);
    return lerp(y0, y1, local.z);
}

float TerrainBoundaryOffsetAt(
    float3 positionWS,
    float boundaryNoiseScale,
    float boundaryNoiseStrength)
{
    return (TerrainValueNoise(positionWS * boundaryNoiseScale) * 2.0 - 1.0)
        * boundaryNoiseStrength;
}

float2 TerrainUpTexelCoord(float3 positionWS, float textureScale, float2 upTextureSize)
{
    float scale = max(textureScale, 0.0001);
    float2 textureSize = max(upTextureSize, 1.0);
    return floor(positionWS.xz * scale * textureSize);
}

float3 TerrainUpTexelCenterPositionWS(float3 positionWS, float textureScale, float2 upTextureSize)
{
    float2 texel = TerrainUpTexelCoord(positionWS, textureScale, upTextureSize);
    float2 textureSize = max(upTextureSize, 1.0);
    float2 texelCenter = (texel + 0.5) / textureSize;
    float scale = max(textureScale, 0.0001);
    return float3(texelCenter.x / scale, positionWS.y, texelCenter.y / scale);
}

float TerrainUpBoundaryOffsetAt(
    float3 positionWS,
    float textureScale,
    float2 upTextureSize,
    float boundaryNoiseScale,
    float boundaryNoiseStrength)
{
    float3 snappedPositionWS = TerrainUpTexelCenterPositionWS(positionWS, textureScale, upTextureSize);
    snappedPositionWS.y = 0.0;
    return TerrainBoundaryOffsetAt(snappedPositionWS, boundaryNoiseScale, boundaryNoiseStrength);
}

float TerrainTopMask(
    float3 positionWS,
    float normalY,
    float textureScale,
    float2 upTextureSize,
    float slopeThreshold,
    float boundaryNoiseScale,
    float boundaryNoiseStrength)
{
    return step(
        slopeThreshold,
        normalY + TerrainUpBoundaryOffsetAt(
            positionWS,
            textureScale,
            upTextureSize,
            boundaryNoiseScale,
            boundaryNoiseStrength));
}

#endif
