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
  Future<void>? _startFuture;
  bool _stopped = false;

  /// Idempotent for the lifetime of the app. Multiple screens consume the
  /// same provider, so opening Weighment repeatedly must never create another
  /// SignalR socket or another polling loop.
  Future<void> start() => _startFuture ??= _start();

  Future<void> _start() async {
    _stopped = false;
    await _connectHub();
    _pollTimer ??= Timer.periodic(const Duration(seconds: 2), (_) {
      // Poll even while SignalR is connected. This keeps the displayed value
      // current if a websocket silently stalls on a factory LAN.
      _poll();
    });
    await _poll();
  }

  Future<void> _connectHub() async {
    if (_hub != null || _stopped) return;
    try {
      final hub = HubConnectionBuilder()
          .withUrl('${ApiClient.baseUrl}/hubs/weight',
              options: HttpConnectionOptions(
                  accessTokenFactory: () async =>
                      await ApiClient.instance.accessToken ?? ''))
          .withAutomaticReconnect()
          .build();
      _hub = hub;
      hub.onreconnecting(({error}) {
        if (!_stopped && identical(_hub, hub)) _poll();
      });
      hub.onreconnected(({connectionId}) {
        if (!_stopped && identical(_hub, hub)) _poll();
      });
      hub.onclose(({error}) {
        if (!_stopped && identical(_hub, hub)) _poll();
      });
      hub.on('liveWeight', (args) {
        if (!_stopped &&
            identical(_hub, hub) &&
            args != null &&
            args.isNotEmpty &&
            args[0] is Map) {
          current =
              LiveWeight.fromJson(Map<String, dynamic>.from(args[0] as Map));
          notifyListeners();
        }
      });
      await hub.start();
    } catch (_) {
      // A failed connection must not leave a stale hub instance behind.  The
      // next controlled retry/navigation can then establish a fresh session.
      _hub = null;
      // Polling fallback stays active when SignalR cannot connect.
    }
  }

  Future<void> _poll() async {
    if (_polling || _stopped) return;
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
    _stopped = true;
    _pollTimer?.cancel();
    _pollTimer = null;
    final hub = _hub;
    _hub = null;
    _startFuture = null;
    try {
      await hub?.stop();
    } catch (_) {}
  }

  @override
  void dispose() {
    stop();
    super.dispose();
  }
}
