// Marca no chão das skills em área, desenhada em código no quadrado (sem textura):
// 0 = círculo rúnico girando, 1 = rachaduras com brilho, 2 = queimado com brasas, 3 = geada cristalina.
// _Life vai de 0 (acabou de surgir) a 1 (sumiu): aparece rápido e esmaece devagar. Cor HDR: o bloom faz o brilho.
Shader "Jogo/MarcaChao"
{
    Properties
    {
        [HDR] _Color ("Cor", Color) = (1, 1, 1, 1)
        _Pattern ("Desenho (0 runa, 1 racha, 2 queimado, 3 geada)", Float) = 0
        _Life ("Vida (0 a 1)", Range(0, 1)) = 0
        _Seed ("Semente", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -2, -2

        Pass
        {
            Name "Marca"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Pattern;
                float _Life;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * 2.0 - 1.0;
                return output;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p + _Seed) * 43758.5453);
            }

            // Distância até a borda da célula de Voronoi mais próxima (linhas finas = rachaduras ou cristais).
            float VoronoiEdge(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float first = 8.0;
                float second = 8.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 o = float2(x, y);
                    float2 r = o + Hash2(cell + o) - f;
                    float d = dot(r, r);
                    if (d < first) { second = first; first = d; }
                    else if (d < second) second = d;
                }
                return sqrt(second) - sqrt(first);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv;
                float r = length(p);
                if (r > 1.0) discard;
                float angle = atan2(p.y, p.x);
                float appear = saturate(_Life / 0.12);
                float fade = 1.0 - saturate((_Life - 0.35) / 0.65);
                float3 color = _Color.rgb;
                float alpha = 0;

                if (_Pattern < 0.5)
                {
                    // Runa: dois anéis, doze marcas entre eles e uma estrela de seis pontas no meio, girando devagar.
                    float spin = angle + _Time.y * 0.6;
                    float rings = smoothstep(0.035, 0.0, abs(r - 0.9 * appear)) + smoothstep(0.025, 0.0, abs(r - 0.66 * appear));
                    float ticks = step(0.68, r) * step(r, 0.88) * smoothstep(0.08, 0.0, abs(frac(spin * 12.0 / 6.2831853) - 0.5) - 0.42);
                    float star = 0;
                    [unroll]
                    for (int k = 0; k < 3; k++)
                    {
                        float a = spin * 0.5 + k * 1.0471976;
                        float2 dir = float2(cos(a), sin(a));
                        star += smoothstep(0.03, 0.0, abs(dot(p, float2(-dir.y, dir.x)))) * step(abs(dot(p, dir)), 0.62);
                    }
                    alpha = saturate(rings + ticks * 0.8 + star * 0.7 * step(r, 0.64)) + 0.12 * (1.0 - r);
                }
                else if (_Pattern < 1.5)
                {
                    // Rachaduras: linhas de Voronoi esticadas para fora, com brilho no meio que apaga antes.
                    float2 q = float2(angle * 2.2, r * 5.0);
                    float crack = smoothstep(0.09, 0.0, VoronoiEdge(q)) * step(r, appear);
                    float glow = saturate(1.0 - _Life * 2.5);
                    color = lerp(float3(0.12, 0.08, 0.05), _Color.rgb, glow);
                    alpha = crack * (1.0 - r * 0.6) + 0.25 * (1.0 - r) * step(r, appear);
                }
                else if (_Pattern < 2.5)
                {
                    // Queimado: mancha escura de borda irregular e brasas que apagam.
                    float edge = 0.75 + 0.18 * sin(angle * 7.0 + _Seed) * sin(angle * 3.0 - _Seed * 2.0);
                    float burn = smoothstep(edge * appear, edge * appear - 0.25, r);
                    float embers = step(0.93, frac(sin(dot(floor(p * 18.0), float2(12.9898, 78.233)) + _Seed) * 43758.5453));
                    float hot = saturate(1.0 - _Life * 2.0);
                    color = lerp(float3(0.07, 0.05, 0.04), _Color.rgb, embers * hot);
                    alpha = burn * 0.85;
                }
                else
                {
                    // Geada: cristais claros (Voronoi) com o centro mais branco.
                    float crystal = smoothstep(0.06, 0.0, VoronoiEdge(p * 6.0));
                    float frost = smoothstep(appear, appear * 0.6, r);
                    color = lerp(_Color.rgb, float3(1.6, 1.8, 2.0), crystal * 0.6);
                    alpha = frost * (0.35 + crystal * 0.6);
                }
                return half4(color, saturate(alpha * fade * _Color.a));
            }
            ENDHLSL
        }
    }
}
