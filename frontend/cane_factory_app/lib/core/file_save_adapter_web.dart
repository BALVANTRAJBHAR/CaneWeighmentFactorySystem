// ignore_for_file: deprecated_member_use, avoid_web_libraries_in_flutter
import 'dart:html' as html;
import 'download_filename.dart';

Future<String> saveDownloadedBytes(List<int> bytes, String filename,
    {bool applicationDocuments = false}) async {
  final blob = html.Blob([bytes], 'application/octet-stream');
  final url = html.Url.createObjectUrlFromBlob(blob);
  try {
    html.AnchorElement(href: url)
      ..download = datedDownloadName(filename)
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
      ..download = datedDownloadName(filename)
      ..target = '_blank'
      ..style.display = 'none'
      ..click();
  } finally {
    await Future<void>.delayed(const Duration(seconds: 1));
    html.Url.revokeObjectUrl(url);
  }
  return true;
}

/// Browsers cannot launch a registered desktop spreadsheet application
/// directly. Downloading the workbook is the equivalent user-visible action
/// on web; the browser/OS can then open it from the downloads list.
Future<bool> saveAndOpenSpreadsheet(List<int> bytes, String filename) async {
  final blob = html.Blob([bytes],
      'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
  final url = html.Url.createObjectUrlFromBlob(blob);
  try {
    html.AnchorElement(href: url)
      ..download = datedDownloadName(filename)
      ..style.display = 'none'
      ..click();
  } finally {
    await Future<void>.delayed(const Duration(seconds: 1));
    html.Url.revokeObjectUrl(url);
  }
  return true;
}
