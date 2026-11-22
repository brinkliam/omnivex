// Omnivex — additive unlit glow for telegraphs, shockwave rings, lure halos and auras. UV-space shapes, no textures.
Shader "Omnivex/Glow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        _Shape ("0 soft disc, 1 ring, 2 filled telegraph", Range(0,2)) = 0
        _Thickness ("Ring thickness", Range(0.01,1)) = 0.15
        _Fill ("Telegraph fill 0..1", Range(0,1)) = 0
        _Fresnel ("Mesh fresnel (3D auras)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Shape; half _Thickness; half _Fill; half _Fresnel;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fres : TEXCOORD1; half4 color : COLOR; };
            V vert(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv;
                half3 n = TransformObjectToWorldNormal(v.normalOS);
                half3 vd = normalize(GetWorldSpaceViewDir(ws));
                o.fres = pow(1 - saturate(abs(dot(n, vd))), 2);
                o.color = v.color;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                if (_Fresnel > 0.5) return half4(_Color.rgb * i.fres * i.color.a, 1);
                float r = length(i.uv * 2 - 1);
                half a;
                if (_Shape > 1.5)
                {
                    half edge = saturate(1 - abs(r - 0.94) / 0.06);
                    half fill = r < _Fill ? 0.35 + 0.3 * saturate(1 - (_Fill - r) * 4) : 0.07;
                    a = (r < 1) ? max(edge, fill) : 0;
                }
                else if (_Shape > 0.5) a = saturate(1 - abs(r - (1 - _Thickness)) / _Thickness);
                else { a = saturate(1 - r); a *= a; }
                return half4(_Color.rgb * a * i.color.a * i.color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
