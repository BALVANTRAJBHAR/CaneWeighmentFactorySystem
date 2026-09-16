import 'dart:async';
import 'package:flutter_tts/flutter_tts.dart';
import '../core/api_client.dart';

/// Weighment sound state machine per specification.
/// States: NO_VEHICLE_WEIGHT -> BELOW_MINIMUM -> ABOVE_MINIMUM_WAITING ->
/// WEIGHING_IN_PROGRESS -> WEIGHMENT_COMPLETED.
/// Uses local/offline system TTS. Voice never overlaps and never blocks
/// live weight, camera, save or print operations.
enum SoundState {
  noVehicle,
  belowMinimum,
  aboveMinimumWaiting,
  weighingInProgress,
  completed
}

class SoundController {
  final FlutterTts _tts = FlutterTts();
  SoundState state = SoundState.noVehicle;

  bool enabled = true;
  String language = 'hi';
  double volume = 1.0;
  double speechRate = 0.5;
  int repeatIntervalSeconds = 10;
  String repeatMode = 'CONTINUOUS';
  Map<String, String> messages = {}; // "EVENT|lang" -> text
  double minimumWeightQuintal = 10.0;
  bool rulesEnabled = true;

  Timer? _repeatTimer;
  int _playCount = 0;
  bool _speaking = false;
  String? _activeEvent;
  Future<void>? _configFuture;

  Future<void> loadConfig() => _configFuture ??= _loadConfig();

  Future<void> _loadConfig() async {
    try {
      final res = await ApiClient.instance.dio.get('/api/weighment/rules');
      if (res.statusCode == 200) {
        final sc = res.data['soundConfig'];
        if (sc != null) {
          enabled = sc['soundEnabled'] == true;
          language = sc['language'] ?? 'hi';
          volume = ((sc['voiceVolume'] ?? 100) as num) / 100.0;
          speechRate = ((sc['speechRate'] ?? 1.0) as num).toDouble() * 0.5;
          repeatIntervalSeconds = sc['repeatIntervalSeconds'] ?? 10;
          repeatMode = sc['repeatMode'] ?? 'CONTINUOUS';
        }
        final rules = res.data['weightRules'];
        if (rules != null) {
          minimumWeightQuintal =
              ((rules['minimumWeightQuintal'] ?? 10) as num).toDouble();
          rulesEnabled = rules['enabled'] == true;
        }
        for (final m in (res.data['soundMessages'] as List? ?? [])) {
          messages['${m['eventCode']}|${m['languageCode']}'] = m['messageText'];
        }
      }
      await _tts.setLanguage(language == 'hi' ? 'hi-IN' : 'en-US');
      await _tts.setVolume(volume);
      await _tts.setSpeechRate(speechRate);
      _tts.setCompletionHandler(() => _speaking = false);
    } catch (_) {}
  }

  /// Feed live weight updates; drives the state machine.
  void onWeight(double weightQuintal) {
    if (!enabled || !rulesEnabled) return;
    if (state == SoundState.completed) return; // wait for reset/new vehicle
    if (weightQuintal < 0.5) {
      _setState(SoundState.noVehicle);
    } else if (weightQuintal < minimumWeightQuintal) {
      _setState(SoundState.belowMinimum);
    } else {
      _setState(SoundState.weighingInProgress);
    }
  }

  Future<void> onWeighmentSaved() async {
    await loadConfig();
    _setState(SoundState.completed);
    Timer(const Duration(seconds: 8), () {
      if (state == SoundState.completed) _setState(SoundState.noVehicle);
    });
  }

  /// Plays a configured one-time event after an asynchronous operation such as
  /// camera evidence capture has actually completed.
  Future<void> playConfiguredEvent(String event) async {
    await loadConfig();
    if (!enabled) return;
    for (var i = 0; i < 50 && _speaking; i++) {
      await Future.delayed(const Duration(milliseconds: 100));
    }
    _stopRepeat();
    _speaking = false;
    _startEvent(event, overrideMode: repeatMode == 'OFF' ? 'OFF' : 'ONCE');
  }

  /// Changing Gross/Tare mode or leaving the screen stops any repeating message.
  void reset() {
    _stopRepeat();
    _tts.stop();
    _speaking = false;
    state = SoundState.noVehicle;
  }

  void _setState(SoundState s) {
    if (state == s) return;
    state = s;
    _stopRepeat();
    switch (s) {
      case SoundState.belowMinimum:
        _startEvent('BELOW_MINIMUM');
        break;
      case SoundState.weighingInProgress:
        _startEvent('WEIGHING_ACTIVE');
        break;
      case SoundState.completed:
        _startEvent('WEIGHMENT_COMPLETED',
            overrideMode: repeatMode == 'OFF' ? 'OFF' : 'ONCE');
        break;
      default:
        break;
    }
  }

  void _startEvent(String event, {String? overrideMode}) {
    final mode = overrideMode ?? repeatMode;
    if (mode == 'OFF') return;
    _activeEvent = event;
    _playCount = 0;
    _speakEvent(event);
    final maxPlays = mode == 'ONCE' ? 1 : (mode == 'TWICE' ? 2 : -1);
    if (maxPlays == 1) return;
    _repeatTimer =
        Timer.periodic(Duration(seconds: repeatIntervalSeconds), (t) {
      if (maxPlays > 0 && _playCount >= maxPlays) {
        t.cancel();
        return;
      }
      if (_activeEvent == event) _speakEvent(event);
    });
  }

  Future<void> _speakEvent(String event) async {
    if (_speaking) return; // no overlapping playback
    final text = messages['$event|$language'] ?? messages['$event|hi'];
    if (text == null) return;
    _speaking = true;
    _playCount++;
    try {
      await _tts.speak(text);
    } catch (_) {
      _speaking = false;
    }
  }

  void _stopRepeat() {
    _repeatTimer?.cancel();
    _repeatTimer = null;
    _activeEvent = null;
  }

  void dispose() {
    reset();
  }
}
