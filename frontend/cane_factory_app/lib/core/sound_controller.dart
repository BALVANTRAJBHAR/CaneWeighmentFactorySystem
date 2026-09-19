import 'dart:async';
import 'package:flutter_tts/flutter_tts.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../core/api_client.dart';
import 'windows_speech.dart';

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
  SoundController._();

  /// Keep one Windows TTS channel for the whole application. Creating and
  /// stopping multiple native TTS objects while switching screens can tear
  /// down the Windows speech COM object while another callback is using it.
  static final SoundController instance = SoundController._();

  // Do not create/call the native flutter_tts channel on Windows. See
  // WindowsSpeech for the access-violation workaround.
  FlutterTts? _tts;
  FlutterTts get _nativeTts => _tts ??= FlutterTts();
  final WindowsSpeech _windowsSpeech = WindowsSpeech();
  Future<void> _nativeQueue = Future<void>.value();
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
  Future<void>? _voiceFuture;
  List<String> _windowsVoices = const [];
  List<String> _windowsHindiVoices = const [];
  String? _windowsVoiceName;
  bool _windowsHasHindiVoice = false;

  List<String> get windowsVoices => List.unmodifiable(_windowsVoices);
  List<String> get windowsHindiVoices =>
      List.unmodifiable(_windowsHindiVoices);
  String? get windowsVoiceName => _windowsVoiceName;
  bool get windowsHasHindiVoice => _windowsHasHindiVoice;

  /// Voices are workstation-specific, so this preference belongs to the
  /// local client rather than the shared server configuration.
  Future<void> loadWindowsVoices({bool refresh = false}) {
    if (refresh) _voiceFuture = null;
    return _voiceFuture ??= _loadWindowsVoices();
  }

  Future<void> _loadWindowsVoices() async {
    if (!_windowsSpeech.isSupported) return;
    final prefs = await SharedPreferences.getInstance();
    _windowsVoiceName = prefs.getString('windows_speech_voice');
    final result = await Future.wait([
      _windowsSpeech.getVoices(),
      _windowsSpeech.getHindiVoices(),
    ]);
    _windowsVoices = result[0];
    _windowsHindiVoices = result[1];
    _windowsHasHindiVoice = _windowsHindiVoices.isNotEmpty;
    if (_windowsVoiceName != null &&
        !_windowsVoices.contains(_windowsVoiceName)) {
      _windowsVoiceName = null;
      await prefs.remove('windows_speech_voice');
    }
  }

  Future<void> setWindowsVoice(String? voiceName) async {
    _windowsVoiceName = voiceName?.trim().isEmpty == true ? null : voiceName;
    final prefs = await SharedPreferences.getInstance();
    if (_windowsVoiceName == null) {
      await prefs.remove('windows_speech_voice');
    } else {
      await prefs.setString('windows_speech_voice', _windowsVoiceName!);
    }
  }

  Future<void> testVoice() async {
    await loadConfig();
    await loadWindowsVoices();
    final text = _eventMessage('WEIGHING_ACTIVE') ?? 'Sound test is working.';
    await _speakText(text);
  }

  Future<void> _runNative(Future<void> Function() operation) {
    final next = _nativeQueue.then((_) => operation()).catchError((_) {});
    _nativeQueue = next;
    return next;
  }

  Future<void> loadConfig() => _configFuture ??= _loadConfig();

  Future<void> reloadConfig() {
    _configFuture = null;
    return loadConfig();
  }

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

      // On Windows, speak() only completes after the spoken phrase when this
      // is enabled. Without it a failed/missing completion callback could
      // leave `_speaking` true forever and suppress every later announcement.
      try {
        if (_windowsSpeech.isSupported) {
          await loadWindowsVoices();
          return;
        }

        await _nativeTts.awaitSpeakCompletion(true);
      } catch (_) {
        // Some platforms do not expose this capability; speaking still works.
      }

      try {
        await _nativeTts.setLanguage(language == 'hi' ? 'hi-IN' : 'en-US');
      } catch (_) {
        // Keep an audible fallback when the selected Windows voice is absent.
        language = 'en';
        await _nativeTts.setLanguage('en-US');
      }
      await _nativeTts.setVolume(volume);
      await _nativeTts.setSpeechRate(speechRate);
    } catch (_) {
      // The screen must remain usable if the API is temporarily unavailable;
      // the next screen instance will load the configuration again.
    }
  }

  /// Feed live weight updates; drives the state machine.
  Future<void> onWeight(double weightQuintal) async {
    // Do not consume the first vehicle transition before sound messages have
    // arrived from Configuration. This was the reason the configured vehicle
    // and weighment prompts could remain silent until the next state change.
    await loadConfig();
    if (!enabled) return;
    // After a successful saved weight, the vehicle-removal instruction must
    // continue until a real zero platform reading arrives. It must never be
    // cleared by a timer, app route change, or digitizer reconnect.
    if (state == SoundState.completed) {
      if (weightQuintal < 0.5) _setState(SoundState.noVehicle);
      return;
    }
    if (weightQuintal < 0.5) {
      _setState(SoundState.noVehicle);
    } else if (rulesEnabled && weightQuintal < minimumWeightQuintal) {
      _setState(SoundState.belowMinimum);
    } else {
      _setState(SoundState.weighingInProgress);
    }
  }

  Future<void> onWeighmentSaved() async {
    await loadConfig();
    // Repeat the configured "move vehicle off" announcement until onWeight
    // receives a zero-platform reading. This is intentionally not a timer.
    _setState(SoundState.completed);
  }

  /// Plays a configured one-time event after an asynchronous operation such as
  /// camera evidence capture has actually completed.
  Future<void> playConfiguredEvent(String event) async {
    await loadConfig();
    if (!enabled) return;
    for (var i = 0; i < 50 && _speaking; i++) {
      await Future.delayed(const Duration(milliseconds: 100));
    }
    // Image-captured is a one-time confirmation. Do not accidentally cancel
    // the mandatory vehicle-removal instruction after a saved weighment.
    final resumeVehicleRemoval = state == SoundState.completed;
    _stopRepeat();
    _speaking = false;
    if (repeatMode != 'OFF') await _speakEvent(event);
    if (resumeVehicleRemoval && state == SoundState.completed) {
      _startEvent('WEIGHMENT_COMPLETED');
    }
  }

  /// Changing Gross/Tare mode or leaving the screen stops any repeating message.
  void reset() {
    _stopRepeat();
    // Serialize stop with speak. Calling Windows TTS methods concurrently can
    // terminate the native plugin without producing a Dart exception.
    if (_windowsSpeech.isSupported) {
      _windowsSpeech.stop();
    } else if (_tts != null) {
      unawaited(_runNative(() async {
        await _nativeTts.stop();
      }));
    }
    _speaking = false;
    state = SoundState.noVehicle;
  }

  void _setState(SoundState s) {
    // On entering a screen with an already-empty platform, state starts as
    // noVehicle. Start the prompt even though the enum value is unchanged.
    if (state == s && _activeEvent != null) return;
    state = s;
    _stopRepeat();
    switch (s) {
      case SoundState.noVehicle:
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
    final text = _eventMessage(event);
    if (text == null) return;
    _speaking = true;
    _playCount++;
    try {
      await _speakText(text);
    } catch (_) {
      // A temporary platform TTS error must not mute all later messages.
    } finally {
      _speaking = false;
    }
  }

  Future<void> _speakText(String text) async {
    if (_windowsSpeech.isSupported) {
      await loadWindowsVoices();
      await _windowsSpeech.speak(
        text: text,
        preferHindi: language == 'hi' && _windowsHasHindiVoice,
        volume: volume,
        speechRate: speechRate,
        voiceName: _windowsVoiceName,
      );
      return;
    }
    await _runNative(() async {
      await _nativeTts.speak(text);
    });
  }

  /// If no Hindi Windows voice exists, use the already-configured English
  /// equivalent. This is preferable to silently sending Hindi Unicode to an
  /// incompatible English-only SAPI voice.
  String? _eventMessage(String event) {
    final mustUseEnglishFallback = _windowsSpeech.isSupported &&
        language == 'hi' &&
        !_windowsHasHindiVoice;
    if (mustUseEnglishFallback) {
      return messages['$event|en'] ?? messages['$event|hi'];
    }
    return messages['$event|$language'] ??
        messages['$event|en'] ??
        messages['$event|hi'];
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
