package com.affllp.canefactory.cane_factory_app

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.ContextCompat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    private val channelName = "com.affllp.canefactory/android_sim_gateway"

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, channelName).setMethodCallHandler { call, result ->
            when (call.method) {
                "configure" -> {
                    val server = call.argument<String>("serverUrl")?.trim().orEmpty()
                    val device = call.argument<String>("deviceId")?.trim().orEmpty()
                    val key = call.argument<String>("apiKey").orEmpty()
                    val sim = call.argument<String>("simSlot") ?: "DEFAULT"
                    val poll = call.argument<Number>("pollIntervalSeconds")?.toInt() ?: 5
                    if (!server.startsWith("https://", ignoreCase = true) || device.isBlank() || key.length < 12 ||
                        sim !in setOf("DEFAULT", "SIM1", "SIM2") || poll !in 2..300) {
                        result.error("invalid_configuration", "HTTPS URL, Device ID, API key (12+ characters), SIM and poll interval are required.", null)
                    } else runAsync(result) {
                        val settings = GatewaySettings(server.trimEnd('/'), device, key, sim, poll)
                        val response = GatewayHttpClient(settings).validate()
                        if (!response.optBoolean("valid", false)) throw IllegalStateException("Android SIM Gateway credentials are invalid.")
                        GatewaySecureStore.save(this, settings)
                        mapOf("valid" to true, "active" to response.optBoolean("active"),
                            "status" to response.optString("status", "OFFLINE"))
                    }
                }
                "testConnection" -> runAsync(result) {
                    val settings = GatewaySecureStore.load(this) ?: throw IllegalStateException("Configure the gateway first.")
                    val response = GatewayHttpClient(settings).validate()
                    mapOf("valid" to response.optBoolean("valid"), "active" to response.optBoolean("active"),
                        "status" to response.optString("status", "OFFLINE"))
                }
                "start" -> {
                    val missing = requiredPermissions().filter { checkSelfPermission(it) != PackageManager.PERMISSION_GRANTED }
                    if (missing.isNotEmpty()) {
                        requestPermissions(missing.toTypedArray(), 7311)
                        result.error("permissions_required", "Allow SMS, phone/SIM and notification permissions, then tap Start again.", null)
                    } else runAsync(result) {
                        val settings = GatewaySecureStore.load(this) ?: throw IllegalStateException("Configure the gateway first.")
                        val response = GatewayHttpClient(settings).validate()
                        if (!response.optBoolean("valid", false)) throw IllegalStateException("Android SIM Gateway credentials are invalid.")
                        GatewaySecureStore.setEnabled(this, true)
                        ContextCompat.startForegroundService(this, Intent(this, GatewayService::class.java))
                        mapOf("running" to true, "active" to response.optBoolean("active"))
                    }
                }
                "stop" -> {
                    GatewaySecureStore.setEnabled(this, false)
                    stopService(Intent(this, GatewayService::class.java))
                    result.success(mapOf("running" to false))
                }
                "status" -> {
                    val settings = GatewaySecureStore.load(this)
                    result.success(mapOf(
                        "configured" to (settings != null),
                        "running" to GatewaySecureStore.isEnabled(this),
                        "serverUrl" to (settings?.serverUrl ?: ""),
                        "deviceId" to (settings?.deviceId ?: ""),
                        "simSlot" to (settings?.simSlot ?: "DEFAULT"),
                        "pollIntervalSeconds" to (settings?.pollIntervalSeconds ?: 5)
                    ))
                }
                else -> result.notImplemented()
            }
        }
    }

    private fun requiredPermissions(): List<String> = buildList {
        add(Manifest.permission.SEND_SMS)
        add(Manifest.permission.READ_PHONE_STATE)
        if (Build.VERSION.SDK_INT >= 33) add(Manifest.permission.POST_NOTIFICATIONS)
    }

    private fun runAsync(result: MethodChannel.Result, action: () -> Map<String, Any>) {
        Thread {
            try {
                val value = action()
                runOnUiThread { result.success(value) }
            } catch (ex: Exception) {
                val message = (ex.message ?: "Gateway operation failed.").replace(Regex("[\\r\\n]+"), " ").take(300)
                runOnUiThread { result.error("gateway_error", message, null) }
            }
        }.start()
    }
}
