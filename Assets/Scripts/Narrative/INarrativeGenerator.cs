using System;

namespace Gate2Reality.Narrative
{
    /// <summary>Грубая эвристическая классификация помещения для промпта MLLM.</summary>
    public enum RoomType : byte
    {
        Unknown = 0,
        LivingRoom = 1,
        Office = 2,
        Kitchen = 3
    }

    /// <summary>
    /// Контекст для генерации нарратива. Struct — собирается на стеке перед
    /// каждым запросом, ничего не держит в куче.
    /// </summary>
    public readonly struct NarrativeContext
    {
        /// <summary>Объект, который игрок рассматривает прямо сейчас.</summary>
        public readonly NarrativeLabel FocusObject;

        /// <summary>Битовая маска всех объектов, замеченных YOLO за сессию:
        /// bit = (1 &lt;&lt; (int)NarrativeLabel). Компактнее списка, без аллокаций.</summary>
        public readonly int SeenObjectsMask;

        /// <summary>Средняя яркость сцены 0..1 (ARCore Light Estimation).
        /// Тёмная комната -> MLLM пишет более тихий, вкрадчивый текст.</summary>
        public readonly float AmbientBrightness;

        /// <summary>Цветовая температура, К (тёплый ламповый свет против
        /// холодного дневного — влияет на тон шёпота).</summary>
        public readonly float ColorTemperatureKelvin;

        /// <summary>Выведенный тип помещения.</summary>
        public readonly RoomType Room;

        public NarrativeContext(NarrativeLabel focus, int seenMask,
                                float brightness, float kelvin, RoomType room)
        {
            FocusObject = focus;
            SeenObjectsMask = seenMask;
            AmbientBrightness = brightness;
            ColorTemperatureKelvin = kelvin;
            Room = room;
        }

        public bool HasSeen(NarrativeLabel label) =>
            (SeenObjectsMask & (1 << (int)label)) != 0;
    }

    /// <summary>
    /// Абстракция генератора нарратива. Две реализации:
    ///  - OnDeviceNarrativeGenerator: локальный MLLM на устройстве
    ///    (MediaPipe LLM Inference / Gemma int4). Privacy Android 15:
    ///    ни промпт, ни ответ не покидают девайс.
    ///  - Фолбэк на заготовленные реплики — встроен в ту же реализацию
    ///    (модель не установлена / таймаут / OOM-килл сервиса).
    ///
    /// Контракт асинхронный: инференс LLM занимает 0.5-3 с, блокировать
    /// игровой поток нельзя. onResult ВСЕГДА вызывается ровно один раз
    /// и ВСЕГДА на главном потоке Unity.
    /// </summary>
    public interface INarrativeGenerator
    {
        /// <summary>true, если on-device модель реально загружена;
        /// false — работаем на заготовках (игра не ломается!).</summary>
        bool IsModelAvailable { get; }

        /// <summary>Запросить короткий шёпот (1-2 предложения) под контекст.</summary>
        void RequestWhisper(in NarrativeContext context, Action<string> onResult);
    }
}
