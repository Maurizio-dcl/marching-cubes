Shader "Custom/Triplanar Terrain"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _UpTex ("Up Texture", 2D) = "white" {}
        _SideTex ("Side Texture", 2D) = "white" {}
        _DownTex ("Down Texture", 2D) = "white" {}
        _UseDownTexture ("Use Down Texture", Range(0, 1)) = 0
        _TextureScale ("Texture Scale", Float) = 1
        _TriplanarSharpness ("Triplanar Sharpness", Range(1, 8)) = 4
        _SlopeThreshold ("Slope Threshold", Range(0, 1)) = 0.55
        _BlendWidth ("Blend Width", Range(0.001, 1)) = 0.15
        _BoundaryNoiseScale ("Boundary Noise Scale", Float) = 2
        _BoundaryNoiseStrength ("Boundary Noise Strength", Range(0, 1)) = 0.12
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
            TEXTURE2D(_SideTex);
            SAMPLER(sampler_SideTex);
            TEXTURE2D(_DownTex);
            SAMPLER(sampler_DownTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _UseDownTexture;
                half _TextureScale;
                half _TriplanarSharpness;
                half _SlopeThreshold;
                half _BlendWidth;
                half _BoundaryNoiseScale;
                half _BoundaryNoiseStrength;
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
    }

    Fallback "Universal Render Pipeline/Lit"
}
