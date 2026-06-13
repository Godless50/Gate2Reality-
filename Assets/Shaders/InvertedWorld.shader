Shader "Gate2Reality/InvertedWorld"
{
    Properties
    {
        _MainTex     ("Albedo", 2D)   = "white" {}
        _ColorGrade  ("Cold Grade Tint", Color) = (0.7, 0.85, 1.0, 1.0)
        _HorrorScale ("Horror Scale", Range(0,1)) = 1.0
    }

    SubShader
    {
        // Render inside portal aperture only (stencil == 1)
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+20" }

        Stencil
        {
            Ref 1
            Comp Equal
        }

        // ZTest Always — bypass AR occlusion so interior shows through real geometry
        ZTest Always
        ZWrite On
        Cull Front  // inverted normals

        Pass
        {
            Name "InvertedWorld"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _ColorGrade;
                half   _HorrorScale;
            CBUFFER_END

            struct Attributes { float4 posOS : POSITION; float2 uv : TEXCOORD0; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 posCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                // Flip mesh along -Z to create mirror interior
                IN.posOS.z = -IN.posOS.z;
                OUT.posCS  = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv     = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                col.rgb   = col.rgb * _ColorGrade.rgb;
                return col;
            }
            ENDHLSL
        }
    }
}
