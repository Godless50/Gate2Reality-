# Дизайн: офлайн-релокализация и «persistent anchors»

**Статус:** проект (design). Реализация device-gated — финальные пороги и выбор
уровня по умолчанию калибруются на Stage A (Pixel 9 / HONOR 90).
**Цель:** при возобновлении сессии (resume из сейва, в т.ч. вход в Главу II)
вернуть нарративные якоря — позы реальных объектов комнаты (стул/книга/чашка)
и эхо-зон — на свои места, **не нарушив офлайн/privacy-обет**.

---

## 1. Ограничения, которые формируют дизайн

1. **Privacy/offline (нерушимо).** Ни кадр, ни облако точек, ни геокоордината не
   покидают устройство. Это исключает целые классы решений (см. §2).
2. **Android/ARCore реальность.**
   - Внутри одной сессии `ARAnchor` живут, пока держится трекинг.
   - **Кросс-сессионной офлайн-персистентности якорей у ARCore нет.** `ARWorldMap`
     — фича ARKit (iOS), на Android её нет. `Cloud Anchors` и `Geospatial` —
     сетевые. Значит «сохранить якорь в файл и поднять после холодного старта»
     на Android офлайн **технически невозможно** напрямую.
3. **Дисциплина проекта.** Zero-GC в горячем пути; одноразовые аллокации в
   `Awake`/на старте; **fallback-цепочка, которая не может провалиться** (как
   `DepthPoseProjector` L1→L2→L3 и фолбэк-кольцо `EchoZonePlacer`).

**Вывод:** «persistent anchors» здесь — это не сохранение якоря, а
**релокализация перерасчётом**: персистим *семантику и геометрию* комнаты, а
точные `ARAnchor` пересоздаём в новой сессии.

---

## 2. Что отвергнуто и почему

| Подход | Почему нет |
|--------|------------|
| ARCore Cloud Anchors | сеть — ломает privacy-обет |
| ARCore Geospatial / VPS | сеть + геолокация — двойное нарушение |
| ARKit ARWorldMap | iOS-only, на Android не существует |
| Сырое сохранение `ARAnchor` | публичного офлайн-API кросс-сессии у ARCore нет |

Остаётся то, что полностью на устройстве: **повторная детекция (YOLO)** и
**относительная геометрия комнаты**.

---

## 3. Трёхуровневая релокализация (философия `DepthPoseProjector`)

Уровень всегда деградирует вниз и **L3 не может провалиться**.

```
RESUME
  │
  ├─ L1  Тёплый возврат (та же сессия трекинга)
  │      Приложение было свёрнуто/экран гас, ARCore не терял трекинг.
  │      Живые ARAnchor ещё валидны — ничего восстанавливать не нужно.
  │      Признак: ARSession.state Tracking без сброса + тот же
  │      session-id (хранится в рантайме, не на диске).
  │
  ├─ L2  Холодный старт + повторная детекция  (точно)
  │      Перезапускаем полный YOLO на N секунд. Каждую найденную метку
  │      проецируем DepthPoseProjector.TryProjectToWorld -> мировая поза,
  │      ставим свежий ARAnchor. Сопоставляем с сейвом по:
  │        (a) метке (Chair/Book/Cup) и
  │        (b) относительной геометрии (RoomFingerprint, §4) —
  │      инвариант к мировому повороту/сдвигу новой сессии.
  │      Привязываем восстановленные якоря к узлам через
  │      NarrativeManager.SetNodeRuntimeTarget.
  │
  └─ L3  Холодный старт, объекты не найдены  (приблизительно, не падает)
         Объекты унесли/переставили/не распознались. Поднимаем
         СОХРАНЁННЫЕ ОТНОСИТЕЛЬНЫЕ позы вокруг текущей камеры (как
         фолбэк-кольцо EchoZonePlacer): расставляем зоны по запомнённой
         геометрии относительно игрока + включаем guard-подсказки.
         Якоря «не приклеены» к миру, но глава проходима.
```

L2 — целевой путь для Главы II: объекты Главы I (стул/книга/чашка) физически в
комнате, их достаточно переякорить и подать в `ChapterTwoDirector.carriedAnchors`.

---

## 4. Модель данных (что персистим)

Только геометрия и метки — **никаких изображений, дескрипторов кадра, GPS.**
Расширяем существующий `ProgressData` (версионирование уже есть в `ProgressStore`).

```csharp
[Serializable]
public sealed class AnchorRecord
{
    public int label;          // (int)NarrativeLabel — Chair/Book/Cup/EchoZone...
    public int nodeIndex;      // узел графа, которому принадлежит якорь
    public Vector3 localPos;   // поза В СИСТЕМЕ ОТСЧЁТА комнаты (см. ниже), не в мире
    public Quaternion localRot;
    public float confidence;   // уверенность последней валидной детекции
}

[Serializable]
public sealed class RoomFingerprint
{
    // Инвариант комнаты: попарные расстояния между якорями. Не зависит от того,
    // как ARCore выберет мировой ноль в новой сессии. Используется в L2 для
    // сопоставления «этот свежедетектированный стул — тот самый».
    public float[] pairwiseDistances; // верхний треугольник матрицы, метры
    public int anchorCount;
}

// Расширение ProgressData (bump ProgressStore.CurrentVersion -> 2, миграция:
// старый сейв без якорей => L3/чистый старт главы):
public AnchorRecord[] anchors;
public RoomFingerprint fingerprint;
public int referenceFrameLabel; // якорь-начало координат комнаты (напр. Chair)
```

**Система отсчёта комнаты.** Мировой ноль ARCore нестабилен между сессиями,
поэтому позы храним **относительно одного опорного якоря** (`referenceFrameLabel`,
напр. стул — он самый «тяжёлый»/стабильный по ТЗ). `localPos/localRot` = поза в
координатах опорного. В L2 после повторной детекции опорного объекта весь набор
разворачивается в новый мир одним преобразованием.

---

## 5. Архитектура и контракты

Новая сборка `Gate2Reality.Persistence` уже существует — кладём туда.

```csharp
/// Регистр живых якорей сессии: эффекты/placer регистрируют сюда свои
/// Transform по мере появления. Источник истины для сейва.
public interface IAnchorRegistry
{
    void Register(int nodeIndex, NarrativeLabel label, Transform anchor);
    bool TryGet(int nodeIndex, out Transform anchor);
    IReadOnlyList<(int nodeIndex, NarrativeLabel label, Transform t)> All { get; }
}

/// Сериализация набора в ProgressData (относительно опорного якоря) и обратно.
public static class AnchorSerializer
{
    public static void Capture(IAnchorRegistry reg, NarrativeLabel referenceLabel, ProgressData into);
    public static RoomFingerprint Fingerprint(IAnchorRegistry reg);
}

/// Релокализатор: реализует L1→L2→L3. Асинхронный (L2 ждёт детекций),
/// коллбэк — как у генератора нарратива: ровно один раз, на главном потоке.
public interface IAnchorRelocalizer
{
    /// Восстановить якоря из сейва. onComplete получает карту nodeIndex->Transform
    /// и достигнутый уровень (для лога Stage A / дебаг-HUD).
    void Relocalize(ProgressData save, Action<RelocalizationResult> onComplete);
}

public readonly struct RelocalizationResult
{
    public readonly int Level;                 // 1/2/3
    public readonly IReadOnlyDictionary<int, Transform> NodeAnchors;
}
```

### Как встаёт на существующий код (минимальные правки)
- **Запись.** `EchoZonePlacer` уже создаёт `ARAnchor` на зоны и держит `Zones`;
  эффекты Главы I знают позы объектов. Добавляем `AnchorRegistry` (MonoBehaviour),
  в который они регистрируют Transform; `ProgressTracker` на каждом
  `OnNodeActivated` вызывает `AnchorSerializer.Capture` и `ProgressStore.Save`.
- **Чтение.** На resume `ProgressTracker` (уже умеет `SuppressAutoStart` +
  `StartSceneAt`) перед стартом зовёт `IAnchorRelocalizer.Relocalize`; по
  результату — `NarrativeManager.SetNodeRuntimeTarget(nodeIndex, t)` для каждого
  якоря, затем `StartSceneAt`.
- **Глава II.** `ChapterTwoDirector.carriedAnchors` сегодня заполняется в
  инспекторе; вместо этого — из `RelocalizationResult.NodeAnchors`. Поле остаётся
  как ручной фолбэк/дебаг.
- **Детектор.** L2 требует временно снять person-only и поднять полный YOLO на
  окно релокализации, затем вернуть person-only (privacy-вахта). Управляется
  `YoloObjectDetector.SetPersonOnlyMode` — API уже есть.

---

## 6. Потоки

### Capture (во время игры)
```
OnNodeActivated(node, pose)
   → AnchorRegistry.Register(node, label, anchorTransform)   // если ещё не
   → AnchorSerializer.Capture(registry, referenceLabel, data)
   → ProgressStore.Save(data)        // атомарно, ~<1 КБ
```

### Resume (холодный старт)
```
ProgressTracker.Awake
   → ProgressStore.TryLoad(save)
   → narrativeManager.SuppressAutoStart()
ProgressTracker.Start
   → relocalizer.Relocalize(save, result =>
        {
          foreach (node→t in result.NodeAnchors)
              narrativeManager.SetNodeRuntimeTarget(node, t);
          narrativeManager.StartSceneAt(save.nodeIndex);
          // лог уровня релокализации для Stage A + дебаг-HUD
        })
```

---

## 7. Failure modes / деградация

| Ситуация | Поведение |
|----------|-----------|
| Тёплый возврат, трекинг цел | L1: живые якоря, нулевая стоимость |
| Объекты на месте, распознаны | L2: точное переякоривание |
| Опорный объект не найден | L2′: берём следующий по `confidence` как опорный |
| Объекты унесли/не распознаны | L3: относительные позы вокруг игрока + guard |
| Сейв без якорей (старая версия) | миграция: чистый старт главы с её entry-узла |
| Повреждённый сейв | `ProgressStore.TryLoad` уже возвращает false → чистый старт |

Инвариант: **resume никогда не блокирует игрока** — то же обещание, что у guard
и фолбэков размещения.

---

## 8. Privacy-анализ

Персистится исключительно: целочисленные метки, относительные позы (float),
попарные расстояния, таймстамп. **Нет** изображений, нет дескрипторов кадра, нет
координат. Файл — в app-private `filesDir` (`Application.persistentDataPath`),
вне общего хранилища. Это согласуется с манифестом без INTERNET (`docs/ANDROID_BUILD.md`)
и с Play Data Safety «данные не собираются/не передаются».

---

## 9. Развилка и рекомендация

**Вопрос:** делать ли L2 (повторная детекция) в v1, или ограничиться L1+L3?

- **L1+L3 (минимум):** проще, без окна полного YOLO на resume; цена — после
  холодного старта позы приблизительные (дрейф до ~10–20 см, как и предупреждал
  комментарий `EchoZonePlacer` про якоря). Для Главы I терпимо (guard доведёт),
  для портала Главы II в стене — заметно.
- **L1+L2+L3 (полный):** точное переякоривание, ценой ~2–4 с полного YOLO и
  кода сопоставления по fingerprint.

**Рекомендация:** заложить интерфейсы под все три уровня, реализовать **L1+L3
сразу** (дёшево, не падает), а **L2 включить после Stage A**, когда на реальном
устройстве измерим, сколько секунд YOLO нужно для надёжной повторной детекции и
не бьёт ли это по термопакету. Это ровно тот стиль «пороги калибруем на железе»,
что уже принят в `DeviceTuningProfile`.

---

## 10. Device-зависимые точки (валидировать на Stage A)

- Окно L2: сколько секунд полного YOLO до надёжной детекции 2–3 объектов
  (ориентир: при 5 Гц — 1–3 с; красная линия — >5 с);
- Порог совпадения fingerprint (терпимость к перестановке мебели);
- Точность позы после L2 vs. дрейф L3 (мерить рулеткой у портала-в-стене);
- Стоимость окна L2 по батарее/нагреву (вернуть person-only сразу после).

---

## 11. Объём реализации (когда дадут go)

Новые файлы (всё в `Gate2Reality.Persistence`, кроме регистра-MonoBehaviour):
`AnchorRecord`/`RoomFingerprint` (в `ProgressData`), `AnchorRegistry`,
`AnchorSerializer`, `OfflineAnchorRelocalizer` (L1/L3 сразу, L2 за флагом).
Правки-вставки: `ProgressTracker` (capture+resume hook), `ChapterTwoDirector`
(источник carriedAnchors), bump `ProgressStore.CurrentVersion`→2 + миграция.
Тесты: сериализация относительных поз (раунд-трип в системе опорного якоря),
fingerprint-матчинг на синтетике, миграция v1→v2.

---

## 12. Открытые вопросы автору
1. Опорный объект комнаты — фиксируем Chair, или брать самый уверенный?
2. L2 в v1 или после Stage A (рекомендация — после)?
3. Допустимый дрейф L3 для портала Главы II — есть числовой порог из ТЗ?
