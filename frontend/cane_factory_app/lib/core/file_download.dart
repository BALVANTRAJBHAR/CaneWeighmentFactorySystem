import 'dart:convert';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'api_client.dart';
import 'file_save_adapter.dart';

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
    final savedPath = await saveDownloadedBytes(res.data!, filename);
    if (context.mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('Saved: $savedPath')));
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
    final savedPath = await saveDownloadedBytes(res.data!, filename);
    if (context.mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('Saved: $savedPath')));
    }
  } on DioException catch (e) {
    final message = e.response == null
        ? 'Download failed: ${e.message}'
        : _downloadFailureMessage(e.response?.data, e.response?.statusCode);
    if (context.mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
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
    String failurePrefix =
        'Transaction saved, but the PDF could not be opened:'}) async {
  try {
    final res = await ApiClient.instance.dio.get<List<int>>(path,
        options: Options(
          responseType: ResponseType.bytes,
          receiveTimeout: const Duration(minutes: 2),
          sendTimeout: const Duration(seconds: 30),
        ));
    if (res.statusCode != 200 || res.data == null) {
      var message = 'PDF generation failed (HTTP ${res.statusCode}).';
      try {
        final decoded = jsonDecode(utf8.decode(res.data ?? const [])) as Map;
        if (decoded['message'] != null) message = decoded['message'].toString();
      } catch (_) {}
      throw StateError(message);
    }
    final bytes = res.data!;
    if (bytes.length < 5 ||
        bytes[0] != 0x25 ||
        bytes[1] != 0x50 ||
        bytes[2] != 0x44 ||
        bytes[3] != 0x46 ||
        bytes[4] != 0x2D) {
      throw const FormatException('Server response is not a valid PDF.');
    }
    final opened = await saveAndOpenPdf(bytes, filename);
    if (!opened) {
      throw StateError('No application is associated with PDF files.');
    }
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(successMessage),
          backgroundColor: const Color(0xFF2E7D32)));
    }
  } catch (e) {
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text('$failurePrefix $e'),
          backgroundColor: Theme.of(context).colorScheme.error));
    }
  }
}
