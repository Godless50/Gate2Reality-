# Android-сборка и Play-релиз

Конкретные конфиг-файлы под требования чек-листа §2/§3/§9/§11. Все лежат в
`Assets/Plugins/Android/` — это стандартные имена Unity-оверрайдов, редактор
подхватывает их при включённых тумблерах в Player Settings.

| Файл | Player Settings тумблер | Назначение |
|------|------------------------|------------|
| `AndroidManifest.xml` | Custom Main Manifest | CAMERA-only, **удаление INTERNET**, AR Required |
| `mainTemplate.gradle` | Custom Main Gradle Template | Kotlin + MediaPipe tasks-genai (unityLibrary), noCompress модели |
| `launcherTemplate.gradle` | Custom Launcher Gradle Template | сборка приложения, arm64, упаковка |
| `gradleTemplate.properties` | Custom Gradle Properties Template | AndroidX + jetifier (нужно MediaPipe) |
| `proguard-user.txt` | Custom Proguard File + Minify | keep-правила для Kotlin-моста MLLM и MediaPipe |

## Почему зависимость MLLM в mainTemplate, а не в launcher
`NarrativeLlmBridge.kt` компилируется в модуль **unityLibrary**. MediaPipe-классы
должны быть на его **compile-classpath**, иначе `.kt` не соберётся. Объявление
зависимости в launcher даёт классы только в рантайме — для компиляции поздно.

## Privacy — доказуемо в манифесте
`<uses-permission android:name="android.permission.INTERNET" tools:node="remove" />`
вычищает INTERNET из слитого манифеста, даже если транзитивная зависимость его
добавит. Плюс превентивно сняты ACCESS_NETWORK_STATE и геолокации. В итоге APK
физически не может выйти в сеть — это и есть фундамент обещания «всё на
устройстве» (чек-лист §11, Play Data Safety).

## proguard — почему критично
R8 в релизе переименовывает классы. `OnDeviceNarrativeGenerator` зовёт мост по
строковым именам:
- `AndroidJavaClass("com.gate2reality.llm.NarrativeLlmBridge")`
- `AndroidJavaProxy("com.gate2reality.llm.LlmCallback")`

Без `-keep` этих имён в рантайме не окажется → мост молча уйдёт в фолбэк (или
упадёт). Правила в `proguard-user.txt` держат мост, MediaPipe, ARCore и
Unity-player живыми.

## Доставка модели Gemma (.task)
Модель НЕ кладётся в APK (лимит размера) и НЕ качается из сети (privacy).
Канал — **Play Asset Delivery**, install-time pack, распаковка в
`filesDir/models/gemma-2b-it-int4.task` (путь захардкожен в `NarrativeLlmBridge.kt`).
До установки модели `isModelReady()` честно возвращает false → игра идёт на
заготовленных шёпотах. RAM-гейтинг: 12 ГБ → Gemma-2B int4; 8 ГБ → Gemma-270M.

## Application Entry Point (важный нюанс версии Unity)
Манифест объявляет классическую `UnityPlayerActivity`. Unity 6 в части конфигов
по умолчанию использует **GameActivity** — тогда поменяйте в манифесте имя
активити на `com.unity3d.player.UnityPlayerGameActivity` и тему на
`@style/BaseUnityGameActivityTheme`. Проверьте Player Settings → Application
Entry Point.

## Токены gradle-шаблонов
`**APIVERSION**`, `**MINSDKVERSION**`, `**DEPS**` и пр. подставляет Unity. Набор
токенов зависит от версии редактора: если при включении шаблона Unity ругается
на отсутствующий токен — дайте ему перегенерировать шаблон и перенесите наши
правки (Kotlin-плагин, зависимость tasks-genai, `noCompress` для `.task/.sentis`)
поверх свежего файла.

## Чек-лист релиза (дополняет §14)
- [ ] Player Settings: IL2CPP, ARM64, Vulkan→GLES3, minSdk 29 / target 35;
- [ ] включить пять Custom-тумблеров выше;
- [ ] Minify: Release (R8) — иначе proguard-правила не применятся;
- [ ] Play Data Safety: «данные не собираются, не передаются» (INTERNET снят);
- [ ] AR Required + ARCore Depth = Required (XR Plug-in Management);
- [ ] PAD-пак с `.task`; на 8 ГБ устройствах — вариант с Gemma-270M.
