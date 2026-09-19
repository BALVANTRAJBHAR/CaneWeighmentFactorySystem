import 'dart:convert';
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

String _downloadFailureMessage(dynamic data, int? statusCode) {
  dynamic decoded = data;
  if (data is List<int>) {
    try {
      decoded = jsonDecode(utf8.decode(data));
    } catch (_) {
      decoded = utf8.decode(data, allowMalformed: true);
    }
  }
  if (decoded is String) {
    try {
      decoded = jsonDecode(decoded);
    } catch (_) {
      if (decoded.trim().isNotEmpty) return decoded.trim();
    }
  }
  if (decoded is Map) {
    for (final key in ['message', 'detail', 'title']) {
      final value = decoded[key];
      if (value != null && value.toString().trim().isNotEmpty) {
        return value.toString().trim();
      }
    }
  }
  return 'Download failed (HTTP ${statusCode ?? 'unknown'}).';
}

/// Creates an authenticated server-side export/backup, then saves the returned file locally.
/// POST is intentional here: the endpoint performs work before returning the download.
Future<void> postDownloadAndNotify(
    BuildContext context, String path, String filename) async {
  try {
    final res = await ApiClient.instance.dio.post<List<int>>(path,
        options: Options(
          responseType: ResponseType.bytes,
          receiveTimeout: const Duration(minutes: 10),
          sendTimeout: const Duration(minutes: 1),
        ));
    if (res.statusCode != 200 || res.data == null) {
      final message = _downloadFailureMessage(res.data, res.statusCode);
      if (context.mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(message)));
      }
      return;
    }
    final dir = await getDownloadsDirectory() ??
        await getApplicationDocumentsDirectory();
    final file = File('${dir.path}${Platform.pathSeparator}$filename');
    await file.writeAsBytes(res.data!, flush: true);
    if (context.mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('Saved: ${file.path}')));
    }
  } on DioException catch (e) {
    final message = e.response == null
        ? 'Download failed: ${e.message}'
        : _downloadFailureMessage(
            e.response?.data, e.response?.statusCode);
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
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
    BuildContext context, String path, String filename,
    {String successMessage = 'Auto print is off. PDF opened.',
    String failurePrefix = 'Transaction saved, but the PDF could not be opened:'}) async {
  try {
    final res = await ApiClient.instance.dio.get<List<int>>(path,
        options: Options(responseType: ResponseType.bytes));
    if (res.statusCode != 200 || res.data == null) {
      var message = 'PDF generation failed (HTTP ${res.statusCode}).';
      try {
        final decoded = jsonDecode(utf8.decode(res.data ?? const [])) as Map;
        if (decoded['message'] != null) message = decoded['message'].toString();
      } catch (_) {}
      throw StateError(message);
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
          SnackBar(content: Text(successMessage)));
    }
  } catch (e) {
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text('$failurePrefix $e')));
    }
  }
}
