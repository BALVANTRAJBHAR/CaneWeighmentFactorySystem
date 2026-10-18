import 'dart:async';
import 'package:dio/dio.dart';
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
  final bool canSaveWeighment;
  final bool weightSaveRestrictedToBridgeHost;
  final bool liveWeightOnlyOnDigitizerPc;
  final DateTime? scaleReadingCapturedAtUtc;
  final String? scaleBridgeSessionId;
  final int scaleBridgeZeroCounter;
  final String? scaleReadingNonce;
  final String? scaleReadingSignature;

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
    this.canSaveWeighment = false,
    this.weightSaveRestrictedToBridgeHost = false,
    this.liveWeightOnlyOnDigitizerPc = false,
    this.scaleReadingCapturedAtUtc,
    this.scaleBridgeSessionId,
    this.scaleBridgeZeroCounter = 0,
    this.scaleReadingNonce,
    this.scaleReadingSignature,
  });

  factory LiveWeight.fromJson(Map<String, dynamic> j, {LiveWeight? previous}) =>
      LiveWeight(
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
        // SignalR broadcasts the physical reading to every PC and therefore
        // cannot carry a requester-specific save permission. Preserve the last
        // HTTP-poll decision when these fields are absent, instead of falling
        // back to true and making the Save button blink.
        canSaveWeighment: j.containsKey('canSaveWeighment')
            ? j['canSaveWeighment'] == true
            : previous?.canSaveWeighment ?? false,
        weightSaveRestrictedToBridgeHost:
            j.containsKey('weightSaveRestrictedToBridgeHost')
                ? j['weightSaveRestrictedToBridgeHost'] == true
                : previous?.weightSaveRestrictedToBridgeHost ?? false,
        liveWeightOnlyOnDigitizerPc:
            j.containsKey('liveWeightOnlyOnDigitizerPc')
                ? j['liveWeightOnlyOnDigitizerPc'] == true
                : previous?.liveWeightOnlyOnDigitizerPc ?? false,
        scaleReadingCapturedAtUtc: j['scaleReadingCapturedAtUtc'] != null
            ? DateTime.tryParse(j['scaleReadingCapturedAtUtc'].toString())
            : null,
        scaleBridgeSessionId: j['scaleBridgeSessionId']?.toString(),
        scaleBridgeZeroCounter:
            (j['scaleBridgeZeroCounter'] as num?)?.toInt() ?? 0,
        scaleReadingNonce: j['scaleReadingNonce']?.toString(),
        scaleReadingSignature: j['scaleReadingSignature']?.toString(),
      );

  Map<String, dynamic> get saveProof => {
        'scaleReadingKg': weightKg,
        'scaleReadingStable': stable,
        'scaleReadingCapturedAtUtc':
            (scaleReadingCapturedAtUtc ?? lastReceivedAt)
                ?.toUtc()
                .toIso8601String(),
        'scaleBridgeSessionId': scaleBridgeSessionId,
        'scaleBridgeZeroCounter': scaleBridgeZeroCounter,
        'scaleReadingNonce': scaleReadingNonce,
        'scaleReadingSignature': scaleReadingSignature,
      };
}

/// Real-time live weight via SignalR with automatic polling fallback.
/// Live weight processing never blocks the UI thread.
class LiveWeightProvider extends ChangeNotifier {
  LiveWeight current = LiveWeight();
  HubConnection? _hub;
  Timer? _pollTimer;
  Timer? _localPollTimer;
  bool _polling = false;
  bool _localPolling = false;
  Future<void>? _startFuture;
  bool _stopped = false;

  /// Idempotent for the lifetime of the app. Multiple screens consume the
  /// same provider, so opening Weighment repeatedly must never create another
  /// SignalR socket or another polling loop.
  Future<void> start() => _startFuture ??= _start();

  Future<void> _start() async {
    _stopped = false;
    // Start the safety-net timer before the first request. Previously, one
    // transient 401/network error while a screen was opening left the shared
    // provider permanently in its default DISCONNECTED state because no
    // retry timer had been created yet.
    _scheduleServerPolling(const Duration(seconds: 2));
    await _pollServer();
  }

  void _scheduleServerPolling(Duration interval) {
    _pollTimer?.cancel();
    if (_stopped) {
      _pollTimer = null;
      return;
    }
    _pollTimer = Timer.periodic(interval, (_) => _pollServer());
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
        if (!_stopped && identical(_hub, hub)) _pollServer();
      });
      hub.onreconnected(({connectionId}) {
        if (!_stopped && identical(_hub, hub)) _pollServer();
      });
      hub.onclose(({error}) {
        if (!_stopped && identical(_hub, hub)) _pollServer();
      });
      hub.on('liveWeight', (args) {
        if (!_stopped &&
            identical(_hub, hub) &&
            args != null &&
            args.isNotEmpty &&
            args[0] is Map) {
          current = LiveWeight.fromJson(
              Map<String, dynamic>.from(args[0] as Map),
              previous: current);
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

  Future<void> _pollServer() async {
    if (_polling || _stopped) return;
    _polling = true;
    try {
      final res = await ApiClient.instance.dio.get('/api/devices/live-weight');
      if (res.statusCode == 200 && res.data is Map) {
        final next = LiveWeight.fromJson(Map<String, dynamic>.from(res.data));
        if (next.liveWeightOnlyOnDigitizerPc) {
          current = next;
          notifyListeners();
          await _enterLocalOnlyMode();
        } else {
          current = next;
          notifyListeners();
          await _enterServerStreamingMode();
        }
      }
    } catch (error) {
      // Keep a valid last reading during a brief reconnect. If no reading has
      // ever arrived, expose the transport problem instead of silently showing
      // a generic disconnected state forever.
      if (current.lastReceivedAt == null && !current.isLive) {
        current = LiveWeight(
          readerState: 'SERVER_RECONNECTING',
          error: 'Live-weight connection failed; retrying automatically.',
          canSaveWeighment: false,
          weightSaveRestrictedToBridgeHost:
              current.weightSaveRestrictedToBridgeHost,
          liveWeightOnlyOnDigitizerPc: current.liveWeightOnlyOnDigitizerPc,
        );
        notifyListeners();
      }
    } finally {
      _polling = false;
      // Defensive recovery for old/stale provider state or any future branch
      // that exits before selecting a transport mode.
      if (!_stopped && _pollTimer == null) {
        _scheduleServerPolling(const Duration(seconds: 2));
      }
    }
  }

  Future<void> _enterLocalOnlyMode() async {
    _scheduleServerPolling(const Duration(seconds: 30));
    final hub = _hub;
    _hub = null;
    try {
      await hub?.stop();
    } catch (_) {}
    if (!kIsWeb && defaultTargetPlatform == TargetPlatform.windows) {
      _localPollTimer ??= Timer.periodic(
          const Duration(milliseconds: 500), (_) => _pollLocal());
      await _pollLocal();
    }
  }

  Future<void> _enterServerStreamingMode() async {
    _localPollTimer?.cancel();
    _localPollTimer = null;
    _scheduleServerPolling(const Duration(seconds: 2));
    await _connectHub();
  }

  Future<void> _pollLocal() async {
    if (_localPolling || _stopped) return;
    _localPolling = true;
    try {
      final res = await Dio(BaseOptions(
        baseUrl: 'http://127.0.0.1:5158',
        connectTimeout: const Duration(milliseconds: 1500),
        receiveTimeout: const Duration(milliseconds: 1500),
        validateStatus: (status) => status != null && status < 500,
      )).get('/live-weight');
      if (res.statusCode == 200 && res.data is Map) {
        current = LiveWeight.fromJson(Map<String, dynamic>.from(res.data));
        notifyListeners();
      }
    } catch (_) {
      current = LiveWeight(
        readerState: 'LOCAL_BRIDGE_UNAVAILABLE',
        error: 'Local Scale Bridge is not available on this PC.',
        canSaveWeighment: false,
        weightSaveRestrictedToBridgeHost: true,
        liveWeightOnlyOnDigitizerPc: true,
      );
      notifyListeners();
    } finally {
      _localPolling = false;
    }
  }

  Future<void> stop() async {
    _stopped = true;
    _pollTimer?.cancel();
    _pollTimer = null;
    _localPollTimer?.cancel();
    _localPollTimer = null;
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
