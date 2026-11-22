// Omnivex — living tissue. Vertex colour = albedo, wrapped light + hemisphere ambient, fresnel rim, procedural
// bioluminescent veins that pulse outward from the core, organic vertex wobble, hit flash and digest dissolve.
Shader "Omnivex/Bio"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [HDR] _GlowColor ("Vein Glow", Color) = (0,0,0,0)
        _VeinAmount ("Vein Amount", Range(0,1)) = 0
        _VeinScale ("Vein Scale", Range(0.5,12)) = 4
        _Pulse ("Pulse Speed", Range(0,8)) = 2
        _Wobble ("Wobble", Range(0,0.3)) = 0.04
        _WobbleSpeed ("Wobble Speed", Range(0,10)) = 3
        [HDR] _RimColor ("Rim", Color) = (0.3,0.9,1,1)
        _Rim ("Rim Strength", Range(0,2)) = 0.6
        _Gloss ("Gloss", Range(0,1)) = 0.35
        _Flash ("Hit Flash", Range(0,1)) = 0
        _Dissolve ("Digest", Range(0,1)) = 0
        [HDR] _EmissionColor ("Flat Emission", Color) = (0,0,0,0)
        [HDR] _WindowGlow ("Vertex-alpha glow (windows)", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; half4 _GlowColor; half _VeinAmount; half _VeinScale; half _Pulse; half _Wobble; half _WobbleSpeed;
            half4 _RimColor; half _Rim; half _Gloss; half _Flash; half _Dissolve; half4 _EmissionColor; half4 _WindowGlow;
        CBUFFER_END
        half4 _OMX_AmbSky; half4 _OMX_AmbGround;

        float3 Wobble(float3 p, float3 n)
        {
            float t = _Time.y * _WobbleSpeed;
            float w = sin(p.x * 5.1 + t) * sin(p.y * 4.3 + t * 1.3) + sin(p.z * 6.7 - t * 0.8) * 0.5;
            return p + n * w * _Wobble;
        }
        float Hash3(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
        ENDHLSL

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

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half4 color : COLOR; float3 posOS : TEXCOORD2; half fog : TEXCOORD3; };

            V vert(A v)
            {
                V o;
                float3 p = Wobble(v.positionOS.xyz, v.normalOS);
                o.posOS = v.positionOS.xyz;
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                if (_Dissolve > 0.001)
                {
                    float h = Hash3(floor(i.posOS * 14));
                    clip(h - _Dissolve);
                }
                half3 n = normalize(i.normalWS);
                half3 albedo = i.color.rgb * _BaseColor.rgb;
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, L.direction);
                half wrap = saturate((ndl + 0.5) / 1.5);
                half sh = lerp(0.45, 1.0, L.shadowAttenuation);
                half3 col = albedo * L.color * wrap * sh;
                half3 amb = lerp(_OMX_AmbGround.rgb, _OMX_AmbSky.rgb, n.y * 0.5 + 0.5);
                col += albedo * amb;

                half3 vd = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 hv = normalize(L.direction + vd);
                col += L.color * pow(saturate(dot(n, hv)), 12 + _Gloss * 90) * _Gloss * sh;

                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                    col += albedo * l.color * l.distanceAttenuation * saturate((dot(n, l.direction) + 0.6) / 1.6);
                LIGHT_LOOP_END
                #endif

                half fres = pow(1 - saturate(dot(n, vd)), 2.5);
                col += _RimColor.rgb * fres * _Rim;

                // veins: two warped sine sheets intersecting, sharpened to thin lines, pulsing outward from the core
                if (_VeinAmount > 0.001)
                {
                    float3 p = i.posOS * _VeinScale;
                    float a = sin(p.x * 2.1 + sin(p.y * 3.3 + p.z * 1.7) * 1.6);
                    float b = sin(p.z * 2.7 + sin(p.x * 2.9 - p.y * 1.3) * 1.4);
                    float lines = pow(saturate(1 - abs(a * b) * 6), 3);
                    float wave = 0.55 + 0.45 * sin(_Time.y * _Pulse - length(i.posOS) * 9);
                    col += _GlowColor.rgb * lines * wave * _VeinAmount;
                    col += _GlowColor.rgb * 0.08 * _VeinAmount * wave;
                }
                col += _EmissionColor.rgb;
                col += i.color.rgb * _WindowGlow.rgb * i.color.a;
                col = lerp(col, half3(3, 3, 3), _Flash);
                if (_Dissolve > 0.001) col += _GlowColor.rgb * 2 * _Dissolve;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vs
            #pragma fragment fs
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };
            V vs(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(Wobble(v.positionOS.xyz, v.normalOS));
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, TransformObjectToWorldNormal(v.normalOS), _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }
            half4 fs(V i) : SV_Target { return 0; }
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
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };
            V vs(A v) { V o; o.positionCS = TransformObjectToHClip(Wobble(v.positionOS.xyz, v.normalOS)); return o; }
            half4 fs(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
