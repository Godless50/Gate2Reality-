Shader "Gate2Reality/PortalWindow"
{
    Properties
    {
        _Aperture ("Aperture Diameter", Range(0, 4)) = 0.0
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.1)) = 0.02
        [HDR] _RimColor ("Rim Color", Color) = (0.4, 0.8, 1.0, 1.0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        // Stencil: write 1 inside aperture so InvertedWorld can read it
        Stencil
        {
            Ref 1
            Comp Always
            Pass Replace
        }

        Pass
        {
            Name "PortalWindow"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Aperture;
                float _EdgeSoftness;
                half4 _RimColor;
            CBUFFER_END

            struct Attributes { float4 posOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 posCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv    = IN.uv - 0.5; // centre UV at 0
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float dist   = length(IN.uv);
                float radius = _Aperture * 0.5;
                float alpha  = smoothstep(radius, radius - _EdgeSoftness, dist);
                // Inside aperture: fully transparent (portal hole)
                // Outside: rim color fades
                float rimAlpha = smoothstep(radius + _EdgeSoftness * 3, radius, dist) * (1 - alpha);
                half4 col = half4(_RimColor.rgb, rimAlpha * _RimColor.a);
                // Discard inside aperture to let InvertedWorld show through
                if (alpha > 0.5) discard;
                return col;
            }
            ENDHLSL
        }
    }
}
