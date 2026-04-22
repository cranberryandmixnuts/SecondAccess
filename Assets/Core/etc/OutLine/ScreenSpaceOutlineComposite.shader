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

            float SampleMaskBinary(float2 uv)
            {
                return step(_AlphaThreshold, SampleMask(uv));
            }

            float FindOuterMask(float2 uv, float2 texel, int radius)
            {
                float outer = 0.0;
                int coreRadius = min(radius, 3);

                for (int y = -3; y <= 3; y++)
                {
                    for (int x = -3; x <= 3; x++)
                    {
                        if (x == 0 && y == 0)
                            continue;

                        if (x * x + y * y > coreRadius * coreRadius)
                            continue;

                        float2 sampleUv = uv + float2((float)x, (float)y) * texel;
                        outer = max(outer, SampleMaskBinary(sampleUv));

                        if (outer >= 1.0)
                            return 1.0;
                    }
                }

                if (radius <= 3)
                    return outer;

                static const int MaxDirectionCount = 32;
                static const int MaxRingCount = 10;
                static const float TwoPi = 6.28318530718;

                int directionCount = clamp(12 + radius / 2, 12, MaxDirectionCount);
                int ringCount = clamp(radius / 2, 4, MaxRingCount);

                float angleStep = TwoPi / (float)directionCount;
                float startDistance = 4.0;
                float endDistance = (float)radius;
                float distanceRange = max(0.0, endDistance - startDistance);
                float ringStep = ringCount > 1 ? distanceRange / (float)(ringCount - 1) : 0.0;

                for (int ringIndex = 0; ringIndex < MaxRingCount; ringIndex++)
                {
                    if (ringIndex >= ringCount)
                        break;

                    float distancePixels = startDistance + ringStep * ringIndex;
                    float angleOffset = (ringIndex & 1) == 0 ? 0.0 : angleStep * 0.5;

                    for (int dirIndex = 0; dirIndex < MaxDirectionCount; dirIndex++)
                    {
                        if (dirIndex >= directionCount)
                            break;

                        float angle = angleStep * dirIndex + angleOffset;
                        float s;
                        float c;
                        sincos(angle, s, c);

                        float2 dir = float2(c, s);
                        float2 sampleUv = uv + dir * texel * distancePixels;

                        outer = max(outer, SampleMaskBinary(sampleUv));

                        if (outer >= 1.0)
                            return 1.0;
                    }
                }

                return outer;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float center = SampleMaskBinary(uv);
                int radius = (int)clamp(round(_OutlineWidthPixels), 0.0, 30.0);

                if (radius <= 0)
                    return sceneColor;

                float2 texel = _OutlineMaskTex_TexelSize.xy;
                float outer = FindOuterMask(uv, texel, radius);
                float outline = saturate(outer - center);

                sceneColor.rgb = lerp(sceneColor.rgb, _OutlineColor.rgb, outline * _OutlineColor.a);
                return sceneColor;
            }
            ENDHLSL
        }
    }
}