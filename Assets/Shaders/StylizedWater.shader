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
        _ShorelineColor ("ShorelineColor", Color) = (0.92, 0.98, 0.91, 1)
        _ShorelineDistance ("ShorelineDistance", Float) = 1.25
        _ShorelineIntensity ("ShorelineIntensity", Range(0, 1)) = 0.9
        _ShorelineAnimationStrength ("ShorelineAnimationStrength", Range(0, 1)) = 0.25
        _ShorelineAnimationScale ("ShorelineAnimationScale", Float) = 2.5
        _ShorelineAnimationSpeed ("ShorelineAnimationSpeed", Float) = 0.35
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
            Cull Back

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
                half4 _ShorelineColor;
                half _ShorelineDistance;
                half _ShorelineIntensity;
                half _ShorelineAnimationStrength;
                half _ShorelineAnimationScale;
                half _ShorelineAnimationSpeed;
                half _RefractionStrength;
                half _RefractionScale;
                half _RefractionSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPosition : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.screenPosition = ComputeScreenPos(output.positionHCS);
                return output;
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
                float shorelineDistance = max(_ShorelineDistance, 0.0001);
                float shorelineWave = sin(dot(input.positionWS.xz, float2(0.73, 1.17)) * _ShorelineAnimationScale
                    + _Time.y * _ShorelineAnimationSpeed * 6.28318);
                float shorelinePulse = 0.75 + shorelineWave * 0.25;
                float animatedShorelineDistance = shorelineDistance
                    * lerp(1.0, 0.85 + shorelinePulse * 0.35, saturate(_ShorelineAnimationStrength));
                float shoreline = 1.0 - smoothstep(0.0, animatedShorelineDistance, waterDepth);
                shoreline *= lerp(1.0, shorelinePulse, saturate(_ShorelineAnimationStrength));
                half3 color = lerp(sceneColor, waterColor, saturate(_Transparency));
                color = lerp(color, _ShorelineColor.rgb, saturate(shoreline * _ShorelineIntensity));

                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
