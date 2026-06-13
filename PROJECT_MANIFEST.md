# Gate2Reality — Манифест проекта (Глава I, кодовая база v1.0-fieldtest)

Иммерсивная нарративная AR-игра для Android. Unity (URP) + AR Foundation + ARCore,
on-device YOLO (Unity Sentis) для семантики, on-device MLLM (MediaPipe/Gemma) для
генеративных шёпотов. Без геолокации, без сети, кадры камеры не покидают устройство.

**Объём:** 24 C#-файла, 4 шейдера, 1 Kotlin-плагин, чек-лист на 15 разделов (~4300 строк).
**Статус:** код-комплит для Главы I, верифицирован симуляциями; ожидает полевого прогона
на HONOR 90 (поддержка ARCore + Depth API подтверждена по официальному списку).

---

## Архитектура (поток данных)

```
ARCameraManager ──YUV──> YoloObjectDetector (Sentis, YOLOv8n int8)
                              │ DetectionEvent        │ OnRawDetection / OnHumanPresenceChanged
                              v                       v
DepthPoseProjector <──2D──> NarrativeManager     SceneOneDirector   HorrorSafetyGovernor
(Depth->плоскости->маркер)   (Trigger-Action граф,  SceneTwoDirector  (_HorrorScale, дакинг)
                              Guard Node 45с)            │
                              │ Trigger(Pose)            │ RequestWhisper / RequestMirrorWhisper
                              v                          v
                    ITriggerable-эффекты        OnDeviceNarrativeGenerator <─> NarrativeLlmBridge.kt
                    (Сцена 1 и Сцена 2)         (префетч, таймаут 3с,          (MediaPipe, Gemma)
                                                 фолбэк-пулы)
                                                         │
                                                         v
                                                WhisperSubtitleController
```

---

## 1. Ядро нарративного графа (`Gate2Reality.Narrative`)

| Файл | Назначение |
|---|---|
| `ITriggerable.cs` | Контракт эффектов + `NarrativeLabel` (вкл. виртуальные EchoZone/Portal) + struct `DetectionEvent` (zero-GC) |
| `NarrativeCondition.cs` | Условия узлов: SemanticDetection / Proximity / Gaze; плоский класс с enum вместо полиморфизма; рантайм-цели для процедурных зон |
| `NarrativeNode.cs` | Сериализуемый узел графа: условие, dwell-time, рёбра, кэш ITriggerable |
| `NarrativeManager.cs` | FSM по графу; dwell с мягким распадом (терпит мерцание YOLO); **Guard Node**: 45с → маяк → +15с десатурация → +15с партиклы → повтор маяка/30с; `SetNodeRuntimeTarget`, прайминг якоря для guard |

## 2. Детекция (`Gate2Reality.Detection`)

| Файл | Назначение |
|---|---|
| `YoloObjectDetector.cs` | Захват CPU-кадра → squash 640², Sentis GPUCompute, **асинхронный readback**, NMS на преаллоц. массиве; классы cup/chair/book + person; **person-only вахта** (1 Гц, ~0.2 Вт) на Сцену 2; замер латентности в dev-сборках; `SetInferenceInterval` |
| `DepthPoseProjector.cs` | 2D-бокс → 3D-поза, фолбэк-цепочка: Depth-рейкаст → плоскости → аппроксимационный маркер (не может провалиться); оценка boundsRadius («стул < 1.5 м») |

## 3. Эффекты Сцены 1 «Echoes in the Silence» (`Gate2Reality.Effects`)

| Файл | Назначение |
|---|---|
| `TriggerableEffectBase.cs` | База: one-shot, snapToAnchor, Update только при активности |
| `ChairAwakeningEffect.cs` | Янтарный «дышащий» свет, ramp `_DistortionStrength` через MPB, тень-указатель (скан → доворот на книгу по первой YOLO-детекции) |
| `BookMemoryEffect.cs` | FSM без корутин: партиклы страниц → белый шум → шёпот → намёк на чашку (звон + призрак трещины) |
| `CupBreachEffect.cs` | `_CrackProgress` по кривой, взрыв осколков на 80%, голограмма карты (становится игровой в Сцене 2) |
| `SceneOneDirector.cs` | Связывание: guard-события → маяк/десатурация(Volume)/партиклы; префетч шёпота книги на активации стула; переключение детектора в person-only на чашке |

## 4. Сцена 2 «Картограф» (`Gate2Reality.SceneTwo`)

| Файл | Назначение |
|---|---|
| `EchoZonePlacer.cs` | Процедурное размещение 3 зон на классифицированных плоскостях; greedy с релаксацией разноса; eye-clamp 1.0–1.8 м; **портал выбирает первым** (самая большая стена); ARAnchor на зону; фолбэк-кольцо 120°; гасит plane detection после размещения |
| `EchoZone.cs` | Маркер-компонент якоря: индекс, тип поверхности, нормаль |
| `HoloMapController.cs` | Голо-карта: контуры плоскостей (LineRenderer), пульс-метки зон, точка игрока; строится один раз |
| `SceneTwoDirector.cs` | Шёпоты изнанки с префетч-эстафетой (инвариант «слот не-null» доказан симуляцией в 6 сценариях); привязка узла «Пересечение» к якорю портала |
| `PortalWindowEffect.cs` | Окно/дверь в зазеркалье: апертура easeOutBack (овершут 10%), интерьер вглубь −Z якоря |
| `EchoSurfaceEffect.cs` | Рябь на полу: пилообразная `_Aperture` на PortalRim — ноль новых шейдеров |
| `CrossingTransitionEffect.cs` | Финал: вспышка → подмена мира под ней (Volume изнанки, `OnCrossedOver` для Главы II) → reveal |

## 5. Генеративный нарратив (`Gate2Reality.Narrative` / UI)

| Файл | Назначение |
|---|---|
| `INarrativeGenerator.cs` | Интерфейс + `NarrativeContext` (битмаска увиденного, яркость, цвет. температура, тип комнаты) |
| `NarrativeContextCollector.cs` | Light Estimation + сырые детекции → контекст; эвристика комнаты |
| `OnDeviceNarrativeGenerator.cs` | Мост к Kotlin; гарантии: коллбэк ровно 1 раз, на главном потоке, таймаут 3с → фолбэк своего пула; `RequestWhisper` / `RequestMirrorWhisper`; вытеснение с синхронным фолбэком |
| `NarrativeLlmBridge.kt` | MediaPipe LLM Inference (Gemma int4), фоновый HandlerThread с пониженным приоритетом, Play Asset Delivery |
| `WhisperSubtitleController.cs` | Призрачные субтитры: zero-GC печатная машинка через `maxVisibleCharacters` |

## 6. Безопасность и адаптация

| Файл | Назначение |
|---|---|
| `HorrorSafetyGovernor.cs` | Человек в кадре → спад до 25% за 0.5с; гистерезис clearDelay 4с, восстановление 3с; каналы: глобальный `_HorrorScale` + дакинг AudioMixer + событие |
| `DeviceTuningProfile.cs` | Рантайм-тиры Flagship/Mid/Low по GPU+RAM (HONOR 90 = Mid: YOLO 300мс, Depth Fastest, renderScale 0.9); честная проверка Depth API с грациозной деградацией |

## 7. Шейдеры (URP, mobile-first, всё в half)

| Файл | Назначение |
|---|---|
| `RealityDistortion.shader` | Дисторсия ножек (`_CameraOpaqueTexture`, без GrabPass) + синяя процедурная трещина; множится на глобальный `_HorrorScale` |
| `PortalWindow.shader` | Стенсил-маска круглой апертуры (Geometry+10); ZTest LEqual + Offset → рука перекрывает окно |
| `InvertedWorld.shader` | Мир за стеной: Stencil Equal, **ZTest Always** (env-depth стены иначе отрезал бы мир), туман в глубину + фреснель (Geometry+20) |
| `PortalRim.shader` | Светящийся обод (Geometry+30, ПОСЛЕ мира) — второй материал того же quad'а |

## 8. Документация

`UNITY_SETUP_CHECKLIST.md` — 15 разделов: пакеты, Player Settings, XR, иерархия,
URP/окклюзия, слои, Sentis, MLLM, аудио, термопакет, смоук-тесты обеих сцен,
оптимизационный паспорт, **протокол полевого прогона HONOR 90** с adb-командами
и таблицей ожидаемых чисел.

---

## Верификация (что доказано симуляциями)

Логические порты один-в-один на Python, 9 прогонов: тайминги Guard (45/60/75/повтор-30),
dwell при мерцании YOLO 2/3 кадров, NMS (межклассовые перекрытия выживают), префетч
субтитров (опоздание 0.00с), гистерезис вахты, смешанный граф Semantic→Prox→Gaze
(границы конуса до 0.1°), размещение зон на 4 комнатах вкл. патологии, инвариант
эстафеты шёпотов (6 сценариев), сквозной таймлайн главы (энергопрофиль −39%).

**Найдено и исправлено по дороге (10):** блокирующий ReadbackAndClone; двойной Dispose
worker-тензора; context-хак в Kotlin; разрыв «MLLM-текст → игрок» (решён субтитрами
с префетчем); кража лучшей стены у портала; обод под инвертированным миром (очередь);
бесхозный EchoZone в чужом namespace; заморозка вахты на стыке сцен; артефакт
теста easeOutBack; уплывание зон без ARAnchor (предотвращено дизайном).

### Автотесты (Unity Test Framework)
Симуляции закреплены реальным сьютом `Assets/Tests/`:
- **EditMode** — предикаты условий (Semantic-фильтр, Proximity/Gaze/**AvertedGaze**,
  включая взаимоисключение Gaze↔AvertedGaze), раунд-трип сейвов `ProgressStore`,
  клон-изоляция `NarrativeGraphAsset.CreateRuntimeNodes` (мутация рантайма не
  пачкает ассет);
- **PlayMode** — FSM графа: dwell→активация→завершение, «нет детекций → нет
  срабатывания», эскалация Guard по порогу простоя.

**Расхождение, найденное тестами:** `_idleTimer` в `NarrativeManager` описан как
«секунды с последнего прогресса», но сбрасывается лишь при входе в узел (guard
считает «время в узле», не «время без прогресса»). Поведение ядра не менял —
вопрос к решению автора (фикс — одна строка в `ReportDetection`).

---

## ДАЛЬНЕЙШИЕ ДЕЙСТВИЯ

### Этап A — Полевой прогон HONOR 90 (сейчас, по §15 чек-листа)
1. Development Build по чек-листу §1–§13 (8-ГБ версия телефона → Gemma-270M!).
2. Протокол §15 строго по порядку; прислать лог: тир, Depth, `YOLO inference+readback`.
3. Три известных device-риска, патчи наготове:
   - ориентация кадра (свет мимо стула) → `ConversionParams.transformation` + display matrix;
   - сигнатуры Sentis (`ReadbackRequest`) → сверка с установленной версией пакета;
   - API MediaPipe genai → сверка `LlmInferenceOptions` с версией зависимости.

### Этап B — Производство ассетов (параллельно прогону; код их уже ждёт)
- Текстура шума (RG: flow, B: трещины) для RealityDistortion;
- Аудио: шёпот-подложка, белый шум, звон фарфора, хруст/гул breach, маяк,
  гул изнанки, разрыв мембраны портала, аккорд пересечения, эмбиент той стороны,
  стингер главы;
- Префабы: quad тени стула (текстура вытянутой тени), призрак трещины,
  партиклы (страницы, осколки-пыль, проводник), интерьер инвертированной
  комнаты (вглубь −Z, материалы InvertedWorld, очереди 2021–2025),
  голо-карта (материал additive), Canvas субтитров (TMP) и вспышки;
- AudioMixer с группой Horror (`HorrorVolumeDb`); Volume-профили
  (десатурация, изнанка); экспорт `yolov8n.onnx` + (опц.) Gemma `.task`.

### Этап C — Стабилизация по результатам прогона
- Закрыть полевые баги; термопрогон 15 мин → при THROTTLING поднять интервалы;
- Решение по «золотым» репликам: расширить пулы фолбэков (сейчас по 2 на сюжет);
- Повторный прогон → тег v1.1-stable.

### Этап D — Глава II «По ту сторону» (каркас в коде)
- ✅ Точка входа `CrossingTransitionEffect.OnCrossedOver` → `ChapterTwoDirector`;
- ✅ Guard ПРОТИВ игрока: `InvertedGuardDirector` переопределяет семантику тех
  же событий ядра (ложный зов / сжатие мира / тянущиеся партиклы) — без правок FSM;
- ✅ Знакомые объекты иначе: `InvertedObjectEffect` (оживает, крадётся, замирает
  под взглядом);
- ✅ Новый тип условия `ConditionType.AvertedGaze` («объект живёт, пока на него
  не смотрят») — единственная, аддитивная правка ядра;
- Переиспользование графа: Глава II — отдельный `NarrativeManager` + свой
  `NarrativeGraphAsset` (авторится в редакторе из Этапа E), пост-профиль изнанки
  как базовый.
- Осталось (продакшн): Ch2 граф-ассет, persistent anchors между сессиями,
  ассеты изнанки. Подробности — `docs/CHAPTER_TWO_DESIGN.md`.

### Этап E — Технический долг и продакшн
- ✅ Редактор графа на ScriptableObject + визуализация рёбер
  (`NarrativeGraphAsset` + `Assets/Editor/NarrativeGraphEditorWindow`);
- ✅ Дебаг-HUD (детекции, состояние узла, guard-таймер) для QA
  (`DetectionDebugHud`, только Editor/Development Build);
- ✅ Сейвы прогресса главы (`ProgressStore` + `ProgressTracker`,
  resume с сохранённого узла); восстановление якорей через ARCore
  persistent anchors — следующий шаг поверх этого;
- Play Store: Data Safety форма (всё on-device — сильная позиция),
  «AR Required» + Depth feature-флаг, матрица тестовых устройств
  (флагман / HONOR 90 / low-tier), стрельбовые сессии с живыми игроками.

Подробности тулинга — `docs/TECH_DEBT_TOOLING.md`.
