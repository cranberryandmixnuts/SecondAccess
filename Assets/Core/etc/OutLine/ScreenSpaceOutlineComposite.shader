Shader "Hidden/Custom/ScreenSpaceOutlineComposite"
{
    Properties
    {
        [NoScaleOffset]_OutlineMaskTex("Outline Mask", 2D) = "black" {}
        _OutlineColor("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidthPixels("Outline Width Pixels", Range(0, 30)) = 10
        _AlphaThreshold("Alpha Threshold", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ScreenSpaceOutlineComposite"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_OutlineMaskTex);
            SAMPLER(sampler_OutlineMaskTex);

            float4 _OutlineColor;
            float _OutlineWidthPixels;
            float _AlphaThreshold;
            float4 _OutlineMaskTex_TexelSize;

            float SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_OutlineMaskTex, sampler_OutlineMaskTex, uv).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float center = step(_AlphaThreshold, SampleMask(uv));
                float outer = 0.0;
                float2 texel = _OutlineMaskTex_TexelSize.xy;
                int radius = (int)clamp(round(_OutlineWidthPixels), 0.0, 30.0);

                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (x == 0 && y == 0)
                            continue;

                        if (x * x + y * y > radius * radius)
                            continue;

                        float2 sampleUv = uv + float2((float)x, (float)y) * texel;
                        float sampleValue = step(_AlphaThreshold, SampleMask(sampleUv));
                        outer = max(outer, sampleValue);
                    }
                }

                float outline = saturate(outer - center);
                sceneColor.rgb = lerp(sceneColor.rgb, _OutlineColor.rgb, outline * _OutlineColor.a);

                return sceneColor;
            }
            ENDHLSL
        }
    }
}