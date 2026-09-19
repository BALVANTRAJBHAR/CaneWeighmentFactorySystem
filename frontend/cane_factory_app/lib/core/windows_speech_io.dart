import 'dart:async';
import 'dart:convert';
import 'dart:io';

/// Windows-only speech service. It avoids flutter_tts_plugin.dll (which caused
/// native access violations) and supports both legacy SAPI voices and modern
/// Windows OneCore voices installed from Language & region.
class WindowsSpeech {
  Process? _activeProcess;

  bool get isSupported => Platform.isWindows;

  static const _baseArguments = <String>[
    '-NoLogo',
    '-NoProfile',
    '-NonInteractive',
    '-WindowStyle',
    'Hidden',
    '-Command',
  ];

  Future<ProcessResult> _runPowerShell(String command) =>
      Process.run('powershell.exe', [..._baseArguments, command]);

  Future<List<String>> getVoices() async {
    if (!isSupported) return const [];
    try {
      final result = await _runPowerShell(r'''
$ErrorActionPreference = 'Stop'
$names = [System.Collections.Generic.List[string]]::new()
try {
  Add-Type -AssemblyName System.Speech
  $speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
  foreach ($voice in $speaker.GetInstalledVoices()) {
    if ($voice.Enabled) { $names.Add($voice.VoiceInfo.Name) }
  }
  $speaker.Dispose()
} catch {}
try {
  $null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]
  foreach ($voice in [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices) {
    $names.Add($voice.DisplayName)
  }
} catch {}
$names | Sort-Object -Unique
''');
      if (result.exitCode != 0) return const [];
      return _lines(result.stdout);
    } catch (_) {
      return const [];
    }
  }

  Future<List<String>> getHindiVoices() async {
    if (!isSupported) return const [];
    try {
      final result = await _runPowerShell(r'''
$ErrorActionPreference = 'Stop'
$names = [System.Collections.Generic.List[string]]::new()
try {
  Add-Type -AssemblyName System.Speech
  $speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
  foreach ($voice in $speaker.GetInstalledVoices()) {
    if ($voice.Enabled -and $voice.VoiceInfo.Culture.Name -like 'hi-*') {
      $names.Add($voice.VoiceInfo.Name)
    }
  }
  $speaker.Dispose()
} catch {}
try {
  $null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]
  foreach ($voice in [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices) {
    if ($voice.Language -like 'hi-*') { $names.Add($voice.DisplayName) }
  }
} catch {}
$names | Sort-Object -Unique
''');
      if (result.exitCode != 0) return const [];
      return _lines(result.stdout);
    } catch (_) {
      return const [];
    }
  }

  Future<bool> hasHindiVoice() async =>
      (await getHindiVoices()).isNotEmpty;

  List<String> _lines(Object output) => output
      .toString()
      .split(RegExp(r'\r?\n'))
      .map((value) => value.trim())
      .where((value) => value.isNotEmpty)
      .toSet()
      .toList();

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
    final safeSapiRate =
        ((speechRate.clamp(0.1, 1.0) * 10) - 5).round().clamp(-10, 10);
    final oneCoreRate = (speechRate.clamp(0.1, 1.0) * 2.0)
        .clamp(0.5, 2.0)
        .toStringAsFixed(2);
    final script = r'''
$ErrorActionPreference = 'Stop'
$text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('__ENCODED_TEXT__'))
$voiceName = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('__ENCODED_VOICE__'))
$preferHindi = __PREFER_HINDI__
$oneCoreVoice = $null

try {
  $null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]
  $oneCoreVoices = [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices
  if ($preferHindi) {
    if (-not [string]::IsNullOrWhiteSpace($voiceName)) {
      $oneCoreVoice = $oneCoreVoices | Where-Object {
        $_.DisplayName -eq $voiceName -and $_.Language -like 'hi-*'
      } | Select-Object -First 1
    }
    if ($null -eq $oneCoreVoice) {
      $oneCoreVoice = $oneCoreVoices | Where-Object {
        $_.Language -like 'hi-*'
      } | Select-Object -First 1
    }
  } elseif (-not [string]::IsNullOrWhiteSpace($voiceName)) {
    $oneCoreVoice = $oneCoreVoices | Where-Object {
      $_.DisplayName -eq $voiceName
    } | Select-Object -First 1
  }
} catch {}

if ($null -ne $oneCoreVoice) {
  Add-Type -AssemblyName System.Runtime.WindowsRuntime
  $synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
  $tempPath = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    'cane_factory_tts_' + [System.Guid]::NewGuid().ToString('N') + '.wav')
  try {
    $synth.Voice = $oneCoreVoice
    try { $synth.Options.AudioVolume = __VOLUME__ / 100.0 } catch {}
    try { $synth.Options.SpeakingRate = __ONECORE_RATE__ } catch {}
    $operation = $synth.SynthesizeTextToStreamAsync($text)
    $asTaskMethod = [System.WindowsRuntimeSystemExtensions].GetMethods() |
      Where-Object {
        $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and
        $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
      } | Select-Object -First 1
    $asTask = $asTaskMethod.MakeGenericMethod(
      [Windows.Media.SpeechSynthesis.SpeechSynthesisStream])
    $task = $asTask.Invoke($null, @($operation))
    $task.Wait()
    $speechStream = $task.Result
    $netStream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($speechStream)
    $output = [System.IO.File]::Create($tempPath)
    try { $netStream.CopyTo($output) } finally {
      $output.Dispose()
      $netStream.Dispose()
      $speechStream.Dispose()
    }
    $sound = New-Object System.Media.SoundPlayer($tempPath)
    try { $sound.PlaySync() } finally { $sound.Dispose() }
  } finally {
    if ($null -ne $synth) { $synth.Dispose() }
    if (Test-Path -LiteralPath $tempPath) {
      Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
  }
  exit 0
}

Add-Type -AssemblyName System.Speech
$speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
  $speaker.SetOutputToDefaultAudioDevice()
  $speaker.Volume = __VOLUME__
  $speaker.Rate = __SAPI_RATE__
  $selected = $null
  if (-not [string]::IsNullOrWhiteSpace($voiceName)) {
    $selected = $speaker.GetInstalledVoices() | Where-Object {
      $_.Enabled -and $_.VoiceInfo.Name -eq $voiceName -and
      (-not $preferHindi -or $_.VoiceInfo.Culture.Name -like 'hi-*')
    } | Select-Object -First 1
  }
  if ($null -eq $selected -and $preferHindi) {
    $selected = $speaker.GetInstalledVoices() | Where-Object {
      $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'hi-*'
    } | Select-Object -First 1
  }
  if ($null -ne $selected) {
    $speaker.SelectVoice($selected.VoiceInfo.Name)
  } elseif ($preferHindi) {
    throw 'Hindi voice was detected but could not be selected.'
  }
  $speaker.Speak($text)
} finally {
  $speaker.Dispose()
}
'''
        .replaceAll('__ENCODED_TEXT__', encodedText)
        .replaceAll('__ENCODED_VOICE__', encodedVoice)
        .replaceAll('__PREFER_HINDI__', preferHindi ? r'$true' : r'$false')
        .replaceAll('__VOLUME__', '$safeVolume')
        .replaceAll('__SAPI_RATE__', '$safeSapiRate')
        .replaceAll('__ONECORE_RATE__', oneCoreRate);

    final process = await Process.start(
      'powershell.exe',
      [..._baseArguments, script],
      runInShell: false,
    );
    _activeProcess = process;
    try {
      final stdout = process.stdout.transform(utf8.decoder).join();
      final stderr = process.stderr.transform(utf8.decoder).join();
      final exitCode =
          await process.exitCode.timeout(const Duration(seconds: 45));
      final errorText = (await stderr).trim();
      await stdout;
      if (exitCode != 0) {
        throw StateError(errorText.isEmpty
            ? 'Windows speech service exited with code $exitCode.'
            : 'Windows speech service: $errorText');
      }
    } on TimeoutException {
      stop();
      throw StateError('Windows speech service timed out.');
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
