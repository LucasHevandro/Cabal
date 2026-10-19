// Céu noturno: estrelas (pontinhos suaves que cintilam) e a lua (disco claro com halo), somados à cor do céu e sem
// neblina. _Alpha acende tudo conforme a noite chega. Cada quadrado traz no UV0 a posição dentro dele (-1 a 1) e no UV1 a
// fase e a velocidade do cintilar; fase negativa marca a lua.
Shader "Jogo/CeuNoturno"
{
    Properties
    {
        _Alpha ("Força (0 de dia, 1 de noite)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "CeuNoturno"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 twinkle : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 twinkle : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.twinkle = input.twinkle;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d = length(input.uv);
                float shape;
                if (input.twinkle.x < 0)
                {
                    // Lua: disco de borda firme, um pouco mais escuro de um lado, e o halo em volta.
                    float disc = 1.0 - smoothstep(0.34, 0.37, d);
                    float shade = 0.82 + 0.18 * saturate(0.5 - input.uv.x * 0.9 + input.uv.y * 0.4);
                    float halo = exp(-d * 5.0) * 0.35;
                    shape = disc * shade + halo * (1.0 - disc);
                }
                else
                {
                    // Estrela: núcleo pequeno e brilho suave, cintilando devagar.
                    float core = saturate(1.0 - d * 2.6);
                    float glow = saturate(1.0 - d) * saturate(1.0 - d);
                    float flicker = 0.62 + 0.38 * sin(_Time.y * input.twinkle.y + input.twinkle.x);
                    shape = (core * core + glow * 0.35) * flicker;
                }
                return half4(input.color.rgb * shape * _Alpha, 1);
            }
            ENDHLSL
        }
    }
}
