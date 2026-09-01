import 'dart:io';
import 'dart:typed_data';
import 'package:dio/dio.dart';
import 'package:printing_ffi/printing_ffi.dart';
import 'api_client.dart';

class PrintOutcome {
  final bool success;
  final String message;
  PrintOutcome(this.success, this.message);
}

/// Fetches the print-ready document from the centralized backend Print Engine and sends it to the
/// local Windows printer via printing_ffi (winspool). A4 -> temp PDF file + printPdf(); DotMatrix ->
/// raw ESC/P bytes + rawDataToPrinter(). Never throws - always returns a PrintOutcome so callers can
/// show a clear retry error instead of crashing when the backend or the printer is unreachable.
class PrintService {
  static Future<PrintOutcome> printDocument({
    required String documentUrl,
    required String printerType, // "A4" | "DotMatrix"
    required String printerName,
    int copies = 1,
  }) async {
    if (printerName.trim().isEmpty) {
      return PrintOutcome(false, 'No printer configured. Set Printer Name in Developer Dashboard \u2192 Print.');
    }
    if (!Platform.isWindows) {
      return PrintOutcome(false, 'Local factory printing is only supported on the Windows client.');
    }
    List<int> bytes;
    try {
      final res = await ApiClient.instance.dio.get(documentUrl,
          options: Options(responseType: ResponseType.bytes, validateStatus: (s) => s != null && s < 500));
      if (res.statusCode != 200) {
        return PrintOutcome(false, 'Could not generate the print document (server error ${res.statusCode}).');
      }
      bytes = res.data as List<int>;
    } catch (_) {
      return PrintOutcome(false, 'Could not reach the server to generate the print document.');
    }

    final safeCopies = copies < 1 ? 1 : (copies > 5 ? 5 : copies);
    try {
      final ffi = PrintingFfi.instance;
      if (printerType == 'A4') {
        final tempFile = File('${Directory.systemTemp.path}${Platform.pathSeparator}'
            'cane_print_${DateTime.now().millisecondsSinceEpoch}.pdf');
        await tempFile.writeAsBytes(bytes);
        bool ok;
        try {
          ok = await ffi.printPdf(printerName, tempFile.path, copies: safeCopies);
        } finally {
          try { await tempFile.delete(); } catch (_) {}
        }
        return ok
            ? PrintOutcome(true, 'Sent to printer "$printerName".')
            : PrintOutcome(false, 'Printer "$printerName" did not accept the job. Check it is online.');
      }

      var ok = true;
      final data = Uint8List.fromList(bytes);
      for (var i = 0; i < safeCopies; i++) {
        final jobOk = await ffi.rawDataToPrinter(printerName, data, docName: 'CaneFactorySlip');
        ok = ok && jobOk;
      }
      return ok
          ? PrintOutcome(true, 'Sent to printer "$printerName".')
          : PrintOutcome(false, 'Printer "$printerName" did not accept the job. Check it is online.');
    } catch (e) {
      return PrintOutcome(false, 'Local printing failed: $e');
    }
  }
}
