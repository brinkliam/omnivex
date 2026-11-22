// Omnivex — texture-free soft particle (ichor, sparks, spores, bubbles, debris glow).
Shader "Omnivex/Particle"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        _Softness ("Softness", Range(0.01, 1)) = 0.6
        _Shape ("Shape 0 disc 1 streak 2 puff", Range(0,2)) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        _FogAmount ("Fog", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Softness; half _Shape; half _SrcBlend; half _DstBlend; half _FogAmount;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; half fog : TEXCOORD1; };

            V vert(A v)
            {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 p = i.uv * 2 - 1;
                float r;
                if (_Shape > 1.5)
                {
                    float wob = sin(atan2(p.y, p.x) * 5 + i.color.a * 8) * 0.08;
                    r = length(p) + wob;
                }
                else if (_Shape > 0.5) r = length(float2(p.x * 2.5, p.y));
                else r = length(p);
                half a = saturate((1 - r) / max(0.01, _Softness));
                half4 c = i.color * _Color;
                c.a *= a;
                half3 rgb = c.rgb;
                rgb = lerp(rgb, MixFog(rgb, i.fog), _FogAmount);
                // additive blends want premultiplied colour
                if (_DstBlend < 1.5) rgb *= c.a;
                return half4(rgb, c.a);
            }
            ENDHLSL
        }
    }
}
