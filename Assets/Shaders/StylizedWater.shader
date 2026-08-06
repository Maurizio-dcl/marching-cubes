Shader "Custom/Stylized Water"
{
    Properties
    {
        _ShallowColor ("ShallowColor", Color) = (0.12, 0.58, 0.66, 1)
        _DeepColor ("DeepColor", Color) = (0.02, 0.16, 0.31, 1)
        _Transparency ("Transparency", Range(0, 1)) = 0.86
        _DepthFadeDistance ("DepthFadeDistance", Float) = 2.8
        _DepthBlendSmoothness ("DepthBlendSmoothness", Range(0, 1)) = 0.65
        _DepthBlendPower ("DepthBlendPower", Float) = 1.2
        _PixelNoiseResolution ("Pixel Noise Resolution", Float) = 32
        _PixelNoiseStrength ("Pixel Noise Strength", Range(0, 1)) = 0.16
        _WaterCellSize ("Water Cell Size", Float) = 0.25
        _WaterGridOrigin ("Water Grid Origin", Vector) = (0, 0, 0, 0)
        _WaterfallScrollSpeed ("Waterfall Scroll Speed", Float) = 1.4
        _WaterfallAlpha ("Waterfall Alpha", Range(0, 1)) = 0.82
        _RefractionStrength ("RefractionStrength", Range(0, 1)) = 0.12
        _RefractionScale ("RefractionScale", Float) = 0.55
        _RefractionSpeed ("RefractionSpeed", Float) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D_X_FLOAT(_WaterCameraDepthTexture);
            SAMPLER(sampler_WaterCameraDepthTexture);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half _Transparency;
                half _DepthFadeDistance;
                half _DepthBlendSmoothness;
                half _DepthBlendPower;
                half _PixelNoiseResolution;
                half _PixelNoiseStrength;
                half _WaterCellSize;
                float4 _WaterGridOrigin;
                half _WaterfallScrollSpeed;
                half _WaterfallAlpha;
                half _RefractionStrength;
                half _RefractionScale;
                half _RefractionSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 waterData : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPosition : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float2 waterData : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.screenPosition = ComputeScreenPos(output.positionHCS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.waterData = input.waterData;
                return output;
            }

            float PixelHash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.11369));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            float PixelNoise(float2 position)
            {
                float resolution = max(_PixelNoiseResolution, 1.0);
                float2 cell = floor(position * resolution);
                return PixelHash(cell) * 2.0 - 1.0;
            }

            float2 WaterCellPixelCoord(float2 worldXZ)
            {
                float cellSize = max(_WaterCellSize, 0.0001);
                return (worldXZ - _WaterGridOrigin.xz) / cellSize;
            }

            half3 ApplyPixelNoise(half3 color, float2 position)
            {
                float noise = PixelNoise(position);
                return color * (1.0h + noise * _PixelNoiseStrength);
            }

            float SampleRawDepth(float2 screenUV)
            {
                return SAMPLE_TEXTURE2D_X(
                    _WaterCameraDepthTexture,
                    sampler_WaterCameraDepthTexture,
                    UnityStereoTransformScreenSpaceTex(screenUV)).r;
            }

            float WaterDepthAt(float rawDepth, float surfaceEyeDepth)
            {
                return max(0.0, LinearEyeDepth(rawDepth, _ZBufferParams) - surfaceEyeDepth);
            }

            float2 RefractionOffset(float2 worldXZ)
            {
                float time = _Time.y * _RefractionSpeed;
                float2 p = worldXZ * _RefractionScale;
                float waveA = sin(dot(p, float2(1.73, 0.62)) + time * 6.28318);
                float waveB = sin(dot(p, float2(-0.51, 1.41)) - time * 4.39823);
                return float2(waveA + waveB * 0.45, waveB - waveA * 0.35) * (_RefractionStrength * 0.01);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float normalUp = abs(normalize(input.normalWS).y);

                if (normalUp < 0.5)
                {
                    float2 waterfallNoisePosition = float2(input.uv.x, input.uv.y / max(_WaterCellSize, 0.0001) - _Time.y * _WaterfallScrollSpeed);
                    half3 waterfallColor = ApplyPixelNoise(lerp(_ShallowColor.rgb, _DeepColor.rgb, 0.45h), waterfallNoisePosition);
                    return half4(waterfallColor, _WaterfallAlpha);
                }

                float2 screenUV = input.screenPosition.xy / max(input.screenPosition.w, 0.0001);
                float rawDepth = SampleRawDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceEyeDepth = LinearEyeDepth(input.positionHCS.z, _ZBufferParams);
                float waterDepth = max(0.0, sceneEyeDepth - surfaceEyeDepth);
                float rawDepth01 = saturate(waterDepth / max(_DepthFadeDistance, 0.0001));
                float smoothDepth01 = rawDepth01 * rawDepth01 * (3.0 - 2.0 * rawDepth01);
                float depth01 = lerp(rawDepth01, smoothDepth01, saturate(_DepthBlendSmoothness));
                depth01 = pow(depth01, max(_DepthBlendPower, 0.001));

                float2 refractedUV = screenUV + RefractionOffset(input.positionWS.xz);
                float refractedRawDepth = SampleRawDepth(refractedUV);
                float refractedWaterDepth = WaterDepthAt(refractedRawDepth, surfaceEyeDepth);

                if (refractedWaterDepth <= 0.0001)
                {
                    refractedUV = screenUV;
                }

                half3 sceneColor = SampleSceneColor(refractedUV);
                half3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);
                float2 waterNoiseUV = WaterCellPixelCoord(input.positionWS.xz);
                waterColor = ApplyPixelNoise(waterColor, waterNoiseUV);
                half3 color = lerp(sceneColor, waterColor, saturate(_Transparency));

                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
