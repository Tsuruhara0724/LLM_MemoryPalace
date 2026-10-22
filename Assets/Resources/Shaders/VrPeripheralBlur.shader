Shader "Hidden/MemPalace/VrPeripheralBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X(_MemPalacePeripheralBlurTexture);
        float4 _MemPalaceBlurStep;
        float4 _MemPalaceBlurParameters;
        float _MemPalacePeripheralDarkness;

        half4 Gaussian(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float2 step = _MemPalaceBlurStep.xy * _BlitScaleBias.xy;
            half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 0.2270270270;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + step * 1.3846153846) * 0.3162162162;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - step * 1.3846153846) * 0.3162162162;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + step * 3.2307692308) * 0.0702702703;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - step * 3.2307692308) * 0.0702702703;
            return color;
        }

        half4 Composite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float2 eyeUV = (uv - _BlitScaleBias.zw) / max(_BlitScaleBias.xy, float2(0.0001, 0.0001));
            // Use eye-local coordinates, not the combined desktop mirror image.
            float2 centered = (eyeUV - 0.5) * 2.0;
            centered.x *= _MemPalaceBlurParameters.w;
            float blend = smoothstep(_MemPalaceBlurParameters.x, _MemPalaceBlurParameters.y,
                length(centered)) * _MemPalaceBlurParameters.z;
            half4 sharp = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            half4 blurred = SAMPLE_TEXTURE2D_X(_MemPalacePeripheralBlurTexture, sampler_LinearClamp, eyeUV);
            // Fade the blurred periphery to black without covering the sharp center.
            // Reuse the movement blend so stopping clears both effects immediately.
            half3 color = lerp(sharp.rgb, blurred.rgb, blend);
            color *= 1.0 - blend * saturate(_MemPalacePeripheralDarkness);
            return half4(color, sharp.a);
        }
        ENDHLSL

        Pass
        {
            Name "Gaussian"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Gaussian
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "PeripheralComposite"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Composite
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback Off
}
