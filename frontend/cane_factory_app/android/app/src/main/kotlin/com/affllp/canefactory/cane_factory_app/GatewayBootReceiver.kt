package com.affllp.canefactory.cane_factory_app

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import androidx.core.content.ContextCompat

class GatewayBootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if ((intent.action == Intent.ACTION_BOOT_COMPLETED || intent.action == Intent.ACTION_MY_PACKAGE_REPLACED) &&
            GatewaySecureStore.isEnabled(context) && GatewaySecureStore.load(context) != null) {
            ContextCompat.startForegroundService(context, Intent(context, GatewayService::class.java))
        }
    }
}
