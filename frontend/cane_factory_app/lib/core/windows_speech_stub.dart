/// Non-IO platforms continue to use flutter_tts through SoundController.
class WindowsSpeech {
  bool get isSupported => false;

  Future<List<String>> getVoices() async => const [];

  Future<void> speak({
    required String text,
    required bool preferHindi,
    required double volume,
    required double speechRate,
    String? voiceName,
  }) async {}

  void stop() {}
}
