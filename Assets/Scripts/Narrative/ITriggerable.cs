using UnityEngine;

namespace Gate2Reality.Narrative
{
    /// <summary>
    /// Контракт для любого сценического эффекта, который активируется узлом
    /// нарративного графа: world-space свет, шейдер дисторсии, партиклы,
    /// аудио-шёпот, голограмма карты и т.д.
    ///
    /// Дизайн-решение: эффекты НИЧЕГО не знают о детекторе (YOLO) и о графе.
    /// Они получают только Pose физического объекта-якоря. Это позволяет
    /// менять детектор (YOLO / Depth-fallback / ручной дебаг-тап) без
    /// единой правки контента.
    /// </summary>
    public interface ITriggerable
    {
        /// <summary>Уникальный ID — для логов и отладочного HUD.</summary>
        string TriggerId { get; }

        /// <summary>
        /// Запуск эффекта. worldAnchor — поза физического объекта,
        /// вычисленная детектором (центр 3D-бокса от YOLO + Depth raycast).
        /// Передаётся по 'in' — struct без копирования, ноль аллокаций.
        /// </summary>
        void Trigger(in Pose worldAnchor);

        /// <summary>Принудительная остановка (guard-сценарий / рестарт сцены).</summary>
        void Cancel();

        /// <summary>true, пока эффект проигрывается — граф может ждать завершения.</summary>
        bool IsActive { get; }
    }

    /// <summary>
    /// Семантические метки Сцены 1. byte — компактно, сравнение через
    /// enum == enum не боксит и не аллоцирует (в отличие от строк!).
    /// При переходе на YOLO: классы COCO 56(chair), 73(book), 41(cup)
    /// маппятся в эти значения внутри детектора.
    /// </summary>
    public enum NarrativeLabel : byte
    {
        None  = 0,
        Chair = 1,
        Book  = 2,
        Cup   = 3,

        // Сцена 2: «виртуальные» метки. YOLO их НИКОГДА не производит
        // (MapLabel детектора не возвращает эти значения) — они существуют
        // только как фокус-объекты для генератора шёпотов и контекста.
        EchoZone = 4,
        Portal   = 5
    }

    /// <summary>
    /// Универсальное событие детекции. Struct (readonly) — живёт на стеке,
    /// ноль давления на GC даже при 30 событиях/сек от YOLO.
    /// </summary>
    public readonly struct DetectionEvent
    {
        public readonly NarrativeLabel Label;
        public readonly Pose WorldPose;      // позиция+ориентация объекта в мире
        public readonly float Confidence;    // 0..1 (YOLO score после NMS)
        public readonly float BoundsRadius;  // приблизительный радиус объекта, м

        public DetectionEvent(NarrativeLabel label, in Pose pose, float confidence, float boundsRadius)
        {
            Label = label;
            WorldPose = pose;
            Confidence = confidence;
            BoundsRadius = boundsRadius;
        }
    }
}
