Shader "Gate2Reality/RealityDistortion"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BaseMap ("Base Map", 2D) = "white" {}
        _Opacity ("Opacity", Range(0, 1)) = 1.0
        _DeformAmount ("Deform Amount", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalRenderPipeline"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  fogFactor   : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _Opacity;
                float  _DeformAmount;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Simple sinusoidal UV distortion proportional to _DeformAmount
                float2 distortedUV = IN.uv;
                distortedUV.x += sin(IN.uv.y * 20.0 + _Time.y * 2.0) * _DeformAmount * 0.05;
                distortedUV.y += cos(IN.uv.x * 20.0 + _Time.y * 1.7) * _DeformAmount * 0.05;

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, distortedUV);
                half4 color = texColor * _BaseColor;

                // Apply _Opacity to alpha
                color.a *= _Opacity;

                // Apply fog
                color.rgb = MixFog(color.rgb, IN.fogFactor);

                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
