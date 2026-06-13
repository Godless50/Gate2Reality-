Shader "Gate2Reality/RealityDistortion"
{
    Properties
    {
        _MainTex     ("Albedo",        2D)     = "white" {}
        _NoiseTex    ("Noise (RG flow, B crack)", 2D) = "black" {}
        _HorrorScale ("Horror Scale",  Range(0,1)) = 1.0
        _BreachProgress ("Breach Progress", Range(0,1)) = 0.0
        _FlowSpeed   ("Flow Speed",    Float)  = 0.5
        _DistortAmt  ("Distort Amt",   Float)  = 0.04
        _CrackColor  ("Crack Color",   Color)  = (0.8, 0.6, 0.3, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex);   SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _NoiseTex_ST;
                half   _HorrorScale;
                half   _BreachProgress;
                float  _FlowSpeed;
                float  _DistortAmt;
                half4  _CrackColor;
            CBUFFER_END

            struct Attributes { float4 posOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 posCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.posCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv    = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 noiseUV = IN.uv + _Time.y * _FlowSpeed * float2(0.07, 0.05);
                half3  noise   = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).rgb;

                float2 distorted = IN.uv + (noise.rg - 0.5) * _DistortAmt * _HorrorScale;
                half4  col       = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distorted);

                // Crack overlay driven by breach progress
                half crackMask = step(1.0 - _BreachProgress, noise.b);
                col.rgb = lerp(col.rgb, _CrackColor.rgb, crackMask * _HorrorScale);

                return col;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
