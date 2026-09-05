import 'dart:io';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';
import 'api_client.dart';

/// Phase 11/12/13: shared PDF/Excel/script download helper for Reports, Farmer Statement and
/// Backup script generation - fetches bytes via Dio and saves next to the user's Downloads
/// folder (falls back to the app documents folder on platforms without one, e.g. mobile).
Future<void> downloadAndNotify(BuildContext context, String path, String filename) async {
  try {
    final res = await ApiClient.instance.dio
        .get<List<int>>(path, options: Options(responseType: ResponseType.bytes));
    if (res.statusCode != 200 || res.data == null) {
      if (context.mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('Download failed (HTTP ${res.statusCode}).')));
      }
      return;
    }
    final dir = await getDownloadsDirectory() ?? await getApplicationDocumentsDirectory();
    final file = File('${dir.path}${Platform.pathSeparator}$filename');
    await file.writeAsBytes(res.data!);
    if (context.mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('Saved: ${file.path}')));
    }
  } catch (e) {
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Download failed: $e')));
    }
  }
}
