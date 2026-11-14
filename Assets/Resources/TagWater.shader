// Sea and lake water for the Mega Mode world: rolling waves, sky reflections, a bright sun glint, foam on the crests.
Shader "TagGame/Water"
{
    Properties
    {
        _Deep ("Deep colour", Color) = (0.02, 0.13, 0.22, 1)
        _Shallow ("Shallow colour", Color) = (0.07, 0.38, 0.47, 1)
        _Foam ("Foam colour", Color) = (0.9, 0.95, 1, 1)
        _WaveHeight ("Wave height", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep;
                half4 _Shallow;
                half4 _Foam;
                float _WaveHeight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            // sum of travelling waves: height and its slope
            void Waves(float2 p, float t, int count, out float h, out float2 slope)
            {
                h = 0; slope = 0;
                float amp = 0.55, len = 38.0;
                float2 dir = normalize(float2(1.0, 0.35));
                [unroll(8)]
                for (int i = 0; i < count; i++)
                {
                    float k = 6.2831853 / len;
                    float w = sqrt(9.81 * k);
                    float ph = dot(dir, p) * k - w * t + i * 1.7;
                    h += amp * sin(ph);
                    slope += amp * k * cos(ph) * dir;
                    // next wave: smaller, shorter, turned
                    amp *= 0.62; len *= 0.61;
                    float s = sin(1.1 + i * 0.9), c = cos(1.1 + i * 0.9);
                    dir = normalize(float2(dir.x * c - dir.y * s, dir.x * s + dir.y * c));
                }
                h *= _WaveHeight; slope *= _WaveHeight;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float h; float2 sl;
                float fade = saturate(1.0 - distance(ws.xz, _WorldSpaceCameraPos.xz) / 1800.0);
                Waves(ws.xz, _Time.y, 4, h, sl);
                ws.y += h * fade;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float h; float2 sl;
                Waves(i.positionWS.xz, _Time.y, 8, h, sl);
                // fine ripples for sparkle
                float2 q = i.positionWS.xz;
                float t = _Time.y;
                sl += 0.06 * float2(cos(q.x * 1.9 + t * 2.1) + cos((q.x + q.y) * 1.3 - t * 1.7), cos(q.y * 2.3 - t * 1.8) + cos((q.y - q.x) * 1.1 + t * 2.4));
                float dist = distance(i.positionWS, _WorldSpaceCameraPos);
                sl *= lerp(1.0, 0.25, saturate(dist / 1500.0));
                float3 n = normalize(float3(-sl.x, 1.0, -sl.y));
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndv = saturate(dot(n, v));
                float fres = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);
                half3 sky = GlossyEnvironmentReflection(reflect(-v, n), 0.04h, 1.0h);
                float lit = saturate(dot(n, sun.direction)) * 0.6 + 0.4;
                half3 body = lerp(_Deep.rgb, _Shallow.rgb, saturate(h * 0.6 + 0.35)) * (SampleSH(n) * 0.8 + sun.color * lit * 0.45);
                float3 r = reflect(-sun.direction, n);
                float glint = pow(saturate(dot(r, v)), 900.0) * 40.0 + pow(saturate(dot(r, v)), 120.0) * 1.5;
                half3 spec = sun.color * glint * sun.shadowAttenuation;
                float foam = saturate((h - 0.45 * _WaveHeight) * 2.5) * saturate(1.0 - dist / 400.0);
                half3 col = lerp(body, sky, fres) + spec;
                col = lerp(col, _Foam.rgb * (0.6 + 0.4 * lit), foam * 0.6);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
