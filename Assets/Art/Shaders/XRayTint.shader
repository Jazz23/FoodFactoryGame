// See-through tint for URP: the model drawn unlit in _Color over everything (no depth test), so a target behind walls still
// shows where it is. Used as an extra material while the player picks among valid targets (EquipmentInteraction.Employees.cs).
// Each part writes the stencil once, so overlapping parts of one model are not tinted twice.
Shader "FoodFactory/XRayTint"
{
    Properties
    {
        _Color ("Color", Color) = (0.55, 1, 0.6, 0.28)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" }
        Pass
        {
            Name "XRayTint"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Back
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref 8
                ReadMask 8
                WriteMask 8
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
