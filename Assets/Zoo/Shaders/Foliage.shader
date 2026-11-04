Shader "Zoo/Foliage"
{
    Properties
    {
        _BaseMap ("Leaf Texture (RGB tone, A mask)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (0.3, 0.8, 0.2, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Sway ("Wind Sway", Float) = 1
        _Variation ("Patch Colour Variation", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _Sway;
            half _Variation;
        CBUFFER_END

        float3 Sway(float3 wp, float w)
        {
            float ph = wp.x * 0.35 + wp.z * 0.27;
            wp.x += sin(_Time.y * 1.3 + ph) * 0.07 * w * _Sway;
            wp.z += cos(_Time.y * 1.1 + ph * 1.3) * 0.06 * w * _Sway;
            wp.y += sin(_Time.y * 1.7 + ph * 0.7) * 0.015 * w * _Sway;
            return wp;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half4 color : TEXCOORD3;
                half fogCoord : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = Sway(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(tex.a - _Cutoff);

                // buyuk olcekli renk yamalari: agaclar birbirinin kopyasi gibi gorunmesin
                float2 q = i.positionWS.xz * 0.11;
                float pv = frac(sin(dot(floor(q), float2(12.9898, 78.233))) * 43758.5453);
                float pv2 = frac(sin(dot(floor(q + 1.0), float2(12.9898, 78.233))) * 43758.5453);
                float2 f = frac(q); f = f * f * (3.0 - 2.0 * f);
                float n = lerp(pv, pv2, (f.x + f.y) * 0.5);
                half3 variation = lerp(half3(1, 1, 1), half3(1.25, 1.05, 0.55), saturate((n - 0.5) * 2.0) * _Variation)
                                * lerp(half3(1, 1, 1), half3(0.7, 0.95, 1.05), saturate((0.5 - n) * 2.0) * _Variation);

                half3 albedo = tex.rgb * _BaseColor.rgb * i.color.rgb * variation;
                float3 N = normalize(i.normalWS);
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light L = GetMainLight(sc);
                half ndl = dot(N, L.direction) * 0.5 + 0.5;
                half sh = lerp(0.45, 1.0, L.shadowAttenuation);
                half3 col = albedo * (L.color * ndl * ndl * sh * 1.25 + SampleSH(N) * 1.15);
                // yapraklardan isik gecmesi
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                col += albedo * L.color * pow(saturate(dot(V, -L.direction)), 3.0) * 0.35 * sh;
                col = MixFog(col, i.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = Sway(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                float3 nW = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - wp);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, nW, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE * cs.w);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE * cs.w);
            #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(v.positionOS.xyz), v.color.a));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
}
