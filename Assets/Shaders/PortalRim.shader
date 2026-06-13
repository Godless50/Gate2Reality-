Shader "Gate2Reality/PortalRim"
{
    Properties
    {
        _Aperture    ("Aperture Diameter", Range(0, 4)) = 0.0
        _RimWidth    ("Rim Width",         Range(0.01, 0.3)) = 0.05
        [HDR] _GlowColor ("Glow Color",   Color) = (0.3, 0.7, 1.0, 1.0)
        _PulseSpeed  ("Pulse Speed",       Float) = 1.5
    }

    SubShader
    {
        // Renders after PortalWindow (Queue Geometry+30)
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+30" }
        Cull Off
        ZWrite Off
        Blend One One  // additive for glow

        Pass
        {
            Name "PortalRim"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Aperture;
                float _RimWidth;
                half4 _GlowColor;
                float _PulseSpeed;
            CBUFFER_END

            struct Attributes { float4 posOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 posCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv    = IN.uv - 0.5;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float dist   = length(IN.uv);
                float radius = _Aperture * 0.5;
                float pulse  = 0.75 + 0.25 * sin(_Time.y * _PulseSpeed * 6.2832);
                float rim    = smoothstep(_RimWidth, 0, abs(dist - radius));
                return half4(_GlowColor.rgb * rim * pulse, 0);
            }
            ENDHLSL
        }
    }
}
