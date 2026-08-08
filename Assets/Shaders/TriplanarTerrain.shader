Shader "Custom/Triplanar Terrain"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _UpTex ("Up Texture", 2D) = "white" {}
        _WaterTopTex ("Water Top Texture", 2D) = "white" {}
        _SideTex ("Side Texture", 2D) = "white" {}
        _DownTex ("Down Texture", 2D) = "white" {}
        [HideInInspector] _UseDownTexture ("Use Down Texture", Range(0, 1)) = 0
        _TextureScale ("Texture Scale", Float) = 1
        _TriplanarSharpness ("Triplanar Sharpness", Range(1, 8)) = 4
        _SlopeThreshold ("Slope Threshold", Range(0, 1)) = 0.55
        _BlendWidth ("Blend Width", Range(0.001, 1)) = 0.15
        _BoundaryNoiseScale ("Boundary Noise Scale", Float) = 2
        _BoundaryNoiseStrength ("Boundary Noise Strength", Range(0, 1)) = 0.12
        _WaterTopShoreWidth ("Water Top Shore Width", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "TerrainTopMask.hlsl"

            TEXTURE2D(_UpTex);
            SAMPLER(sampler_UpTex);
            float4 _UpTex_TexelSize;
            TEXTURE2D(_WaterTopTex);
            SAMPLER(sampler_WaterTopTex);
            float4 _WaterTopTex_TexelSize;
            TEXTURE2D(_SideTex);
            SAMPLER(sampler_SideTex);
            TEXTURE2D(_DownTex);
            SAMPLER(sampler_DownTex);

            #define MAX_TERRAIN_WATER_BODIES 16
            #define MAX_TERRAIN_WATER_RIVERS 16

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _UseDownTexture;
                half _TextureScale;
                half _TriplanarSharpness;
                half _SlopeThreshold;
                half _BlendWidth;
                half _BoundaryNoiseScale;
                half _BoundaryNoiseStrength;
                half _WaterTopShoreWidth;
                int _TerrainWaterBodyCount;
                float4 _TerrainWaterBodies[MAX_TERRAIN_WATER_BODIES];
                float4 _TerrainWaterBodyShapeData[MAX_TERRAIN_WATER_BODIES];
                int _TerrainWaterRiverCount;
                float4 _TerrainWaterRivers[MAX_TERRAIN_WATER_RIVERS];
                float4 _TerrainWaterRiverData[MAX_TERRAIN_WATER_RIVERS];
                float4 _TerrainWaterRiverShapeData[MAX_TERRAIN_WATER_RIVERS];
                float4x4 _TerrainWaterWorldToIsland;
                float4 _TerrainWaterIslandCenter;
                float4 _TerrainWaterNoiseSeedOffsets;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
            };

            half BoundaryOffsetAt(float3 positionWS)
            {
                return TerrainBoundaryOffsetAt(positionWS, _BoundaryNoiseScale, _BoundaryNoiseStrength);
            }

            float2 UpTexelCoord(float3 positionWS)
            {
                return TerrainUpTexelCoord(positionWS, _TextureScale, _UpTex_TexelSize.zw);
            }

            float3 UpTexelCenterPositionWS(float3 positionWS)
            {
                return TerrainUpTexelCenterPositionWS(positionWS, _TextureScale, _UpTex_TexelSize.zw);
            }

            half UpBoundaryOffsetAt(float3 positionWS)
            {
                return TerrainUpBoundaryOffsetAt(
                    positionWS,
                    _TextureScale,
                    _UpTex_TexelSize.zw,
                    _BoundaryNoiseScale,
                    _BoundaryNoiseStrength);
            }

            float2 WrapTexelCoord(float2 texelCoord, float2 textureSize)
            {
                return texelCoord - floor(texelCoord / textureSize) * textureSize;
            }

            half UpTexelMask(float3 positionWS, half normalY)
            {
                return TerrainTopMask(
                    positionWS,
                    normalY,
                    _TextureScale,
                    _UpTex_TexelSize.zw,
                    _SlopeThreshold,
                    _BoundaryNoiseScale,
                    _BoundaryNoiseStrength);
            }

            half4 SampleTopTexture(TEXTURE2D_PARAM(textureName, samplerName), float3 positionWS)
            {
                float scale = max(_TextureScale, 0.0001);
                return SAMPLE_TEXTURE2D(textureName, samplerName, positionWS.xz * scale);
            }

            half4 LoadUpTexel(float3 positionWS)
            {
                float2 textureSize = max(_UpTex_TexelSize.zw, 1.0);
                int2 texelCoord = (int2)WrapTexelCoord(UpTexelCoord(positionWS), textureSize);
                return LOAD_TEXTURE2D(_UpTex, texelCoord);
            }

            half4 LoadWaterTopTexel(float3 positionWS)
            {
                float2 textureSize = max(_WaterTopTex_TexelSize.zw, 1.0);
                int2 texelCoord = (int2)WrapTexelCoord(TerrainUpTexelCoord(positionWS, _TextureScale, textureSize), textureSize);
                return LOAD_TEXTURE2D(_WaterTopTex, texelCoord);
            }

            static const int TERRAIN_PERLIN_PERMUTATION[256] =
            {
                151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
                140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
                247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
                57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
                74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
                60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
                65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
                200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
                52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
                207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
                119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
                129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
                218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
                81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
                184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
                222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180
            };

            int TerrainPerlinPermutation(int index)
            {
                return TERRAIN_PERLIN_PERMUTATION[index & 255];
            }

            float TerrainPerlinFade(float value)
            {
                return value * value * value * (value * (value * 6.0 - 15.0) + 10.0);
            }

            float TerrainPerlinGradient(int hash, float x, float y, float z)
            {
                int gradient = hash & 15;
                float u = gradient < 8 ? x : y;
                float v = gradient < 4 ? y : (gradient == 12 || gradient == 14 ? x : z);
                float first = (gradient & 1) == 0 ? u : -u;
                float second = (gradient & 2) == 0 ? v : -v;
                return first + second;
            }

            float TerrainPerlinSampleSigned(float x, float y, float z)
            {
                int floorX = (int)floor(x);
                int floorY = (int)floor(y);
                int floorZ = (int)floor(z);
                int latticeX = floorX & 255;
                int latticeY = floorY & 255;
                int latticeZ = floorZ & 255;

                float localX = x - floorX;
                float localY = y - floorY;
                float localZ = z - floorZ;
                float fadeX = TerrainPerlinFade(localX);
                float fadeY = TerrainPerlinFade(localY);
                float fadeZ = TerrainPerlinFade(localZ);

                int a = TerrainPerlinPermutation(latticeX) + latticeY;
                int aa = TerrainPerlinPermutation(a) + latticeZ;
                int ab = TerrainPerlinPermutation(a + 1) + latticeZ;
                int b = TerrainPerlinPermutation(latticeX + 1) + latticeY;
                int ba = TerrainPerlinPermutation(b) + latticeZ;
                int bb = TerrainPerlinPermutation(b + 1) + latticeZ;

                float bottomFront = lerp(
                    TerrainPerlinGradient(TerrainPerlinPermutation(aa), localX, localY, localZ),
                    TerrainPerlinGradient(TerrainPerlinPermutation(ba), localX - 1.0, localY, localZ),
                    fadeX);
                float bottomBack = lerp(
                    TerrainPerlinGradient(TerrainPerlinPermutation(ab), localX, localY - 1.0, localZ),
                    TerrainPerlinGradient(TerrainPerlinPermutation(bb), localX - 1.0, localY - 1.0, localZ),
                    fadeX);
                float bottom = lerp(bottomFront, bottomBack, fadeY);
                float topFront = lerp(
                    TerrainPerlinGradient(TerrainPerlinPermutation(aa + 1), localX, localY, localZ - 1.0),
                    TerrainPerlinGradient(TerrainPerlinPermutation(ba + 1), localX - 1.0, localY, localZ - 1.0),
                    fadeX);
                float topBack = lerp(
                    TerrainPerlinGradient(TerrainPerlinPermutation(ab + 1), localX, localY - 1.0, localZ - 1.0),
                    TerrainPerlinGradient(TerrainPerlinPermutation(bb + 1), localX - 1.0, localY - 1.0, localZ - 1.0),
                    fadeX);
                float top = lerp(topFront, topBack, fadeY);
                return lerp(bottom, top, fadeZ);
            }

            float PositionYAtUpTexelCenter(float3 positionWS)
            {
                float2 dxPosition = ddx(positionWS.xz);
                float2 dyPosition = ddy(positionWS.xz);
                float dxHeight = ddx(positionWS.y);
                float dyHeight = ddy(positionWS.y);
                float determinant = dxPosition.x * dyPosition.y - dxPosition.y * dyPosition.x;

                if (abs(determinant) < 0.000001)
                {
                    return positionWS.y;
                }

                float2 heightGradient = float2(
                    (dxHeight * dyPosition.y - dyHeight * dxPosition.y) / determinant,
                    (dyHeight * dxPosition.x - dxHeight * dyPosition.x) / determinant);
                float2 texelDelta = UpTexelCenterPositionWS(positionWS).xz - positionWS.xz;
                return positionWS.y + dot(heightGradient, texelDelta);
            }

            half WaterTopMask(float3 positionWS)
            {
                if (_TerrainWaterBodyCount <= 0 && _TerrainWaterRiverCount <= 0)
                {
                    return 0.0h;
                }

                half mask = 0.0h;
                float3 maskPositionWS = UpTexelCenterPositionWS(positionWS);
                maskPositionWS.y = PositionYAtUpTexelCenter(positionWS);
                float2 islandLocalXZ = mul(_TerrainWaterWorldToIsland, float4(maskPositionWS, 1.0)).xz
                    - _TerrainWaterIslandCenter.xy;
                half boundaryOffset = UpBoundaryOffsetAt(positionWS);
                half boundaryWidth = max(_BlendWidth, 0.0001h);
                half distanceBoundaryOffset = boundaryOffset * boundaryWidth;
                int count = min(_TerrainWaterBodyCount, MAX_TERRAIN_WATER_BODIES);

                [loop]
                for (int i = 0; i < count; i++)
                {
                    float4 waterBody = _TerrainWaterBodies[i];
                    float4 shapeData = _TerrainWaterBodyShapeData[i];
                    float radius = max(waterBody.z, 0.0);

                    if (shapeData.x > 0.0 && shapeData.y > 0.0)
                    {
                        float2 noisePosition = (islandLocalXZ + _TerrainWaterNoiseSeedOffsets.xy) * shapeData.x;
                        float shapeNoise = TerrainPerlinSampleSigned(noisePosition.x, 0.0, noisePosition.y);
                        radius *= max(0.001, 1.0 + shapeNoise * shapeData.y);
                    }

                    float footprintSignedDistance = distance(islandLocalXZ, waterBody.xy) - radius;
                    float shoreSignedDistance = maskPositionWS.y - (waterBody.w + max(_WaterTopShoreWidth, 0.0h));
                    half bodyMask = step(footprintSignedDistance + distanceBoundaryOffset, 0.0)
                        * step(shoreSignedDistance + distanceBoundaryOffset, 0.0);
                    mask = max(mask, bodyMask);
                }

                int riverCount = min(_TerrainWaterRiverCount, MAX_TERRAIN_WATER_RIVERS);

                [loop]
                for (int i = 0; i < riverCount; i++)
                {
                    float4 river = _TerrainWaterRivers[i];
                    float4 riverData = _TerrainWaterRiverData[i];
                    float4 riverShape = _TerrainWaterRiverShapeData[i];
                    float2 start = river.xy;
                    float2 end = river.zw;
                    float2 segment = end - start;
                    float lengthSqr = max(dot(segment, segment), 0.000001);
                    float riverT = saturate(dot(islandLocalXZ - start, segment) / lengthSqr);
                    float2 center = lerp(start, end, riverT);

                    if (riverShape.x > 0.0 && riverShape.y > 0.0)
                    {
                        float2 normal = normalize(float2(-segment.y, segment.x));
                        float2 noisePosition = (center + _TerrainWaterNoiseSeedOffsets.zw) * riverShape.x;
                        float meander = TerrainPerlinSampleSigned(noisePosition.x, 0.0, noisePosition.y);
                        center += normal * (meander * riverData.x * riverShape.y);
                    }

                    float footprintSignedDistance = distance(islandLocalXZ, center) - max(riverData.x, 0.001);
                    float surfaceY = lerp(riverData.y, riverData.z, riverT);
                    float shoreSignedDistance = maskPositionWS.y - (surfaceY + max(_WaterTopShoreWidth, 0.0h));
                    half riverMask = step(footprintSignedDistance + distanceBoundaryOffset, 0.0)
                        * step(shoreSignedDistance + distanceBoundaryOffset, 0.0);
                    mask = max(mask, riverMask);
                }

                return mask;
            }

            half NormalYAtUpTexelCenter(float3 positionWS, half normalY)
            {
                float2 dxPosition = ddx(positionWS.xz);
                float2 dyPosition = ddy(positionWS.xz);
                float dxNormalY = ddx(normalY);
                float dyNormalY = ddy(normalY);
                float determinant = dxPosition.x * dyPosition.y - dxPosition.y * dyPosition.x;

                if (abs(determinant) < 0.000001)
                {
                    return normalY;
                }

                float2 normalGradient = float2(
                    (dxNormalY * dyPosition.y - dyNormalY * dxPosition.y) / determinant,
                    (dyNormalY * dxPosition.x - dxNormalY * dyPosition.x) / determinant);
                float2 texelDelta = UpTexelCenterPositionWS(positionWS).xz - positionWS.xz;
                return normalY + dot(normalGradient, texelDelta);
            }

            half4 SampleSideTexture(TEXTURE2D_PARAM(textureName, samplerName), float3 positionWS, half3 normalWS)
            {
                float scale = max(_TextureScale, 0.0001);
                half2 weights = pow(abs(normalWS.xz), _TriplanarSharpness);
                weights /= max(weights.x + weights.y, 0.0001h);

                half4 xProjection = SAMPLE_TEXTURE2D(textureName, samplerName, positionWS.zy * scale);
                half4 zProjection = SAMPLE_TEXTURE2D(textureName, samplerName, positionWS.xy * scale);
                return xProjection * weights.x + zProjection * weights.y;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.shadowCoord = TransformWorldToShadowCoord(positionInputs.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half texelNormalY = NormalYAtUpTexelCenter(input.positionWS, normalWS.y);
                half boundaryOffset = BoundaryOffsetAt(input.positionWS);
                half upBlend = UpTexelMask(input.positionWS, texelNormalY);
                half downBlend = smoothstep(
                    _SlopeThreshold - _BlendWidth,
                    _SlopeThreshold + _BlendWidth,
                    -normalWS.y + boundaryOffset);

                half4 upColor = LoadUpTexel(input.positionWS);
                if (_TerrainWaterBodyCount > 0 || _TerrainWaterRiverCount > 0)
                {
                    half waterTopMask = WaterTopMask(input.positionWS);
                    half4 waterTopColor = LoadWaterTopTexel(input.positionWS);
                    upColor = lerp(upColor, waterTopColor, waterTopMask);
                }
                half4 sideColor = SampleSideTexture(TEXTURE2D_ARGS(_SideTex, sampler_SideTex), input.positionWS, normalWS);
                half4 downColor = SampleTopTexture(TEXTURE2D_ARGS(_DownTex, sampler_DownTex), input.positionWS);
                downColor = lerp(sideColor, downColor, saturate(_UseDownTexture));

                half4 surfaceColor = lerp(sideColor, upColor, upBlend);
                surfaceColor = lerp(surfaceColor, downColor, downBlend);

                Light mainLight = GetMainLight(input.shadowCoord);
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = ambient + mainLight.color * diffuse * mainLight.shadowAttenuation;
                half4 color = surfaceColor * _Color;
                return half4(color.rgb * lighting, color.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"

            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    CustomEditor "TriplanarTerrainShaderGUI"
    Fallback "Universal Render Pipeline/Lit"
}
