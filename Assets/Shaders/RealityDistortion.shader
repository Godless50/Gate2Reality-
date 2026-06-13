// =============================================================================
// Gate2Reality / RealityDistortion
// Один шейдер на оба эффекта Сцены 1:
//   - "вибрация реальности" на ножках стула  -> _DistortionStrength (0..1)
//   - сине-светящаяся трещина на чашке       -> _CrackProgress (0..1)
// Управляется из C# через MaterialPropertyBlock (Step 3), поэтому один
// материал обслуживает оба оверлея без дублирования.
//
// ПРИНЦИП: оверлей-меш (quad/низкополигональная оболочка) рисуется поверх
// физического объекта и "переснимает" сцену позади себя из
// _CameraOpaqueTexture со смещёнными UV — классический heat-haze, но без
// GrabPass (его в URP нет и он был бы смертелен для tiler-GPU).
//
// ТРЕБОВАНИЕ ПАЙПЛАЙНА (войдёт в чек-лист Step 5):
//   URP Asset -> Opaque Texture = ON (Downsampling: 2x Bilinear достаточно).
//
// МОБИЛЬНЫЙ БЮДЖЕТ (Pixel 9 / S26, Vulkan):
//   - 2 выборки текстур на пиксель (1x noise, 1x scene color) + арифметика;
//   - всё в half: на Adreno/Mali это буквально вдвое дешевле float;
//   - ZWrite Off, прозрачная очередь, оверлеи маленькие -> overdraw ничтожен.
// =============================================================================
Shader "Gate2Reality/RealityDistortion"
{
    Properties
    {
        [NoScaleOffset] _NoiseTex ("Noise (RG: flow-вектора, B: трещины)", 2D) = "gray" {}

        [Header(Distortion ... chair legs)]
        _DistortionStrength ("Distortion Strength", Range(0, 1)) = 0
        _DistortionScale    ("Noise UV Scale",      Float)       = 3
        _DistortionSpeed    ("Scroll Speed",        Float)       = 1.5
        _DistortionAmount   ("Max UV Offset",       Range(0, 0.1)) = 0.03

        [Header(Crack ... cup breach)]
        _CrackProgress ("Crack Progress", Range(0, 1)) = 0
        [HDR] _CrackColor ("Crack Color (HDR, синий)", Color) = (0.25, 0.7, 2.5, 1)
        _CrackScale ("Crack UV Scale", Float) = 6
        _CrackWidth ("Crack Line Width", Range(0.001, 0.2)) = 0.045
        _CrackPulseHz ("Crack Pulse, Hz", Float) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "RealityDistortion"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            // Вырезаем варианты, которые нам не нужны, — меньше компиляций,
            // быстрее загрузка на устройстве.
            #pragma skip_variants FOG_EXP FOG_EXP2 FOG_LINEAR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            // CBUFFER -> совместимость с SRP Batcher (критично: оба оверлея
            // и любые будущие инстансы батчатся в один проход настройки).
            CBUFFER_START(UnityPerMaterial)
                half _DistortionStrength;
                half _DistortionScale;
                half _DistortionSpeed;
                half _DistortionAmount;
                half _CrackProgress;
                half4 _CrackColor;
                half _CrackScale;
                half _CrackWidth;
                half _CrackPulseHz;
            CBUFFER_END

            // ГЛОБАЛЬНЫЙ privacy-множитель (0..1). Пишется одним вызовом
            // Shader.SetGlobalFloat из HorrorSafetyGovernor — гасит хоррор
            // на ВСЕХ материалах сразу, без обхода рендереров.
            // ВНИМАНИЕ: по умолчанию глобалы = 0 — Governor ОБЯЗАН выставить
            // 1.0 в Awake (пункт чек-листа), иначе эффекты невидимы.
            half _HorrorScale;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1; // для выборки opaque-текстуры
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // ------------------------------------------------------------
                // 0. Общие величины
                // ------------------------------------------------------------
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                half t = (half)_Time.y;

                // Мягкое затухание к краям quad'а, чтобы оверлей не имел
                // видимой рамки: 1 в центре -> 0 на краях.
                half2 centered = abs(input.uv - 0.5h) * 2.0h;
                half edgeFade = saturate(1.0h - max(centered.x, centered.y));
                edgeFade *= edgeFade; // квадратичное — дешевле smoothstep

                // ------------------------------------------------------------
                // 1. ДИСТОРСИЯ: flow-вектора из RG-каналов шума
                // ------------------------------------------------------------
                float2 noiseUV = input.uv * _DistortionScale
                               + float2(0.0, t * _DistortionSpeed * 0.1);
                half3 noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).rgb;

                half2 flow = (noise.rg * 2.0h - 1.0h);       // -1..1
                // Дрожь: высокочастотная синус-модуляция поверх плавного flow —
                // даёт нервную "вибрацию", а не ленивое марево.
                half jitter = sin(t * 23.0h + noise.r * 12.0h) * 0.5h + 0.5h;
                half2 offset = flow * jitter
                             * (_DistortionAmount * _DistortionStrength * edgeFade * _HorrorScale);

                half3 sceneColor = SampleSceneColor(screenUV + offset).rgb;

                // ------------------------------------------------------------
                // 2. ТРЕЩИНА: тонкие линии из B-канала шума + радиальное
                //    раскрытие от центра по _CrackProgress
                // ------------------------------------------------------------
                half crackNoise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                                      input.uv * _CrackScale + noise.rg * 0.05h).b;
                // Линия там, где шум пересекает 0.5: |n-0.5| < width
                half line_ = 1.0h - smoothstep(0.0h, _CrackWidth, abs(crackNoise - 0.5h));

                // Раскрытие: круг растёт из центра UV; рваный фронт за счёт
                // подмешивания шума в радиус — скол ползёт "рывками".
                half radial = length(input.uv - 0.5h) + (crackNoise - 0.5h) * 0.15h;
                half reveal = step(radial, _CrackProgress * 0.75h);

                half pulse = 0.75h + 0.25h * sin(t * _CrackPulseHz * 6.2832h);
                half crackMask = line_ * reveal * edgeFade;
                half3 emission = _CrackColor.rgb * (crackMask * pulse * _HorrorScale);

                // ------------------------------------------------------------
                // 3. Композиция
                //    Альфа = есть ли вообще что показывать (дисторсия ИЛИ
                //    трещина). При обоих параметрах в нуле пиксель полностью
                //    прозрачен — ранний выход блендинга, overdraw бесплатен.
                // ------------------------------------------------------------
                half alpha = saturate(max(_DistortionStrength * edgeFade, crackMask) * _HorrorScale);
                return half4(sceneColor + emission, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
