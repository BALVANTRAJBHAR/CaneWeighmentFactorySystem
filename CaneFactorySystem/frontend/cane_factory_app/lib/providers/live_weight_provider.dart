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
  final DateTime? lastReceivedAt;
  final String? deviceName;
  final String? error;

  LiveWeight({
    this.weightKg = 0,
    this.weightQuintal = 0,
    this.weightUnit = 'KG',
    this.stable = false,
    this.deviceConnected = false,
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
        lastReceivedAt: j['lastReceivedAt'] != null ? DateTime.tryParse(j['lastReceivedAt']) : null,
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
  bool _hubConnected = false;

  Future<void> start() async {
    await _connectHub();
    _pollTimer ??= Timer.periodic(const Duration(seconds: 2), (_) {
      if (!_hubConnected) _poll();
    });
  }

  Future<void> _connectHub() async {
    try {
      final token = await ApiClient.instance.accessToken;
      _hub = HubConnectionBuilder()
          .withUrl('${ApiClient.baseUrl}/hubs/weight',
              options: HttpConnectionOptions(accessTokenFactory: () async => token ?? ''))
          .withAutomaticReconnect()
          .build();
      _hub!.on('liveWeight', (args) {
        if (args != null && args.isNotEmpty && args[0] is Map) {
          current = LiveWeight.fromJson(Map<String, dynamic>.from(args[0] as Map));
          notifyListeners();
        }
      });
      await _hub!.start();
      _hubConnected = true;
    } catch (_) {
      _hubConnected = false; // polling fallback stays active
    }
  }

  Future<void> _poll() async {
    try {
      final res = await ApiClient.instance.dio.get('/api/devices/live-weight');
      if (res.statusCode == 200 && res.data is Map) {
        current = LiveWeight.fromJson(Map<String, dynamic>.from(res.data));
        notifyListeners();
      }
    } catch (_) {}
  }

  Future<void> stop() async {
    _pollTimer?.cancel();
    _pollTimer = null;
    try {
      await _hub?.stop();
    } catch (_) {}
    _hubConnected = false;
  }

  @override
  void dispose() {
    stop();
    super.dispose();
  }
}
