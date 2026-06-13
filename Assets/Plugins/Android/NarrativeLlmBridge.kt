package com.gate2reality

import android.os.Handler
import android.os.HandlerThread
import com.google.mediapipe.tasks.genai.llminference.LlmInference
import com.google.mediapipe.tasks.genai.llminference.LlmInference.LlmInferenceOptions
import com.unity3d.player.UnityPlayer
import java.io.File

object NarrativeLlmBridge {

    private val handlerThread = HandlerThread("LlmInference").also { it.start() }
    private val handler = Handler(handlerThread.looper)

    private var inference: LlmInference? = null

    private fun ensureLoaded() {
        if (inference != null) return
        val context = UnityPlayer.currentActivity.applicationContext
        val modelPath = File(context.filesDir, "models/gemma.task").absolutePath
        val options = LlmInferenceOptions.builder()
            .setModelPath(modelPath)
            .setMaxTokens(64)
            .setTopK(40)
            .setTemperature(0.8f)
            .build()
        inference = LlmInference.createFromOptions(context, options)
    }

    @JvmStatic
    fun requestWhisper(prompt: String, gameObject: String, callbackMethod: String) {
        handler.post {
            try {
                ensureLoaded()
                val result = inference!!.generateResponse(
                    "You are a ghost narrator. In one short sentence (max 12 words), respond to: $prompt"
                )
                UnityPlayer.UnitySendMessage(gameObject, callbackMethod, result.trim())
            } catch (e: Exception) {
                // Timeout or error — Unity side handles fallback via timer
                android.util.Log.w("Gate2Reality", "LLM error: ${e.message}")
            }
        }
    }

    @JvmStatic
    fun shutdown() {
        handler.post {
            inference?.close()
            inference = null
        }
    }
}
