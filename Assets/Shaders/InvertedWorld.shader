// =============================================================================
// Gate2Reality / InvertedWorld
// Материал всего, что живёт «за стеной»: оболочка инвертированной комнаты
// и её реквизит. Рисуется ТОЛЬКО там, где PortalWindow записал stencil.
//
// КЛЮЧЕВЫЕ РЕШЕНИЯ:
//   Stencil Comp Equal  — геометрия существует лишь в апертуре окна.
//   ZTest Always        — ОБЯЗАТЕЛЬНО: environment depth ARCore уже записал
//                         в буфер глубину ФИЗИЧЕСКОЙ стены, и честный ZTest
//                         отрезал бы весь мир за ней. Мы сознательно
//                         игнорируем глубину и сортируемся очередями:
//                           Geometry+20 — оболочка комнаты (этот материал),
//                           Geometry+21..+25 — реквизит (material.renderQueue).
//                         Осознанный компромисс: рука игрока НЕ перекрывает
//                         содержимое внутри окна (только само окно — его
//                         маска ZTest LEqual). Для окон <= 2м незаметно.
//   ZWrite Off          — глубина мира за стеной никому не нужна и не должна
//                         испортить последующие прозрачные эффекты.
//
// ВИД: холодный градиент «в глубину» (туман к чёрному по дистанции от
// камеры), фреснель-контур по краям геометрии, медленное вертикальное
// мерцание — мир дышит. Ноль текстур, всё процедурно, всё в half.
// =============================================================================
Shader "Gate2Reality/InvertedWorld"
{
    Properties
    {
        _BaseColor ("Near Color", Color) = (0.16, 0.20, 0.34, 1)
        _DeepColor ("Deep Color (даль)", Color) = (0.01, 0.01, 0.03, 1)
        [HDR] _FresnelColor ("Fresnel Edge (HDR)", Color) = (0.4, 0.7, 1.6, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FogDistance ("Fog Distance, m", Float) = 6.0
        _ShimmerSpeed ("Shimmer Speed", Float) = 0.6
        _ShimmerAmount ("Shimmer Amount", Range(0, 0.3)) = 0.08
        [IntRange] _StencilRef ("Stencil Ref", Range(1, 255)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+20"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "InvertedWorld"
            ZTest Always
            ZWrite Off
            Cull Back

            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _FresnelColor;
                half _FresnelPower;
                half _FogDistance;
                half _ShimmerSpeed;
                half _ShimmerAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = (half3)TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 toCam = (half3)(_WorldSpaceCameraPos - i.positionWS);
                half dist = length(toCam);
                half3 viewDir = toCam / max(dist, 0.001h);

                // Туман в глубину зазеркалья: чем дальше — тем чернее.
                half fog = saturate(dist / _FogDistance);
                half3 color = lerp(_BaseColor.rgb, _DeepColor.rgb, fog);

                // Фреснель: холодный контур по краям форм — единственное
                // «освещение» этого мира, реального света тут нет.
                half ndv = saturate(dot(normalize(i.normalWS), viewDir));
                half fresnel = pow(1.0h - ndv, _FresnelPower);
                color += _FresnelColor.rgb * fresnel * (1.0h - fog);

                // Дыхание мира: медленная вертикальная волна яркости.
                half shimmer = sin(i.positionWS.y * 5.0h
                                   + (half)_Time.y * _ShimmerSpeed * 6.2832h);
                color *= 1.0h + shimmer * _ShimmerAmount;

                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
