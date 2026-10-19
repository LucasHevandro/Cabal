// Água do rio: cor rasa perto da margem e funda no meio (pela profundidade da cena), ondinhas que correm com a corrente,
// reflexo do céu na borda (fresnel), brilho do sol e espuma onde a água encosta na margem, nas pedras e nos pilares.
// Sem textura de profundidade (qualidade baixa) fica só a cor funda com reflexo e brilho.
Shader "Jogo/Agua"
{
    Properties
    {
        _Shallow ("Cor rasa", Color) = (0.35, 0.72, 0.72, 0.55)
        _Deep ("Cor funda", Color) = (0.08, 0.3, 0.42, 0.88)
        _DepthMax ("Profundidade da cor funda", Float) = 1.4
        _Foam ("Cor da espuma", Color) = (0.95, 1, 1, 1)
        _FoamDistance ("Largura da espuma", Float) = 0.45
        _Flow ("Corrente (xz)", Vector) = (0.35, 0, 0.05, 0)
        _WaveScale ("Tamanho das ondas", Float) = 0.9
        _WaveStrength ("Força das ondas", Range(0, 1)) = 0.35
        _Reflection ("Reflexo", Range(0, 1)) = 0.6
        _Specular ("Brilho do sol", Float) = 1.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "Agua"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow;
                half4 _Deep;
                float _DepthMax;
                half4 _Foam;
                float _FoamDistance;
                float4 _Flow;
                float _WaveScale;
                float _WaveStrength;
                float _Reflection;
                float _Specular;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(0.3183099, 0.3678794) + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * (p.x + p.y));
            }

            float Noise(float2 x)
            {
                float2 i = floor(x);
                float2 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            // Duas camadas de ondinhas correndo em direções um pouco diferentes.
            float Waves(float2 p)
            {
                float t = _Time.y;
                return Noise(p * _WaveScale - _Flow.xz * t) * 0.6 + Noise(p * _WaveScale * 2.3 + float2(0.7, -0.4) * t * 0.6 - _Flow.xz * t * 1.4) * 0.4;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz;
                const float e = 0.15;
                float h = Waves(p);
                float3 normal = normalize(float3((h - Waves(p + float2(e, 0))) * _WaveStrength / e * 0.3, 1.0,
                                                 (h - Waves(p + float2(0, e))) * _WaveStrength / e * 0.3));
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));

                // Quanto de água há entre a superfície e o fundo neste pixel.
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float depth = max(0.0, sceneDepth - input.screenPos.w);
                float deep = saturate(depth / _DepthMax);
                half4 water = lerp(_Shallow, _Deep, deep);

                // Luz: sol e luz ambiente (de noite a água escurece junto com o resto).
                Light sun = GetMainLight();
                half3 ambient = SampleSH(float3(0, 1, 0));
                half3 lit = water.rgb * (ambient + sun.color * saturate(dot(normal, sun.direction)) * 0.6);

                // Reflexo do céu, forte quando se olha de lado.
                float fresnel = pow(1.0 - saturate(dot(normal, view)), 4.0);
                half3 sky = GlossyEnvironmentReflection(reflect(-view, normal), 0.15, 1.0);
                lit = lerp(lit, sky, fresnel * _Reflection);

                // Brilho do sol nas ondinhas.
                float3 halfway = normalize(sun.direction + view);
                lit += sun.color * pow(saturate(dot(normal, halfway)), 180.0) * _Specular;

                // Espuma: faixa recortada pelo ruído onde a água fica rasa, mais uns fiapos soltos que correm no meio.
                float edge = 1.0 - saturate(depth / _FoamDistance);
                float foamNoise = Noise(p * 3.1 - _Flow.xz * _Time.y * 2.0);
                float foam = smoothstep(0.35, 0.55, edge * 1.2 - foamNoise * 0.6 + 0.15) * step(0.0001, edge);
                foam = max(foam, smoothstep(0.82, 0.9, h) * 0.35);
                half3 foamColor = _Foam.rgb * (ambient * 0.9 + sun.color * 0.5);
                lit = lerp(lit, foamColor, foam);

                float alpha = saturate(max(water.a + fresnel * 0.3, foam));
                lit = MixFog(lit, input.fog);
                return half4(lit, alpha);
            }
            ENDHLSL
        }
    }
}
