// =============================================================================
// Gate2Reality / PortalRim
// Светящийся обод портального окна. Отдельный шейдер с очередью Geometry+30:
// рисуется ПОСЛЕ InvertedWorld (+20), поэтому кромка лежит поверх мира,
// а не под ним (баг, пойманный прогоном контракта очередей).
//
// Применение: ВТОРОЙ материал на том же quad'е, что и PortalWindow.
// MaterialPropertyBlock рендерера задаёт _Aperture обоим материалам разом —
// C#-код (PortalWindowEffect) об этом разделении даже не знает.
// =============================================================================
Shader "Gate2Reality/PortalRim"
{
    Properties
    {
        _Aperture ("Aperture (0=closed, 1=open)", Range(0, 1)) = 0
        [HDR] _RimColor ("Rim Color (HDR)", Color) = (0.7, 0.35, 2.2, 1)
        _RimWidth ("Rim Width", Range(0.005, 0.3)) = 0.07
        _RimPulseHz ("Rim Pulse, Hz", Float) = 1.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Geometry+30"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Rim"
            Blend One One        // аддитив: свечение складывается со сценой
            ColorMask RGB
            ZWrite Off
            ZTest LEqual         // рука игрока перекрывает обод вместе с окном
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Aperture;
                half4 _RimColor;
                half _RimWidth;
                half _RimPulseHz;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Закрытое окно не светится вовсе (и не жжёт филлрейт зря).
                clip(_Aperture - 0.001h);

                half r = length(i.uv - 0.5h) * 2.0h;
                // Колоколообразная полоса вокруг радиуса апертуры.
                half band = 1.0h - saturate(abs(r - _Aperture) / _RimWidth);
                clip(band - 0.001h); // вне обода — отбрасываем

                band *= band; // острее к кромке
                half pulse = 0.8h + 0.2h * sin((half)_Time.y * _RimPulseHz * 6.2832h);
                return half4(_RimColor.rgb * (band * pulse), 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
