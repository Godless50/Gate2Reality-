package com.gate2reality.llm

import android.app.Activity
import android.os.Handler
import android.os.HandlerThread
import com.google.mediapipe.tasks.genai.llminference.LlmInference
import java.io.File

/**
 * Android-сторона моста к on-device MLLM (MediaPipe LLM Inference API).
 * Кладётся в Assets/Plugins/Android вместе с зависимостью
 * com.google.mediapipe:tasks-genai в mainTemplate.gradle.
 *
 * МОДЕЛЬ: gemma-2b-it int4 (.task), доставляется через Play Asset Delivery
 * (install-time pack) в filesDir — НЕ в APK (лимит размера) и НЕ из сети
 * в рантайме (privacy-обещание «всё на устройстве» держим честно).
 *
 * ПОТОКИ: инференс на выделенном HandlerThread с пониженным приоритетом —
 * не воюем за big-ядра с рендером Unity и не провоцируем троттлинг.
 */
interface LlmCallback {
    fun onResult(text: String)
}

class NarrativeLlmBridge private constructor(activity: Activity) {

    // Захватываем application context сразу — не держим ссылку на Activity
    // (утечка при повороте экрана) и не лезем за ним из воркер-потока.
    private val appContext = activity.applicationContext

    companion object {
        @Volatile private var instance: NarrativeLlmBridge? = null

        @JvmStatic
        fun getInstance(activity: Activity): NarrativeLlmBridge =
            instance ?: synchronized(this) {
                instance ?: NarrativeLlmBridge(activity).also { instance = it }
            }
    }

    private val modelFile = File(activity.filesDir, "models/gemma-2b-it-int4.task")
    private var llm: LlmInference? = null

    private val workerThread = HandlerThread(
        "Gate2RealityLLM",
        android.os.Process.THREAD_PRIORITY_BACKGROUND
    ).apply { start() }
    private val worker = Handler(workerThread.looper)

    init {
        if (modelFile.exists()) {
            // Ленивая инициализация на воркере: первая загрузка модели ~1-2с,
            // главный поток (и Unity) этого не почувствуют.
            worker.post {
                try {
                    val options = LlmInference.LlmInferenceOptions.builder()
                        .setModelPath(modelFile.absolutePath)
                        .setMaxTokens(96)
                        .setTemperature(0.9f)   // шёпоту положено быть непредсказуемым
                        .setTopK(40)
                        .build()
                    llm = LlmInference.createFromOptions(appContext, options)
                } catch (_: Throwable) {
                    llm = null // C#-сторона уйдёт в фолбэк по isModelReady()
                }
            }
        }
    }

    /** Дёргается из C# (Awake). До конца ленивой инициализации честно вернёт false. */
    fun isModelReady(): Boolean = llm != null

    /** Неблокирующая генерация; коллбэк прилетит с воркер-потока. */
    fun generateAsync(prompt: String, maxTokens: Int, callback: LlmCallback) {
        worker.post {
            val text = try {
                llm?.generateResponse(prompt) ?: ""
            } catch (_: Throwable) {
                "" // пустая строка -> C#-сторона подставит заготовку
            }
            callback.onResult(text)
        }
    }
}
