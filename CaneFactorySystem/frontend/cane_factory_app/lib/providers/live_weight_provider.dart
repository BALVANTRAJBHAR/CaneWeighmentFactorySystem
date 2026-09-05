import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:signalr_netcore/signalr_client.dart';
import '../core/api_client.dart';

class LiveWeight {
  final double weightKg;
  final double weightQuintal;
  final String weightUnit;
  final bool stable;
  final bool deviceConnected;
  final bool readerRunning;
  final bool isLive;
  final String readerState;
  final DateTime? lastReceivedAt;
  final String? deviceName;
  final String? error;

  LiveWeight({
    this.weightKg = 0,
    this.weightQuintal = 0,
    this.weightUnit = 'KG',
    this.stable = false,
    this.deviceConnected = false,
    this.readerRunning = false,
    this.isLive = false,
    this.readerState = 'DISCONNECTED',
    this.lastReceivedAt,
    this.deviceName,
    this.error,
  });

  factory LiveWeight.fromJson(Map<String, dynamic> j) => LiveWeight(
        weightKg: (j['weightKg'] ?? 0).toDouble(),
        weightQuintal: (j['weightQuintal'] ?? 0).toDouble(),
        weightUnit: j['weightUnit'] ?? 'KG',
        stable: j['stable'] == true,
        deviceConnected: j['deviceConnected'] == true,
        readerRunning: j['readerRunning'] == true,
        isLive: j['isLive'] == true,
        readerState: j['readerState']?.toString() ?? 'DISCONNECTED',
        lastReceivedAt: j['lastReceivedAt'] != null
            ? DateTime.tryParse(j['lastReceivedAt'])
            : null,
        deviceName: j['deviceName'],
        error: j['error'],
      );
}

/// Real-time live weight via SignalR with automatic polling fallback.
/// Live weight processing never blocks the UI thread.
class LiveWeightProvider extends ChangeNotifier {
  LiveWeight current = LiveWeight();
  HubConnection? _hub;
  Timer? _pollTimer;
  bool _polling = false;

  Future<void> start() async {
    await _connectHub();
    _pollTimer ??= Timer.periodic(const Duration(seconds: 2), (_) {
      // Poll even while SignalR is connected. This keeps the displayed value
      // current if a websocket silently stalls on a factory LAN.
      _poll();
    });
    await _poll();
  }

  Future<void> _connectHub() async {
    try {
      _hub = HubConnectionBuilder()
          .withUrl('${ApiClient.baseUrl}/hubs/weight',
              options: HttpConnectionOptions(
                  accessTokenFactory: () async =>
                      await ApiClient.instance.accessToken ?? ''))
          .withAutomaticReconnect()
          .build();
      _hub!.onreconnecting(({error}) {
        _poll();
      });
      _hub!.onreconnected(({connectionId}) {
        _poll();
      });
      _hub!.onclose(({error}) {
        _poll();
      });
      _hub!.on('liveWeight', (args) {
        if (args != null && args.isNotEmpty && args[0] is Map) {
          current =
              LiveWeight.fromJson(Map<String, dynamic>.from(args[0] as Map));
          notifyListeners();
        }
      });
      await _hub!.start();
    } catch (_) {
      // Polling fallback stays active when SignalR cannot connect.
    }
  }

  Future<void> _poll() async {
    if (_polling) return;
    _polling = true;
    try {
      final res = await ApiClient.instance.dio.get('/api/devices/live-weight');
      if (res.statusCode == 200 && res.data is Map) {
        current = LiveWeight.fromJson(Map<String, dynamic>.from(res.data));
        notifyListeners();
      }
    } catch (_) {
    } finally {
      _polling = false;
    }
  }

  Future<void> stop() async {
    _pollTimer?.cancel();
    _pollTimer = null;
    try {
      await _hub?.stop();
    } catch (_) {}
  }

  @override
  void dispose() {
    stop();
    super.dispose();
  }
}
