import 'dart:convert';
import 'dart:typed_data';
import 'package:dio/dio.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'api_client.dart';
import 'native_print_adapter.dart';

typedef DotMatrixTearConfirmation = Future<bool> Function(int totalCopies);

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
  static List<String> installedPrinterNames() => installedNativePrinterNames();

  static String? validateInstalledPrinter(String name) {
    final installed = installedPrinterNames();
    if (!installed.contains(name)) {
      return 'Configured printer "$name" is not installed or unavailable on this Windows PC.';
    }
    return null;
  }

  static Future<PrintOutcome> printDocument({
    required String documentUrl,
    required String printerType, // "A4" | "DotMatrix"
    required String printerName,
    int copies = 1,
    bool dotMatrixTearOffParkingEnabled = false,
    double dotMatrixTearOffFeedLines = 12.0,
    int dotMatrixLineSpacingUnits = 20,
    DotMatrixTearConfirmation? confirmTearOff,
  }) async {
    if (printerName.trim().isEmpty) {
      return PrintOutcome(false,
          'No printer configured. Set Printer Name in Developer Dashboard \u2192 Print.');
    }
    if (!nativePrintingSupported) {
      return PrintOutcome(false,
          'Local factory printing is only supported on the Windows client.');
    }
    final printerError = validateInstalledPrinter(printerName);
    if (printerError != null) return PrintOutcome(false, printerError);
    List<int> bytes;
    try {
      final res = await ApiClient.instance.dio.get(documentUrl,
          options: Options(
              responseType: ResponseType.bytes,
              validateStatus: (s) => s != null && s < 500));
      if (res.statusCode != 200) {
        var message =
            'Could not generate the print document (server error ${res.statusCode}).';
        try {
          final decoded =
              jsonDecode(utf8.decode(List<int>.from(res.data as List))) as Map;
          if (decoded['message'] != null) {
            message = decoded['message'].toString();
          }
        } catch (_) {}
        return PrintOutcome(false, message);
      }
      bytes = res.data as List<int>;
    } catch (_) {
      return PrintOutcome(
          false, 'Could not reach the server to generate the print document.');
    }

    final safeCopies = copies < 1 ? 1 : (copies > 5 ? 5 : copies);
    try {
      final data = Uint8List.fromList(bytes);
      if (printerType == 'A4') {
        final ok = await printNativePdf(
          printerName,
          data,
          copies: safeCopies,
        );
        return ok
            ? PrintOutcome(true, 'Sent to printer "$printerName".')
            : PrintOutcome(false,
                'Printer "$printerName" did not accept the job. Check it is online.');
      }

      if (dotMatrixTearOffParkingEnabled && confirmTearOff != null) {
        return await _printDotMatrixWithTearOff(
          printerName: printerName,
          data: data,
          copies: safeCopies,
          feedLines: dotMatrixTearOffFeedLines,
          lineSpacingUnits: dotMatrixLineSpacingUnits,
          confirmTearOff: confirmTearOff,
        );
      }

      final ok = await printNativeRaw(printerName, data, copies: safeCopies);
      return ok
          ? PrintOutcome(true, 'Sent to printer "$printerName".')
          : PrintOutcome(false,
              'Printer "$printerName" did not accept the job. Check it is online.');
    } catch (e) {
      return PrintOutcome(false, 'Local printing failed: $e');
    }
  }

  static Future<PrintOutcome> _printDotMatrixWithTearOff({
    required String printerName,
    required Uint8List data,
    required int copies,
    required double feedLines,
    required int lineSpacingUnits,
    required DotMatrixTearConfirmation confirmTearOff,
  }) async {
    final safeFeedLines = feedLines.clamp(1.0, 18.0).toDouble();
    final safeSpacing = lineSpacingUnits.clamp(12, 30).toInt();

    final recovery = await recoverDotMatrixPaperToTof(
      printerName: printerName,
      feedLines: safeFeedLines,
      lineSpacingUnits: safeSpacing,
    );
    if (!recovery.success) return recovery;

    // All configured copies must remain one uninterrupted physical print batch.
    // Parking between copies would make the second half-page wait for an operator
    // and would lose the pre-printed stationery alignment.
    for (var copy = 1; copy <= copies; copy++) {
      final slipAccepted = await printNativeRaw(printerName, data, copies: 1);
      if (!slipAccepted) {
        return PrintOutcome(false,
            'Printer "$printerName" did not accept slip $copy. Check it is online.');
      }
    }

    final forwardAccepted = await _sendPaperMotion(
      printerName: printerName,
      reverse: false,
      feedLines: safeFeedLines,
      lineSpacingUnits: safeSpacing,
    );
    if (!forwardAccepted) {
      return PrintOutcome(false,
          'Printing completed, but the printer did not accept the tear-off forward feed.');
    }
    await _setPaperParked(printerName, true);

    final confirmed = await confirmTearOff(copies);
    if (!confirmed) {
      return PrintOutcome(false,
          'Paper remains at the tear-off position. Use Recover Parked Paper before the next print.');
    }

    final reverseAccepted = await _sendPaperMotion(
      printerName: printerName,
      reverse: true,
      feedLines: safeFeedLines,
      lineSpacingUnits: safeSpacing,
    );
    if (!reverseAccepted) {
      return PrintOutcome(false,
          'Paper was torn, but the printer did not accept the reverse TOF reset. Use Recover Parked Paper.');
    }
    await _setPaperParked(printerName, false);

    return PrintOutcome(true,
        'Printed $copies slip${copies == 1 ? '' : 's'} and restored the next half-page TOF.');
  }

  /// Recovers a paper position left outside the cover if the app was closed
  /// after forward parking but before the operator-confirmed reverse feed.
  static Future<PrintOutcome> recoverDotMatrixPaperToTof({
    required String printerName,
    required double feedLines,
    required int lineSpacingUnits,
  }) async {
    if (!nativePrintingSupported) {
      return PrintOutcome(false,
          'Paper-position recovery is only supported on the Windows client.');
    }
    final prefs = await SharedPreferences.getInstance();
    if (prefs.getBool(_paperParkedKey(printerName)) != true) {
      return PrintOutcome(true, 'Paper is already marked at logical TOF.');
    }
    final accepted = await _sendPaperMotion(
      printerName: printerName,
      reverse: true,
      feedLines: feedLines.clamp(1.0, 18.0).toDouble(),
      lineSpacingUnits: lineSpacingUnits.clamp(12, 30).toInt(),
    );
    if (!accepted) {
      return PrintOutcome(
          false, 'Printer did not accept the reverse TOF recovery command.');
    }
    await _setPaperParked(printerName, false);
    return PrintOutcome(true, 'Paper returned to the logical half-page TOF.');
  }

  /// Use after the operator manually aligns paper with the printer controls.
  /// This changes only local recovery state and never moves paper.
  static Future<void> markDotMatrixPaperAtTof(String printerName) =>
      _setPaperParked(printerName, false);

  static Future<bool> isDotMatrixPaperParked(String printerName) async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getBool(_paperParkedKey(printerName)) == true;
  }

  static Future<bool> _sendPaperMotion({
    required String printerName,
    required bool reverse,
    required double feedLines,
    required int lineSpacingUnits,
  }) {
    // MSP 270 ESC/P vertical movement is programmable in 1/216-inch units.
    // One configured raster line is lineSpacingUnits/180 inch.
    final units216 = (feedLines * lineSpacingUnits * 216 / 180).round();
    final bytes = <int>[0x1B, 0x40, 0x0D]; // initialize + carriage return
    var remaining = units216;
    while (remaining > 0) {
      // Keep every reverse segment at or below 1/2 inch. This is more stable
      // on tractor paper than one large reverse command.
      final chunk = remaining > 108 ? 108 : remaining;
      bytes.addAll([0x1B, reverse ? 0x6A : 0x4A, chunk]); // ESC j / ESC J
      remaining -= chunk;
    }
    bytes.add(0x0D);
    return printNativeRaw(printerName, Uint8List.fromList(bytes), copies: 1);
  }

  static String _paperParkedKey(String printerName) =>
      'dot_matrix_paper_parked_${base64Url.encode(utf8.encode(printerName))}';

  static Future<void> _setPaperParked(String printerName, bool parked) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_paperParkedKey(printerName), parked);
  }
}
