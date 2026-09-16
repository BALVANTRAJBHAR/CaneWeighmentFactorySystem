import 'dart:io';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';
import 'package:url_launcher/url_launcher.dart';
import 'api_client.dart';

/// Phase 11/12/13: shared PDF/Excel/script download helper for Reports, Farmer Statement and
/// Backup script generation - fetches bytes via Dio and saves next to the user's Downloads
/// folder (falls back to the app documents folder on platforms without one, e.g. mobile).
Future<void> downloadAndNotify(
    BuildContext context, String path, String filename,
    {Map<String, dynamic>? queryParameters}) async {
  try {
    final res = await ApiClient.instance.dio.get<List<int>>(path,
        queryParameters: queryParameters,
        options: Options(responseType: ResponseType.bytes));
    if (res.statusCode != 200 || res.data == null) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
            content: Text('Download failed (HTTP ${res.statusCode}).')));
      }
      return;
    }
    final dir = await getDownloadsDirectory() ??
        await getApplicationDocumentsDirectory();
    final file = File('${dir.path}${Platform.pathSeparator}$filename');
    await file.writeAsBytes(res.data!);
    if (context.mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('Saved: ${file.path}')));
    }
  } catch (e) {
    if (context.mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('Download failed: $e')));
    }
  }
}

/// Fetches an authenticated PDF and opens it in the operating system's default
/// PDF viewer.  This is deliberately separate from printing: when automatic
/// printing is disabled, a completed transaction must still give the operator
/// its printable document without sending a job to a physical printer.
Future<void> openPdfAfterSave(
    BuildContext context, String path, String filename) async {
  try {
    final res = await ApiClient.instance.dio.get<List<int>>(path,
        options: Options(responseType: ResponseType.bytes));
    if (res.statusCode != 200 || res.data == null) {
      throw StateError('PDF generation failed (HTTP ${res.statusCode}).');
    }
    final dir = await getApplicationDocumentsDirectory();
    final file = File('${dir.path}${Platform.pathSeparator}$filename');
    await file.writeAsBytes(res.data!, flush: true);
    final opened = await launchUrl(file.uri,
        mode: LaunchMode.externalApplication, webOnlyWindowName: '_blank');
    if (!opened)
      throw StateError('No application is associated with PDF files.');
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Auto print is off. PDF opened.')));
    }
  } catch (e) {
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content:
              Text('Transaction saved, but the PDF could not be opened: $e')));
    }
  }
}
