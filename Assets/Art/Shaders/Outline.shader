// Inverted-hull outline for URP: back faces pushed out from each part's centre (_OutlineCenter, object space, set per
// renderer by EmployeeOutline) by _Width metres, drawn unlit behind the model. Used as an extra material on hover.
Shader "FoodFactory/Outline"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 1, 0.45, 1)
        _Width ("Width (m)", Float) = 0.018
        _OutlineCenter ("Centre (object space)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float4 _OutlineCenter;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 radial = input.positionOS.xyz - _OutlineCenter.xyz;
                float3 direction = dot(radial, radial) > 1e-8 ? radial : input.normalOS;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + TransformObjectToWorldDir(direction) * _Width;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
