package com.affllp.canefactory.cane_factory_app

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

data class GatewaySettings(
    val serverUrl: String,
    val deviceId: String,
    val apiKey: String,
    val simSlot: String,
    val pollIntervalSeconds: Int
)

object GatewaySecureStore {
    private const val PREFS = "android_sim_gateway"
    private const val KEY_ALIAS = "cane_factory_android_gateway_key"

    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        val existing = store.getKey(KEY_ALIAS, null) as? SecretKey
        if (existing != null) return existing
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").run {
            init(KeyGenParameterSpec.Builder(KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build())
            generateKey()
        }
    }

    private fun encrypt(value: String): String {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val payload = cipher.iv + cipher.doFinal(value.toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(payload, Base64.NO_WRAP)
    }

    private fun decrypt(value: String): String {
        val payload = Base64.decode(value, Base64.NO_WRAP)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, payload.copyOfRange(0, 12)))
        return String(cipher.doFinal(payload.copyOfRange(12, payload.size)), Charsets.UTF_8)
    }

    fun save(context: Context, settings: GatewaySettings) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putString("server", encrypt(settings.serverUrl.trimEnd('/')))
            .putString("device", encrypt(settings.deviceId))
            .putString("key", encrypt(settings.apiKey))
            .putString("sim", settings.simSlot)
            .putInt("poll", settings.pollIntervalSeconds.coerceIn(2, 300))
            .apply()
    }

    fun load(context: Context): GatewaySettings? = try {
        val p = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val server = p.getString("server", null) ?: return null
        val device = p.getString("device", null) ?: return null
        val apiKey = p.getString("key", null) ?: return null
        GatewaySettings(decrypt(server), decrypt(device), decrypt(apiKey),
            p.getString("sim", "DEFAULT") ?: "DEFAULT", p.getInt("poll", 5))
    } catch (_: Exception) { null }

    fun setEnabled(context: Context, enabled: Boolean) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean("enabled", enabled).apply()

    fun isEnabled(context: Context): Boolean =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean("enabled", false)

    // A local delivery receipt closes the small crash window between Android's SENT callback and
    // the server acknowledgement. If that queue id is re-issued, it is acknowledged, not re-sent.
    fun rememberDelivered(context: Context, queueId: Int, providerId: String) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putString("delivered_$queueId", providerId).apply()

    fun deliveredProviderId(context: Context, queueId: Int): String? =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString("delivered_$queueId", null)

    fun deliveredReceipts(context: Context): Map<Int, String> =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).all.mapNotNull { (key, value) ->
            if (!key.startsWith("delivered_") || value !is String) null
            else key.removePrefix("delivered_").toIntOrNull()?.let { it to value }
        }.toMap()

    fun forgetDelivered(context: Context, queueId: Int) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().remove("delivered_$queueId").apply()
}
