// Contorno de desenho animado em tela cheia: acha as bordas pela profundidade e pelas normais da cena e escurece a cor
// ali (mantendo o tom, como traço de nanquim colorido). Some com a distância, para o fundo não virar rabisco.
// Ligado pelo Full Screen Pass Renderer Feature do URP (Editor/RenderLook.cs), antes do pós-processamento.
Shader "Jogo/Contorno"
{
    Properties
    {
        _Strength ("Força (0 = sem contorno)", Range(0, 1)) = 0.65
        _Thickness ("Espessura em pixels (a 1080p)", Range(0.5, 3)) = 1.2
        _DepthThreshold ("Limite da profundidade", Range(0.01, 1)) = 0.08
        _NormalThreshold ("Limite das normais", Range(0.05, 1)) = 0.45
        _MaxDistance ("Distância máxima", Float) = 70
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "Contorno"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _Strength;
            float _Thickness;
            float _DepthThreshold;
            float _NormalThreshold;
            float _MaxDistance;

            float Depth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float depth = Depth(uv);
                if (depth > _MaxDistance || _Strength <= 0) return color;

                // Mesma espessura em qualquer resolução (medida a 1080p).
                float2 step = _Thickness * (_ScreenParams.y / 1080.0) / _ScreenParams.xy;
                float2 offsets[4] = { float2(step.x, 0), float2(-step.x, 0), float2(0, step.y), float2(0, -step.y) };

                float3 normal = SampleSceneNormals(uv);
                float depthEdge = 0;
                float normalEdge = 0;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float2 o = uv + offsets[i];
                    // Relativo à distância: o mesmo degrau perto e longe.
                    depthEdge = max(depthEdge, (Depth(o) - depth) / depth);
                    normalEdge = max(normalEdge, 1 - dot(normal, SampleSceneNormals(o)));
                }
                float edge = saturate(max(smoothstep(_DepthThreshold, _DepthThreshold * 1.6, depthEdge),
                                          smoothstep(_NormalThreshold, _NormalThreshold * 1.4, normalEdge)));
                float fade = 1 - saturate((depth - _MaxDistance * 0.55) / (_MaxDistance * 0.45));
                color.rgb *= 1 - edge * fade * _Strength;
                return color;
            }
            ENDHLSL
        }
    }
}
