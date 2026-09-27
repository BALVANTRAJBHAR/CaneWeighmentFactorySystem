package com.affllp.canefactory.cane_factory_app

import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

data class PendingSms(val id: Int, val mobileNumber: String, val message: String, val simSlot: String)

class GatewayHttpClient(private val settings: GatewaySettings) {
    init {
        require(URL(settings.serverUrl).protocol.equals("https", ignoreCase = true)) {
            "Gateway server URL must use HTTPS."
        }
    }

    fun validate(): JSONObject = request("POST", "/api/android-sms-gateway/auth/validate")
    fun heartbeat(): JSONObject = request("POST", "/api/android-sms-gateway/heartbeat")

    fun pending(): List<PendingSms> {
        val result = request("GET", "/api/android-sms-gateway/messages/pending?limit=20")
        val items = result.optJSONArray("items") ?: return emptyList()
        return (0 until items.length()).map { index ->
            val item = items.getJSONObject(index)
            PendingSms(item.getInt("id"), item.getString("mobileNumber"),
                item.getString("message"), item.optString("simSlot", settings.simSlot))
        }
    }

    fun processing(id: Int) = request("POST", "/api/android-sms-gateway/messages/$id/processing")
    fun sent(id: Int, providerMessageId: String) = request("POST",
        "/api/android-sms-gateway/messages/$id/sent", JSONObject().put("providerMessageId", providerMessageId))
    fun failed(id: Int, reason: String) = request("POST",
        "/api/android-sms-gateway/messages/$id/failed", JSONObject().put("failureReason", reason.take(500)))

    private fun request(method: String, path: String, body: JSONObject? = null): JSONObject {
        val connection = URL(settings.serverUrl.trimEnd('/') + path).openConnection() as HttpURLConnection
        try {
            connection.requestMethod = method
            connection.connectTimeout = 10_000
            connection.readTimeout = 20_000
            connection.setRequestProperty("X-Device-Id", settings.deviceId)
            connection.setRequestProperty("X-Device-Key", settings.apiKey)
            connection.setRequestProperty("Accept", "application/json")
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json")
                connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            }
            val code = connection.responseCode
            val stream = if (code in 200..299) connection.inputStream else connection.errorStream
            val text = stream?.bufferedReader()?.use { it.readText() }.orEmpty()
            if (code !in 200..299) {
                val message = try { JSONObject(text).optString("message", "HTTP $code") } catch (_: Exception) { "HTTP $code" }
                throw IllegalStateException(message)
            }
            return if (text.isBlank()) JSONObject() else JSONObject(text)
        } finally {
            connection.disconnect()
        }
    }
}
