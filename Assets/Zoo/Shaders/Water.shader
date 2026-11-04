Shader "Zoo/Water"
{
    Properties
    {
        _Shallow ("Shallow Color", Color) = (0.3, 0.9, 0.85, 1)
        _Deep ("Deep Color", Color) = (0.03, 0.38, 0.82, 1)
        _Scale ("Wave Scale (1/m)", Float) = 0.16
        _Speed ("Wave Speed", Float) = 0.5
        _Strength ("Normal Strength", Float) = 0.7
        _DepthRange ("Depth Range (m)", Float) = 5
        _Foam ("Foam Width (m)", Float) = 0.5
        _Opacity ("Opacity", Range(0, 1)) = 0.88
        _Glint ("Sun Glint", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow;
                half4 _Deep;
                float _Scale;
                float _Speed;
                float _Strength;
                float _DepthRange;
                float _Foam;
                float _Opacity;
                float _Glint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogCoord : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs vp = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = vp.positionCS;
                o.positionWS = vp.positionWS;
                o.fogCoord = ComputeFogFactor(vp.positionCS.z);
                o.eyeDepth = -vp.positionVS.z;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float waveH(float2 p, float t)
            {
                float h = vnoise(p + float2(t * 0.7, t * 0.4)) * 0.6;
                h += vnoise(p * 2.1 - float2(t * 0.5, -t * 0.8)) * 0.3;
                h += vnoise(p * 4.3 + float2(-t * 0.9, t * 0.6)) * 0.15;
                return h;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                // gercek yuzey normali (yatay su ve tank cami arkasindaki dikey yuzler icin)
                float3 Ng = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                Ng = dot(Ng, V) < 0 ? -Ng : Ng;
                bool isTop = abs(Ng.y) > 0.7;
                float2 p = (isTop ? i.positionWS.xz : float2(i.positionWS.x + i.positionWS.z, i.positionWS.y)) * _Scale;
                float t = _Time.y * _Speed;

                // dalga yuksekliginden normal
                float e = 0.06;
                float h0 = waveH(p, t);
                float hx = waveH(p + float2(e, 0), t);
                float hz = waveH(p + float2(0, e), t);
                float2 g = float2((h0 - hx) / e, (h0 - hz) / e) * _Strength;
                float3 n = isTop ? normalize(float3(g.x, 1.0, g.y)) : normalize(Ng + float3(g.x, g.y, g.x) * 0.5);
                float fres = pow(1.0 - saturate(dot(n, V)), 3.0);

                // derinlik: yuzeyin altindaki cisimlere (zemin, kenar) uzaklik
                float2 suv = i.positionCS.xy / _ScaledScreenParams.xy;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float diff = max(0.0, sceneEye - i.eyeDepth);
                float depth01 = saturate(diff / max(0.01, _DepthRange));

                float3 col = lerp(_Shallow.rgb, _Deep.rgb, sqrt(depth01));

                // gokyuzu yansimasi
                float3 R = reflect(-V, n);
                float3 sky = lerp(unity_AmbientEquator.rgb, unity_AmbientSky.rgb, saturate(R.y * 0.5 + 0.5)) * 1.5;
                col = lerp(col, sky, saturate(fres * 0.55 + 0.04));

                // gunes parlamasi
                Light L = GetMainLight();
                float3 H = normalize(L.direction + V);
                float spec = pow(saturate(dot(n, H)), 420.0) * _Glint;
                col += L.color * min(spec, 1.0);

                // sigda isik agi (kostik) cizgileri
                float c1 = vnoise(p * 5.0 + float2(t, -t * 0.7));
                float c2 = vnoise(p * 5.7 - float2(t * 1.2, t * 0.5));
                float caustic = pow(saturate(1.0 - abs(c1 - c2) * 3.2), 6.0);
                col += caustic * 0.28 * (1.0 - depth01) * L.color;

                // kiyi kopugu
                float fw = 1.0 - saturate(diff / max(0.01, _Foam));
                float fn = vnoise(p * 7.0 + float2(t * 0.6, t * 0.3));
                float foam = saturate(fw * fw * 1.5 * (0.45 + fn));
                col = lerp(col, float3(0.95, 1.0, 1.0), foam * 0.85);

                float alpha = saturate(lerp(0.6, 1.0, sqrt(depth01)) * _Opacity + fres * 0.25 + foam);
                col = MixFog(col, i.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
