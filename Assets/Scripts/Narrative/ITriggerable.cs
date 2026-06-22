using UnityEngine;

namespace Gate2Reality.Narrative
{
    /// <summary>Нарративная группа объекта — используется ритуальными условиями.</summary>
    public enum ObjectGroup : byte
    {
        Unknown  = 0,
        Sleep    = 1,
        Food     = 2,
        Movement = 3,
        Child    = 4,
        Light    = 5,
        Sharp    = 6
    }

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
        Portal   = 5,

        // v2: расширенный YOLO COCO-80 маппинг
        Table     = 6,  // COCO 60
        Tv        = 7,  // COCO 62
        Laptop    = 8,  // COCO 63
        Phone     = 9,  // COCO 67
        Bottle    = 10, // COCO 39
        Bowl      = 11, // COCO 45
        Fork      = 12, // COCO 42
        Knife     = 13, // COCO 43
        Scissors  = 14, // COCO 76
        TeddyBear = 15, // COCO 77
        Backpack  = 16, // COCO 26
        Couch     = 17, // COCO 57
        Bed       = 18, // COCO 59
        Bicycle   = 19  // COCO 1
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
