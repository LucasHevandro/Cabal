// Energia mágica: brilho forte na borda (fresnel), energia ondulando por dentro (ruído que corre) e dissolver
// (_Dissolve de 0 a 1 some aos pedaços). Aditivo, sem gravar profundidade: cúpulas de explosão, auras e projéteis.
Shader "Jogo/Energia"
{
    Properties
    {
        [HDR] _Color ("Cor", Color) = (1, 1, 1, 1)
        _Intensity ("Intensidade", Float) = 2
        _Rim ("Borda (maior = mais fina)", Range(0.5, 6)) = 2.2
        _NoiseScale ("Tamanho do ruído", Float) = 3
        _Scroll ("Velocidade do ruído", Float) = 1.5
        _Dissolve ("Dissolver", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Energia"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Rim;
                float _NoiseScale;
                float _Scroll;
                float _Dissolve;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                float fresnel = pow(1.0 - saturate(abs(dot(n, v))), _Rim);

                float t = _Time.y * _Scroll;
                float3 p = input.positionOS * _NoiseScale;
                float noise = Noise(p + float3(0, t, t * 0.5)) * 0.65 + Noise(p * 2.3 - float3(t, 0, t * 0.7)) * 0.35;

                // Dissolver: o ruído decide o que some primeiro; a borda do buraco brilha mais.
                float cut = smoothstep(_Dissolve, _Dissolve + 0.12, noise);
                float edge = (1.0 - smoothstep(_Dissolve + 0.02, _Dissolve + 0.14, noise)) * step(0.001, _Dissolve);

                float glow = fresnel * (0.55 + 0.9 * noise) + noise * 0.18 + edge * 1.5;
                return half4(_Color.rgb * _Intensity * glow * cut * _Color.a, 1);
            }
            ENDHLSL
        }
    }
}
