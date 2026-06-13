# Gate2Reality — Чек-лист настройки Unity (Сцена 1, Android 15)

Definitive setup для Pixel 9 / Galaxy S26. Порядок имеет значение: сверху вниз.

---

## 1. Пакеты (Package Manager)

- [ ] **AR Foundation** 6.x (latest verified)
- [ ] **Google ARCore XR Plugin** 6.x (та же мажорная версия, что ARF)
- [ ] **Unity Sentis** 2.x — инференс YOLO
- [ ] **Universal RP** (версия из LTS)
- [ ] Удалить неиспользуемое: ARKit XR Plugin (iOS не цель) — минус размер сборки

## 2. Project Settings → Player (Android)

- [ ] Scripting Backend: **IL2CPP**, Target Architectures: **ARM64 only**
- [ ] Graphics API: **Vulkan** первым, GLES3 fallback'ом (Sentis GPUCompute и URP на Vulkan быстрее)
- [ ] Minimum API Level: **29**, Target API Level: **35 (Android 15)**
- [ ] Active Input Handling: Input System Package
- [ ] **Optimized Frame Pacing: ON** — ровный кадр без скачков частоты
- [ ] Incremental GC: **ON** (страховка; в горячем пути аллокаций всё равно ноль)
- [ ] Permissions: только CAMERA. Никаких INTERNET-зависимых фич в рантайме —
      privacy-обещание «всё on-device» должно быть видно даже в манифесте

## 3. XR Plug-in Management

- [ ] Android tab → **Google ARCore: ON**
- [ ] Requirement: **Required**; Depth: **Required** (Сцена 1 не работает без Depth API —
      честно отсекаем несовместимые устройства на уровне Play Store)

## 4. Иерархия сцены

```
[AR Session]            ← ARSession + ARInputManager
[XR Origin]
 └─ Camera Offset
     └─ Main Camera     ← ARCameraManager, ARCameraBackground,
                           AROcclusionManager, DepthPoseProjector (arCamera = этот)
[XR Origin]             ← ARRaycastManager, ARPlaneManager (визуализатор плоскостей ВЫКЛ —
                           дебаг-меши плоскостей убивают атмосферу и fillrate)
[Gate2Reality Core]     ← NarrativeManager, YoloObjectDetector, SceneOneDirector,
                           NarrativeContextCollector, OnDeviceNarrativeGenerator,
                           HorrorSafetyGovernor
[Effects]
 ├─ ChairEffect         ← ChairAwakeningEffect + Light(amber) + shadowQuad + оверлей ножек
 ├─ BookEffect          ← BookMemoryEffect + 3 AudioSource + ParticleSystem
 ├─ CupEffect           ← CupBreachEffect + оверлей трещины + осколки + holoMap
 └─ GuardFX             ← beaconSource (3D), guideParticles, Volume(desaturation)
```

- [ ] Связать ссылки в инспекторах (NarrativeManager.nodes: 0=Chair→[1], 1=Book→[2], 2=Cup→[])
- [ ] `dwellTimeSeconds`: Chair 0.75 / Book 0.75 / Cup 1.0 (чашка мелкая — детекция дрожит сильнее)

## 5. ARCameraManager / AROcclusionManager

- [ ] Light Estimation: **Ambient Intensity + Ambient Color** (нужно NarrativeContextCollector)
- [ ] AROcclusionManager → Environment Depth Mode: **Best** (Pixel 9/S26 тянут;
      при троттлинге ARCore сам деградирует)
- [ ] Occlusion Preference: **Prefer Environment Occlusion**
- [ ] Осколки чашки: материал **Opaque или Alpha-Cutout** — окклюзия рукой корректна
      только для геометрии, пишущей depth. Прозрачные партиклы рука перекрывать не будет —
      поэтому осколки = меши, а партиклы лишь пыль вокруг.

## 6. URP Asset + Renderer

- [ ] **Opaque Texture: ON**, Downsampling: **2x Bilinear** — без этого RealityDistortion
      рисует чёрное (SampleSceneColor читает именно её)
- [ ] **HDR: ON** — HDR-цвет трещины (_CrackColor до 2.5) обязан переполнять 1.0 для Bloom
- [ ] Renderer Features: **AR Background Renderer Feature** (камера-фон + окклюзия в URP)
- [ ] Post-processing Volume (global): **Bloom** (threshold 1.0, intensity ~0.6) +
      **Color Adjustments** (Saturation −100) на ОТДЕЛЬНОМ Volume c weight=0 —
      это и есть desaturationVolume для SceneOneDirector
- [ ] MSAA: 4x (AR-контента мало, fillrate позволяет), Render Scale: 1.0
- [ ] Shadows: выключить cascade'ы (Main Light Shadows: OFF) — виртуальная тень стула
      у нас своя (quad), реальные тени AR-света не нужны и дороги

## 7. Слои и Culling Matrix

| Слой            | Кто в нём                  | Зачем                                          |
|-----------------|----------------------------|------------------------------------------------|
| `ARSurfaces`    | плоскости, depth-меши      | raycast-маска; камерой не рендерится           |
| `HorrorOverlay` | оверлеи дисторсии/трещины  | исключён из света Amber (свет не должен бликовать на собственных оверлеях) |
| `Holograms`     | карта комнаты, призрак трещины | исключён из десатурации, если захотим оставить голограммы цветными |

- [ ] Physics: снять ВСЕ галочки в Layer Collision Matrix (физика не используется —
      Auto Sync Transforms OFF, Physics.simulationMode = Script, нулевой бюджет PhysX)
- [ ] Amber Light → Culling Mask: всё, КРОМЕ HorrorOverlay

## 8. Sentis / YOLO

- [ ] `yolov8n.onnx` (экспорт: opset 15, imgsz 640, без встроенного NMS) → папка `Assets/Models`
- [ ] В инспекторе YoloObjectDetector: ModelAsset = импортированная модель
- [ ] Проверка на устройстве: лог инференса ≤ 15 мс (Vulkan). Если > 25 мс — поднять
      inferenceIntervalMs до 300 (3.3 Гц всё ещё комфортно для нарратива)

## 9. MLLM (Kotlin-плагин)

- [ ] `NarrativeLlmBridge.kt` → `Assets/Plugins/Android/`
- [ ] `mainTemplate.gradle`: зависимость `com.google.mediapipe:tasks-genai`
      (версию сверить с актуальной; API класса LlmInference менялось между релизами)
- [ ] Модель `.task` — через **Play Asset Delivery (install-time)** в `filesDir/models/`
- [ ] Тест фолбэка: запуск БЕЗ модели — шёпоты идут из заготовок, игра не ломается
- [ ] **proguard**: `Assets/Plugins/Android/proguard-user.txt` + Minify(R8) в релизе —
      иначе мост `com.gate2reality.llm.*` будет вырезан/переименован (см. `docs/ANDROID_BUILD.md`)
- [ ] Android-обвязка готова: `AndroidManifest.xml` (CAMERA-only, INTERNET removed),
      `mainTemplate.gradle` (Kotlin+genai), `gradleTemplate.properties` (AndroidX).
      Подробности и нюанс GameActivity — `docs/ANDROID_BUILD.md`

## 10. Аудио

- [ ] AudioMixer: группа **Horror** (шёпот, белый шум, маяк, хруст) с exposed-параметром
      `HorrorVolumeDb` → ссылка в HorrorSafetyGovernor
- [ ] Все нарративные AudioSource → Output: группа Horror; spatialBlend = 1 (кроме фолбэк-режима маяка)
- [ ] DSP Buffer Size: Best Performance

## 11. Производительность / термопакет (финальная проверка)

- [ ] `Application.targetFrameRate = 30` в бутстрапе — для медленного хоррора 30 fps
      неотличимы от 60, а это буквально −40% энергии SoC. Главная анти-троттлинг мера.
- [ ] Profiler на устройстве, 10 минут геймплея: **GC Alloc в Update-блоке = 0 B**
      (допустимы разовые аллокации в момент срабатывания узлов и запросов MLLM)
- [ ] Thermal: `Application.lowMemory` + Android Thermal API через ARCore —
      при THROTTLING_SEVERE поднять inferenceIntervalMs ×2 (одной строкой)
- [ ] Privacy-аудит: ни одного сетевого вызова в рантайме (проверить Charles/PCAP),
      кадры камеры не сохраняются, person-детекции не логируются (только bool-флаг)

## 12. Smoke-тест Сцены 1 (порядок прохождения)

1. Запуск → плоскости найдены → наводимся на стул → 0.75с → янтарный свет + дрожь ножек, тень «сканирует».
2. Мельком показать книгу камере → тень доворачивается на книгу.
3. Удержать книгу в кадре → страницы + шум + шёпот (или заготовка) + звон → призрак трещины.
4. Навестись на чашку → синяя трещина ползёт → рука перекрывает осколки (проверить окклюзию!) → голограмма карты.
5. Попросить друга войти в кадр на шаге 4 → хоррор гаснет за полсекунды, через ~4с после выхода — медленно возвращается.
6. Встать и ничего не делать 45с → маяк; 60с → мир обесцвечивается; 75с → партиклы-проводник.

---

## 13. Сцена 2 «Картограф» — дельта настройки

Граф продолжается за Чашку: у узла 2 выставить `nextNodeIndices = [3]`.

| # | Узел         | Условие   | Параметры                  | dwell | Эффекты (triggerables)            |
|---|--------------|-----------|----------------------------|-------|-----------------------------------|
| 3 | WallEcho     | Proximity | triggerRadius = 1.2        | 0.5   | PortalWindowEffect (d = 0.6 м)    |
| 4 | SurfaceEcho  | Proximity | triggerRadius = 1.2        | 0.5   | EchoSurfaceEffect                 |
| 5 | PortalWall   | Gaze      | 12°, maxDist = 6           | 1.0   | PortalWindowEffect (d = 2.0 м)    |
| 6 | Crossing     | Proximity | triggerRadius = 0.5        | 0.4   | CrossingTransitionEffect          |

- [ ] `runtimeTarget` узлов 3–5 ставит **EchoZonePlacer**, узла 6 — **SceneTwoDirector** (общий якорь с порталом: дверь открывается взглядом, пересекается ногами)
- [ ] `[Gate2Reality Core]` += EchoZonePlacer, HoloMapController, SceneTwoDirector
- [ ] XR Origin += **ARAnchorManager** (без него зоны уплывают при уточнении карты)
- [ ] `NarrativeManager.playerCamera` = Main Camera из XR Origin (нужен Proximity/Gaze)
- [ ] Quad окна портала: **массив из двух материалов** [PortalWindow, PortalRim]; quad ряби пола: [PortalRim]
- [ ] Интерьер InvertedWorld: оболочка — материал на Geometry+20 (по умолчанию), реквизит — `material.renderQueue` 2021–2025 (painter's-порядок вглубь)
- [ ] Префаб интерьера авторится **вглубь локального −Z** якоря (forward якоря смотрит в комнату)
- [ ] CrossingTransitionEffect: **snapToAnchor = OFF**; Canvas вспышки — Screen Space - Overlay; отдельный global Volume «изнанки» с weight = 0
- [ ] HoloMapController.mapContentRoot — дочерний объект holographicMapRoot из CupBreachEffect

### Смоук-тест Сцены 2
1. Финал чашки → на голо-карте контур комнаты, 3 пульсирующие метки (фиолетовая — дверь), жёлтая точка ходит за игроком.
2. Подойти к стене с меткой → окно «лопается» (overshoot ~10%), внутри — холодное зазеркалье; закрыть ладонью — окно перекрывается рукой.
3. Шёпот изнанки субтитром ~1.2 с после раскрытия (или заготовка из MirrorLines — игра не ждёт MLLM).
4. Подойти к ряби на полу → круги расходятся, столб пыли, гул снизу.
5. Посмотреть на большую стену 1 с → дверь 2 м распахивается.
6. Войти в дверь → вспышка, под ней мир сменяется холодным гримом, стингер главы под пиком вспышки, OnCrossedOver для Сцены 3.

---

## 14. Финальный оптимизационный паспорт главы (Step 5)

Энергетическая лесенка детектора (главная статья расхода после AR-трекинга):
- Узлы 0–2: полный YOLO, 5 Гц (~1 Вт) — единственный отрезок, где он нужен.
- Активация Чашки: `SetPersonOnlyMode(true)` — privacy-вахта 1 Гц, только класс
  person, одно чтение тензора на якорь (~0.2 Вт). Обещание «гасим хоррор при
  людях в кадре» действует ВСЮ главу, а не только Сцену 1.
- Размещение зон: `PlaneDetectionMode.None` — сканирование плоскостей погашено.
- `OnSceneCompleted`: детектор выключается полностью; `OnDisable` отправляет
  говернору «кадр чист» (фикс застревания хоррора на 25%, если человек был
  в кадре в момент выключения).

Чек перед релиз-кандидатом:
- [ ] Прогон главы с Profiler: GC Alloc = 0 B вне моментов активации узлов
- [ ] Battery Historian / adb power: ступенька потребления на t(Чашка) видна
- [ ] Вахта: попросить человека войти в кадр в Сцене 2 — хоррор гаснет за 0.5с
      даже при 1 Гц инференса (worst-case реакция ~1.5с — приемлемо)
- [ ] Стык: человек в кадре в момент финальной вспышки — после OnCrossedOver
      интенсивность возвращается к 1.0 (не застревает)
- [ ] Полный смоук Сцены 1 (раздел 12) + Сцены 2 (раздел 13) одним прогоном

---

## 15. Полевой прогон: HONOR 90 (REA-NX9)

**Факты устройства:** официальный список ARCore — поддерживается, **Supports
Depth API** (вся фолбэк-цепочка и окклюзия рукой работают). Snapdragon 7 Gen 1
Accelerated (Adreno 644), 8/12 ГБ RAM, экран 2664×1200. `minSdkVersion 29`
покрывает любую прошивку (Android 13 / MagicOS 7.1 и новее).

**Профиль (применяет DeviceTuningProfile автоматически, тир Mid):**
YOLO 300 мс (3.3 Гц), Environment Depth = Fastest, renderScale 0.9, 30 fps cap.

- [ ] `DeviceTuningProfile` на `[Gate2Reality Core]`, Script Execution Order =
      раньше всех (Project Settings → Script Execution Order, −100)
- [ ] Сборка Development Build (для полевых логов `[Gate2Reality]`)
- [ ] 12-ГБ версия: Gemma-2B int4 ок; **8-ГБ версия: только Gemma-270M** —
      MagicOS агрессивно убивает память, 2B-модель спровоцирует OOM-килл

### Протокол прогона (строго по порядку)
```
adb logcat -c && adb logcat -v time Unity:I *:S | grep -E "Gate2Reality|Exception"
```
1. **Старт:** лог тира (`Тир устройства: Mid ... Adreno (TM) 644`) и
   `Depth API: поддерживается`. Нет этих строк — дальше не идти.
2. **Риск №2 (Sentis):** первые строки `YOLO inference+readback: NN ms`.
   Ожидание на Adreno 644: **20–35 мс**. Если > 45 мс — поднять
   `midIntervalMs` до 400; если эксепшен на `ReadbackRequest` — сверить
   версию пакета Sentis (API менялся между 1.x/2.x).
3. **Риск №1 (ориентация кадра):** навести на стул в ПОРТРЕТЕ. Янтарный свет
   должен загореться НА стуле. Смещение/мимо → снять видеолог, мне нужны
   3-4 строки детекций (`Узел ... ждём`) + скрин — патч одной строкой в
   `ConversionParams.transformation` + маппинг через display matrix.
   Проверить и в ландшафте.
4. **Риск №3 (MediaPipe):** лог `isModelReady`; шёпот книги: замерить
   задержку субтитра. Заготовка вместо генерации — норма (таймаут работает),
   но если ВСЕГДА заготовка при готовой модели — лог из Kotlin-моста.
5. **Окклюзия:** рука перед окном портала — окно перекрывается ладонью;
   рука перед осколками чашки — осколки прячутся за рукой.
6. **Термопрогон 15 мин** (полная глава + повтор Сцены 1):
   `adb shell dumpsys thermalservice | grep -A2 Temperature` до/после;
   `adb shell dumpsys battery | grep temperature`. Троттлинг-статус THROTTLING
   выше MODERATE на 15-й минуте — повод поднять интервалы YOLO ×1.5.
7. **Вахта:** второй человек входит в кадр в Сцене 2 — реакция ≤ 1.5 с
   (1 Гц инференса + фейд 0.5 с).

### Ожидаемые числа Honor 90 (для сверки)
| Метрика                     | Ожидание        | Красная зона |
|-----------------------------|-----------------|--------------|
| YOLO int8 GPU, 640²         | 20–35 мс        | > 45 мс      |
| FPS (cap 30)                | стабильные 30   | < 27         |
| Gemma-2B латентность (12ГБ) | 1.5–3 с (таймаут спасает) | OOM-килл |
| Батарея за 15 мин главы     | 4–6%            | > 9%         |

---

## 16. Сохранение / возобновление + персистентные якоря + меню

Подсистема кросс-сессионного восстановления: при повторном входе игрок
оказывается в той же комнате с теми же якорями (стул/книга/чашка/зоны),
без повторного прохождения. Всё **offline, on-device** — никакой сети.

### 16.1 Компоненты на `[Gate2Reality Core]`

- [ ] **AnchorRegistry** — рантайм-реестр живых Transform якорей. Без ссылок,
      просто положить на Core. Его читают все остальные.
- [ ] **OfflineAnchorRelocalizer** — трёхуровневое восстановление (L1→L2→L3).
      Связи в инспекторе:
      - `anchorRegistry` = AnchorRegistry (тот же объект)
      - `detector` = YoloObjectDetector (для L2 YOLO-окна)
      - `arCamera` = Main Camera (для L3 относительной геометрии)
      - `enableL2` = ON (HONOR 90 подтверждён), `l2WindowSeconds`/`l2FingerprintTolerance`
        оставить дефолт — их перезапишет DeviceTuningProfile по тиру
- [ ] **ProgressTracker** — связать новые поля:
      - `anchorRegistry` = AnchorRegistry
      - `referenceAnchorLabel` = **Chair** (опорная система отсчёта)
      - `relocalizer` = OfflineAnchorRelocalizer
      - `resumeOnStart` = ON

### 16.2 AnchorRegistrationHook на эффектах (КРИТИЧНО)

Без этого реестр пуст → сейв пуст → восстановление всегда падает в L3.
На КАЖДЫЙ из трёх семантических эффектов добавить компонент
**AnchorRegistrationHook** (он в сборке Persistence, на эффекты ссылок не плодит):

| GameObject          | narrativeManager | anchorRegistry | nodeIndex | label |
|---------------------|------------------|----------------|-----------|-------|
| ChairEffect         | NarrativeManager | AnchorRegistry | 0         | Chair |
| BookEffect          | NarrativeManager | AnchorRegistry | 1         | Book  |
| CupEffect           | NarrativeManager | AnchorRegistry | 2         | Cup   |

- [ ] Хук регистрирует transform эффекта на `OnNodeActivated` — Trigger()
      срабатывает РАНЬШЕ события, поза уже снэпнута, кадровой задержки не нужно.

### 16.3 EchoZonePlacer (Сцена 2)

- [ ] `EchoZonePlacer.anchorRegistry` = AnchorRegistry. После размещения зон
      они регистрируются как `EchoZone` (nodeIndex 3/4). В fingerprint НЕ входят
      (YOLO их не видит), но сохраняются в `anchors[]` для L3-фолбэка.

### 16.4 DeviceTuningProfile

- [ ] `DeviceTuningProfile.relocalizer` = OfflineAnchorRelocalizer. По тиру
      ставит окно L2: Flagship 2с / Mid 3с / Low 4с (медленнее YOLO → шире окно).

### 16.5 Главное меню (Continue / New Game)

Отдельный Canvas поверх сцены. **MainMenuController** в Awake вызывает
`NarrativeManager.SuppressAutoStart()` и (если сейв есть) `ProgressTracker.DeferToMenu()` —
все Awake выполняются до Start, поэтому ProgressTracker.Start() увидит флаг и
не стартует сам. Сцена ждёт выбора игрока.

- [ ] **MainMenuController** связи:
      - `progressTracker` = ProgressTracker, `narrativeManager` = NarrativeManager
      - `panelGroup` = CanvasGroup панели меню
      - `continueSection` / `continueInfoLabel` / `continueButton` — блок «Продолжить»
        (прячется, если сейва нет), `newGameButton` / `newGameLabel`
      - `fadeOutSeconds` = 0.4
- [ ] Кнопки: Continue → `progressTracker.BeginResume()`, New Game →
      `BeginFreshStart()` (с подтверждением, если есть прогресс — затирает сейв).
      Без сейва видна только одна кнопка «Начать».
- [ ] Script Execution Order: MainMenuController **раньше** ProgressTracker
      (вместе с DeviceTuningProfile в −100 группе достаточно — главное раньше Start).

### 16.6 Глава 2: перенос якорей

- [ ] `ChapterTwoDirector.ch1AnchorRegistry` = AnchorRegistry из главы 1.
      Директор читает якоря из реестра, инспекторные массивы — фолбэк.

### 16.7 Смоук-тест сохранения

1. Пройти до Чашки (узлы 0–2 активированы) → свернуть приложение / убить процесс.
2. Перезапуск → меню показывает **Continue: «the cup · N min ago»**.
3. Continue → L1 (тёплый возврат) или L2 (YOLO находит стул/книгу/чашку за окно)
   → якоря на местах, прогресс с узла 2. Debug-HUD: `Reloc: L1/L2 N anchor(s)`.
4. Унести телефон в другую комнату → Continue → L2 fingerprint mismatch → **L3**:
   якоря раскладываются относительно камеры (`Reloc: L3`), guard-подсказки доводят.
5. New Game при наличии сейва → подтверждение → сейв стёрт, реестр очищен,
   старт с нуля.
6. **Миграция v1→v2:** положить старый сейв без `anchors` → загрузка не падает,
   `anchors = null` → сразу L3, сцена стартует штатно.

### Privacy-инвариант L2 (проверить)
- [ ] L2 на короткое окно снимает person-only (`SetPersonOnlyMode(false)`),
      собирает детекции, **немедленно** возвращает person-only. Окно ≤ l2WindowSeconds.
      Кадры не покидают устройство, person-детекции не логируются.
