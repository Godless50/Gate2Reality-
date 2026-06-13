# Tooling & Tech-Debt — Этап E (часть 1)

Три системы, снимающие технический долг для роста контента и QA. Все три —
аддитивные: существующие сцены работают без единой правки, новые возможности
включаются опционально.

---

## 1. ScriptableObject-граф + визуальный редактор

### Файлы
- `Assets/Scripts/Narrative/NarrativeGraphAsset.cs` — рантайм-ассет графа.
- `Assets/Editor/NarrativeGraphEditorWindow.cs` — окно редактора (Editor-only).

### Зачем
Раньше граф (`NarrativeNode[]`) жил инлайном в инспекторе `NarrativeManager`.
Это не масштабируется: ветвления, несколько сцен, ревью диффов в YAML.
Теперь **структура** графа (узлы, условия, dwell, рёбра) хранится в ассете и
правится визуально; **сценозависимые ссылки на эффекты** остаются в сцене
(ассет не может ссылаться на объекты сцены).

### Как пользоваться
1. `Assets → Create → Gate2Reality → Narrative Graph` — создаётся ассет.
2. Открыть: двойной клик по ассету **или** `Window → Gate2Reality → Narrative Graph`.
3. `Add Node`, перетаскивание блоков, правка полей в правой панели, рёбра
   `nextNodeIndices` рисуются безье-стрелками. Раскладка хранится в ассете
   (`EditorNodePositions`), переживает перезапуск.
4. В сцене на `NarrativeManager`:
   - назначить **Graph Asset**;
   - заполнить **Graph Triggerable Bindings** — массив `{ nodeIndex, behaviours[] }`,
     где `behaviours` — компоненты-`ITriggerable` (Chair/Book/Cup-эффекты) для узла.

### Рантайм-контракт
`NarrativeManager.Awake`: если `graphAsset != null` →
`graphAsset.CreateRuntimeNodes()` делает **глубокую копию** узлов (покадровые
мутации `DwellAccumulator`/`LastSeenPose` никогда не пачкают ассет на диске),
затем `ApplyTriggerableBindings()` доливает эффекты по индексу. Если ассет не
назначен — работает инлайн-массив, как раньше. **Полная обратная совместимость.**

### Готовые графы (генератор)
`Tools → Gate2Reality → Generate Chapter I / II Graph`
(`Assets/Editor/ChapterGraphGenerator.cs`) создаёт канонические ассеты в
`Assets/Narrative/`: Глава I — узлы 0–6 ровно по чек-листу §4 (dwell) + §13
(WallEcho/SurfaceEcho/PortalWall/Crossing); Глава II — перевёрнутая арка на
`AvertedGaze`. Дальше — правка в визуальном редакторе. Закрывает разрыв
«система графа есть, контента нет».

---

## 2. Дебаг-HUD детекций

### Файл
- `Assets/Scripts/DebugTools/DetectionDebugHud.cs`

### Зачем
Полевой прогон (Этап A) и QA: видеть вживую, что детектор и граф делают прямо
сейчас, без подключения профайлера.

### Что показывает
- **FPS** (сглаженный, цвет-индикация: зелёный ≥28, жёлтый ≥24, красный ниже);
- **Состояние узла**: индекс, имя, прогресс-бар dwell `current/target`;
- **Guard Node**: ступень (`Dormant/BeaconFired/Desaturated/ParticlesFired`)
  и бар простоя `idle/threshold`;
- **Privacy**: флаг «человек в кадре» + текущая интенсивность хоррора (бар);
- **Лента детекций**: последние N (label, confidence, радиус, возраст), цвет
  угасает со временем.

### Важно
Весь рабочий код — под `#if UNITY_EDITOR || DEVELOPMENT_BUILD`. **В релизной
сборке компонент пуст** — ноль влияния на горячий путь. IMGUI (`OnGUI`), без
зависимости от Canvas: бросить на любой объект, назначить `detector`,
`narrativeManager`, `safetyGovernor`. `ToggleVisible()` — для меню разработчика.

Снимок состояния графа отдаёт `NarrativeManager.GetDebugSnapshot()` — тоже
только в dev-сборках, тривиальные геттеры приватных полей.

---

## 3. Сейвы прогресса

### Файлы
- `Assets/Scripts/Persistence/ProgressStore.cs` — JSON I/O.
- `Assets/Scripts/Persistence/ProgressTracker.cs` — связка с графом.

### Зачем
Возобновление главы: игрок закрыл приложение — вернулся на тот же узел.

### Как работает
- `ProgressStore` — один атомарно записываемый файл
  `Application.persistentDataPath/progress.json` (версионируется под миграции).
  Данные: глава, индекс узла, маска увиденных объектов, флаг `crossedOver`, время.
- `ProgressTracker` (MonoBehaviour на `[Gate2Reality Core]`):
  - **пишет** сейв на каждой `OnNodeActivated`, на `CrossingTransitionEffect.OnCrossedOver`
    и на `OnSceneCompleted`;
  - **возобновляет**: в `Awake` (до любого `Start`) читает сейв и зовёт
    `NarrativeManager.SuppressAutoStart()`; в своём `Start` —
    `StartSceneAt(savedNode)`. Гонок нет: `Awake` всегда раньше `Start`.

### Новое в `NarrativeManager`
- `StartSceneAt(int nodeIndex)` — старт/resume с произвольного узла;
- `SuppressAutoStart()` — отдать автозапуск трекеру;
- `autoStartOnStart` (инспектор, по умолчанию **on**) — поведение по умолчанию
  не изменилось;
- `CurrentNodeIndex`, `IsSceneRunning`, `NodeCount` — публичные геттеры для сейвов.

### Resume и Сцена 2
Узлы Сцены 2 пространственные — их `runtimeTarget` ставит `EchoZonePlacer` в
рантайме. Resume наиболее осмыслен на границах узлов внутри главы; для
поузлового возобновления Сцены 2 поверх этого ляжет восстановление ARCore
persistent anchors (следующий шаг Этапа E).

---

## Сборка (asmdef)

| Сборка | Платформы | Ссылки |
|--------|-----------|--------|
| `Gate2Reality.Persistence` | все | Narrative, Effects |
| `Gate2Reality.DebugTools`  | все | Narrative, Detection, Effects |
| `Gate2Reality.Editor`      | **Editor** | Narrative |

Релизный билд: `DebugTools` компилируется пустым (dev-флаг), `Editor` не входит
в сборку по определению.
