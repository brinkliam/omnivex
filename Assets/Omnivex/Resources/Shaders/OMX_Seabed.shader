// Omnivex — the tide-pool floor. Vertex colour = albedo (rgb) + zone glow (a). Animated double-layer caustics,
// shadows, and a soft glow where bioluminescent zones sit.
Shader "Omnivex/Seabed"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [HDR] _CausticColor ("Caustics", Color) = (0.5,1.2,1.3,1)
        _CausticScale ("Caustic Scale", Range(0.02,1)) = 0.22
        [HDR] _ZoneGlow ("Zone Glow", Color) = (0.2,1,0.9,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; half4 _CausticColor; half _CausticScale; half4 _ZoneGlow;
            CBUFFER_END
            half4 _OMX_AmbSky; half4 _OMX_AmbGround;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half4 color : COLOR; half fog : TEXCOORD2; };

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Caustic(float2 p, float t)
            {
                float2 q = p;
                float c = 0;
                for (int k = 0; k < 3; k++)
                {
                    q += float2(sin(q.y * 1.7 + t * 0.9), cos(q.x * 1.3 - t * 0.7)) * 0.55;
                    c += abs(sin(q.x + q.y * 0.6));
                }
                return pow(saturate(1.6 - c * 0.55), 4);
            }

            half4 frag(V i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 albedo = i.color.rgb * _BaseColor.rgb;
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half wrap = saturate((dot(n, L.direction) + 0.35) / 1.35);
                half sh = lerp(0.4, 1.0, L.shadowAttenuation);
                half3 col = albedo * L.color * wrap * sh + albedo * lerp(_OMX_AmbGround.rgb, _OMX_AmbSky.rgb, n.y * 0.5 + 0.5);

                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                    col += albedo * l.color * l.distanceAttenuation * saturate(dot(n, l.direction) * 0.7 + 0.3);
                LIGHT_LOOP_END
                #endif

                float t = _Time.y;
                float2 p = i.positionWS.xz * _CausticScale;
                float c = Caustic(p, t) * 0.65 + Caustic(p * 1.7 + 3.1, t * 1.3) * 0.45;
                col += _CausticColor.rgb * c * sh * saturate(n.y);
                half zone = i.color.a;
                col += _ZoneGlow.rgb * zone * (0.55 + 0.45 * sin(t * 1.5 + i.positionWS.x * 0.3 + i.positionWS.z * 0.2));
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vs
            #pragma fragment fs
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };
            V vs(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 fs(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
