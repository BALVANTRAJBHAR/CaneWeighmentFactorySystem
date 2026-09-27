package com.affllp.canefactory.cane_factory_app

import android.Manifest
import android.app.Activity
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.os.Build
import android.os.IBinder
import android.telephony.SmsManager
import android.telephony.SubscriptionManager
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

class GatewayService : Service() {
    companion object {
        private const val CHANNEL_ID = "android_sim_gateway"
        private const val NOTIFICATION_ID = 7310
        const val ACTION_STOP = "com.affllp.canefactory.GATEWAY_STOP"
    }

    private val executor = Executors.newSingleThreadExecutor()
    private val running = AtomicBoolean(false)

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        startForeground(NOTIFICATION_ID, notification("Starting Android SIM Gateway…"))
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            GatewaySecureStore.setEnabled(this, false)
            stopSelf()
            return START_NOT_STICKY
        }
        if (!GatewaySecureStore.isEnabled(this)) {
            stopSelf()
            return START_NOT_STICKY
        }
        if (running.compareAndSet(false, true)) executor.submit(::runLoop)
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        running.set(false)
        executor.shutdownNow()
        super.onDestroy()
    }

    private fun runLoop() {
        var lastHeartbeat = 0L
        while (running.get() && GatewaySecureStore.isEnabled(this)) {
            val settings = GatewaySecureStore.load(this)
            if (settings == null) {
                updateNotification("Gateway is not configured")
                break
            }
            try {
                require(ContextCompat.checkSelfPermission(this, Manifest.permission.SEND_SMS) == PackageManager.PERMISSION_GRANTED) {
                    "SEND_SMS permission is not granted."
                }
                val client = GatewayHttpClient(settings)
                val now = System.currentTimeMillis()
                for ((queueId, providerId) in GatewaySecureStore.deliveredReceipts(this)) {
                    try {
                        client.sent(queueId, providerId)
                        GatewaySecureStore.forgetDelivered(this, queueId)
                    } catch (_: Exception) { /* retry acknowledgement without re-sending */ }
                }
                if (now - lastHeartbeat >= 25_000) {
                    val heartbeat = client.heartbeat()
                    lastHeartbeat = now
                    if (!heartbeat.optBoolean("active", false)) {
                        updateNotification("Connected • SMS sending disabled by server")
                        sleep(settings.pollIntervalSeconds)
                        continue
                    }
                }
                val messages = client.pending()
                updateNotification("Connected • ${messages.size} pending")
                for (message in messages) {
                    if (!running.get() || !GatewaySecureStore.isEnabled(this)) break
                    try {
                        client.processing(message.id)
                    } catch (_: Exception) {
                        continue // another poller/device claim won the atomic transition
                    }
                    val priorDelivery = GatewaySecureStore.deliveredProviderId(this, message.id)
                    if (priorDelivery != null) {
                        try {
                            client.sent(message.id, priorDelivery)
                            GatewaySecureStore.forgetDelivered(this, message.id)
                        } catch (_: Exception) { /* keep receipt and acknowledge on a later poll */ }
                        continue
                    }
                    try {
                        val providerId = sendSms(message)
                        GatewaySecureStore.rememberDelivered(this, message.id, providerId)
                        try {
                            client.sent(message.id, providerId)
                            GatewaySecureStore.forgetDelivered(this, message.id)
                        } catch (_: Exception) {
                            // Do not report send failure after SmsManager already confirmed delivery to
                            // the carrier. The local receipt prevents this Queue Id being sent twice.
                        }
                    } catch (ex: Exception) {
                        try { client.failed(message.id, safeMessage(ex)) } catch (_: Exception) { }
                    }
                }
            } catch (ex: Exception) {
                updateNotification("Offline • ${safeMessage(ex)}")
            }
            sleep(settings.pollIntervalSeconds)
        }
        stopSelf()
    }

    private fun sleep(seconds: Int) {
        try { Thread.sleep(seconds.coerceIn(2, 300) * 1000L) } catch (_: InterruptedException) { }
    }

    private fun sendSms(item: PendingSms): String {
        require(Regex("^\\+91[6-9][0-9]{9}$").matches(item.mobileNumber)) { "Invalid Indian mobile number." }
        val manager = smsManager(item.simSlot)
        val parts = manager.divideMessage(item.message)
        require(parts.isNotEmpty()) { "SMS message is empty." }

        val result = AtomicReference<String?>(null)
        val latch = CountDownLatch(parts.size)
        val sentIntents = ArrayList<PendingIntent>(parts.size)
        val filter = IntentFilter()
        val actions = mutableSetOf<String>()
        for (index in parts.indices) {
            val action = "$packageName.GATEWAY_SMS_SENT.${item.id}.$index.${System.nanoTime()}"
            actions += action
            filter.addAction(action)
            sentIntents += PendingIntent.getBroadcast(this, item.id * 100 + index,
                Intent(action).setPackage(packageName), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        }
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context?, intent: Intent?) {
                val action = intent?.action ?: return
                if (!actions.contains(action)) return
                if (resultCode != Activity.RESULT_OK) result.compareAndSet(null, smsFailure(resultCode))
                latch.countDown()
            }
        }
        if (Build.VERSION.SDK_INT >= 33) registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED)
        else {
            @Suppress("DEPRECATION")
            registerReceiver(receiver, filter)
        }
        try {
            manager.sendMultipartTextMessage(item.mobileNumber, null, parts, sentIntents, null)
            if (!latch.await(90, TimeUnit.SECONDS)) throw IllegalStateException("SmsManager send confirmation timed out.")
            result.get()?.let { throw IllegalStateException(it) }
            return "ANDROID-${item.id}-${UUID.randomUUID()}"
        } finally {
            try { unregisterReceiver(receiver) } catch (_: Exception) { }
        }
    }

    @Suppress("DEPRECATION")
    private fun smsManager(simSlot: String): SmsManager {
        if (simSlot == "DEFAULT") return SmsManager.getDefault()
        require(ContextCompat.checkSelfPermission(this, Manifest.permission.READ_PHONE_STATE) == PackageManager.PERMISSION_GRANTED) {
            "READ_PHONE_STATE permission is required for SIM selection."
        }
        val subscriptions = (getSystemService(Context.TELEPHONY_SUBSCRIPTION_SERVICE) as SubscriptionManager)
            .activeSubscriptionInfoList.orEmpty().sortedBy { it.simSlotIndex }
        val index = if (simSlot == "SIM2") 1 else 0
        require(index < subscriptions.size) { "$simSlot is not active on this phone." }
        return SmsManager.getSmsManagerForSubscriptionId(subscriptions[index].subscriptionId)
    }

    private fun smsFailure(code: Int): String = when (code) {
        SmsManager.RESULT_ERROR_GENERIC_FAILURE -> "Generic modem/SIM failure."
        SmsManager.RESULT_ERROR_NO_SERVICE -> "No mobile network service."
        SmsManager.RESULT_ERROR_NULL_PDU -> "SMS PDU creation failed."
        SmsManager.RESULT_ERROR_RADIO_OFF -> "Phone radio is off."
        else -> "Android SmsManager failed with result code $code."
    }

    private fun safeMessage(ex: Exception): String =
        (ex.message ?: ex.javaClass.simpleName).replace(Regex("[\\r\\n]+"), " ").take(180)

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(CHANNEL_ID, "Android SIM Gateway", NotificationManager.IMPORTANCE_LOW)
            channel.description = "Keeps the factory Android SIM SMS gateway connected"
            getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
        }
    }

    private fun notification(text: String) = NotificationCompat.Builder(this, CHANNEL_ID)
        .setSmallIcon(android.R.drawable.stat_notify_chat)
        .setContentTitle("CaneFactory SIM Gateway")
        .setContentText(text)
        .setOngoing(true)
        .setOnlyAlertOnce(true)
        .build()

    private fun updateNotification(text: String) =
        getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, notification(text))
}
