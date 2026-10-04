// ignore_for_file: deprecated_member_use, avoid_web_libraries_in_flutter
import 'dart:html' as html;

Future<String> saveDownloadedBytes(List<int> bytes, String filename,
    {bool applicationDocuments = false}) async {
  final blob = html.Blob([bytes], 'application/octet-stream');
  final url = html.Url.createObjectUrlFromBlob(blob);
  try {
    html.AnchorElement(href: url)
      ..download = filename
      ..style.display = 'none'
      ..click();
  } finally {
    await Future<void>.delayed(const Duration(seconds: 1));
    html.Url.revokeObjectUrl(url);
  }
  return filename;
}

Future<bool> saveAndOpenPdf(List<int> bytes, String filename) async {
  final blob = html.Blob([bytes], 'application/pdf');
  final url = html.Url.createObjectUrlFromBlob(blob);
  try {
    html.AnchorElement(href: url)
      ..download = filename
      ..target = '_blank'
      ..style.display = 'none'
      ..click();
  } finally {
    await Future<void>.delayed(const Duration(seconds: 1));
    html.Url.revokeObjectUrl(url);
  }
  return true;
}
