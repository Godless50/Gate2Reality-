// =============================================================================
// Gate2Reality / PortalWindow
// «Окно в зазеркалье» на физической стене. Quad, два прохода:
//
//   PASS 1 (StencilMask): пишет stencil ref в КРУГЛОЙ апертуре (clip по
//     радиусу), цвет не трогает (ColorMask 0). _Aperture 0..1 анимируется
//     из C# — окно «раскрывается» из точки.
//   PASS 2 (Rim): аддитивный светящийся обод по текущему радиусу апертуры —
//     живой край раны в реальности.
//
// КОНТРАКТ ОЧЕРЕДЕЙ (вся магия порталов — в порядке):
//   Geometry+10  PortalWindow (этот шейдер): записывает stencil
//   Geometry+20  InvertedWorld (оболочка мира за стеной): Comp Equal
//   Geometry+21+ реквизит инвертированного мира (очередь материала)
//
// ОККЛЮЗИЯ: ZTest LEqual + Offset -1,-1 — quad лежит НА стене, офсет
// побеждает z-fight с environment depth стены, но РУКА игрока, попавшая
// между камерой и окном, честно перекрывает и маску, и обод: окно
// «закрывается» ладонью. Дёшево и очень физично.
//
// Бюджет: ноль текстур, чистая арифметика, всё в half.
// =============================================================================
Shader "Gate2Reality/PortalWindow"
{
    Properties
    {
        _Aperture ("Aperture (0=closed, 1=open)", Range(0, 1)) = 0
        [HDR] _RimColor ("Rim Color (HDR)", Color) = (0.7, 0.35, 2.2, 1)
        _RimWidth ("Rim Width", Range(0.005, 0.3)) = 0.07
        _RimPulseHz ("Rim Pulse, Hz", Float) = 1.3
        [IntRange] _StencilRef ("Stencil Ref", Range(1, 255)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ---------------------------------------------------------------------
        // PASS 1: стенсил-маска круглой апертуры
        // ---------------------------------------------------------------------
        Pass
        {
            Name "StencilMask"
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            Stencil
            {
                Ref [_StencilRef]
                Comp Always
                Pass Replace
            }

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
                // r: 0 в центре quad'а, 1 у вписанной окружности.
                half r = length(i.uv - 0.5h) * 2.0h;
                // Вне текущей апертуры — пиксель отбрасывается, stencil
                // НЕ пишется: окно ровно того размера, что задал C#.
                clip(_Aperture - r);
                return 0;
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------------
        // PASS 2 (Rim) ПЕРЕЕХАЛ в отдельный шейдер Gate2Reality/PortalRim
        // (Queue Geometry+30). Причина, пойманная прогоном: обод в очереди
        // +10 закрашивался бы InvertedWorld (+20, ZTest Always) на внутренней
        // половине кромки. Rim вешается ВТОРЫМ материалом на тот же quad.
        // ---------------------------------------------------------------------
    }
    Fallback Off
}
