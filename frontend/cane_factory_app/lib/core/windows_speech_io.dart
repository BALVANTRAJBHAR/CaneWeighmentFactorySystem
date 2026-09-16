import 'dart:convert';
import 'dart:io';

/// Windows-only speech fallback. It intentionally uses the OS speech service
/// in a child process instead of flutter_tts_plugin.dll, which has caused
/// native access violations while a weighment screen is disposed.
class WindowsSpeech {
  Process? _activeProcess;

  bool get isSupported => Platform.isWindows;

  Future<List<String>> getVoices() async {
    if (!isSupported) return const [];
    try {
      final result = await Process.run('powershell.exe', [
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-Command',
        'Add-Type -AssemblyName System.Speech; '
            '(New-Object System.Speech.Synthesis.SpeechSynthesizer).GetInstalledVoices() '
            '| ForEach-Object { \$_.VoiceInfo.Name }',
      ]);
      if (result.exitCode != 0) return const [];
      return result.stdout
          .toString()
          .split(RegExp(r'\r?\n'))
          .map((value) => value.trim())
          .where((value) => value.isNotEmpty)
          .toSet()
          .toList();
    } catch (_) {
      return const [];
    }
  }

  Future<void> speak({
    required String text,
    required bool preferHindi,
    required double volume,
    required double speechRate,
    String? voiceName,
  }) async {
    if (!isSupported || text.trim().isEmpty) return;

    stop();
    final encodedText = base64Encode(utf8.encode(text));
    final encodedVoice = base64Encode(utf8.encode(voiceName ?? ''));
    final safeVolume = (volume.clamp(0.0, 1.0) * 100).round();
    final safeRate =
        ((speechRate.clamp(0.1, 1.0) * 10) - 5).round().clamp(-10, 10);
    final script = '''
Add-Type -AssemblyName System.Speech
\$text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('$encodedText'))
\$voiceName = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('$encodedVoice'))
\$speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
\$speaker.Volume = $safeVolume
\$speaker.Rate = $safeRate
if (-not [string]::IsNullOrWhiteSpace(\$voiceName) -and (\$speaker.GetInstalledVoices() | Where-Object { \$_.VoiceInfo.Name -eq \$voiceName })) {
  \$speaker.SelectVoice(\$voiceName)
} elseif (${preferHindi ? 'true' : 'false'}) {
  \$voice = \$speaker.GetInstalledVoices() | Where-Object { \$_.VoiceInfo.Culture.Name -eq 'hi-IN' } | Select-Object -First 1
  if (\$null -ne \$voice) { \$speaker.SelectVoice(\$voice.VoiceInfo.Name) }
}
\$speaker.Speak(\$text)
\$speaker.Dispose()
''';

    final process = await Process.start(
      'powershell.exe',
      ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', script],
      runInShell: false,
    );
    _activeProcess = process;
    try {
      await process.exitCode;
    } finally {
      if (identical(_activeProcess, process)) _activeProcess = null;
    }
  }

  void stop() {
    final process = _activeProcess;
    _activeProcess = null;
    if (process != null && !process.kill(ProcessSignal.sigterm)) {
      process.kill(ProcessSignal.sigkill);
    }
  }
}
