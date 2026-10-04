import 'dart:io';
import 'package:path_provider/path_provider.dart';
import 'package:url_launcher/url_launcher.dart';

Future<String> saveDownloadedBytes(List<int> bytes, String filename,
    {bool applicationDocuments = false}) async {
  final dir = applicationDocuments
      ? await getApplicationDocumentsDirectory()
      : (await getDownloadsDirectory() ??
          await getApplicationDocumentsDirectory());
  final file = File('${dir.path}${Platform.pathSeparator}$filename');
  await file.writeAsBytes(bytes, flush: true);
  return file.path;
}

Future<bool> saveAndOpenPdf(List<int> bytes, String filename) async {
  final path =
      await saveDownloadedBytes(bytes, filename, applicationDocuments: true);
  final file = File(path);
  var opened = false;
  Object? lastOpenError;
  for (var attempt = 0; attempt < 3 && !opened; attempt++) {
    try {
      opened = await launchUrl(file.uri,
          mode: LaunchMode.externalApplication, webOnlyWindowName: '_blank');
    } catch (error) {
      lastOpenError = error;
    }
    if (!opened && attempt < 2) {
      await Future<void>.delayed(Duration(milliseconds: 350 * (attempt + 1)));
    }
  }
  if (!opened && Platform.isWindows) {
    try {
      await Process.start('explorer.exe', <String>[file.path],
          mode: ProcessStartMode.detached);
      opened = true;
    } catch (error) {
      lastOpenError = error;
    }
  }
  if (!opened && lastOpenError != null) throw lastOpenError;
  return opened;
}
