Shader "Custom/Terrain Grass"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct GrassBlade
            {
                float3 positionWS;
                float width;
                float height;
                float angle;
                float4 color;
            };

            StructuredBuffer<GrassBlade> _GrassBlades;

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR0;
                float4 shadowCoord : TEXCOORD0;
            };

            Varyings vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                uint cornerIndex = vertexID;
                GrassBlade blade = _GrassBlades[instanceID];

                float2 corners[6] =
                {
                    float2(-0.5, 0.0),
                    float2(-0.5, 1.0),
                    float2(0.5, 1.0),
                    float2(-0.5, 0.0),
                    float2(0.5, 1.0),
                    float2(0.5, 0.0)
                };

                float3 cameraForwardWS = normalize(GetCameraPositionWS() - blade.positionWS);
                cameraForwardWS.y = 0.0;

                if (dot(cameraForwardWS, cameraForwardWS) < 0.0001)
                {
                    cameraForwardWS = float3(sin(blade.angle), 0.0, cos(blade.angle));
                }
                else
                {
                    cameraForwardWS = normalize(cameraForwardWS);
                }

                float3 rightWS = normalize(cross(float3(0.0, 1.0, 0.0), cameraForwardWS));
                float2 corner = corners[cornerIndex];
                float3 positionWS = blade.positionWS
                    + rightWS * corner.x * blade.width
                    + float3(0.0, corner.y * blade.height, 0.0);

                Varyings output;
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.color = blade.color;
                output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = half3(0.0h, 1.0h, 0.0h);
                Light mainLight = GetMainLight(input.shadowCoord);
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 direct = mainLight.color * diffuse * mainLight.shadowAttenuation;
                half3 lighting = ambient + direct;
                return half4(input.color.rgb * lighting, input.color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
