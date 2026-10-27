import 'dart:convert';
import 'dart:typed_data';
import 'dart:ui' as ui;
import 'package:dio/dio.dart';
import 'package:pdf/pdf.dart';
import 'package:pdf/widgets.dart' as pw;
import 'package:shared_preferences/shared_preferences.dart';
import 'api_client.dart';
import 'native_print_adapter.dart';

typedef DotMatrixTearConfirmation = Future<bool> Function(
    int totalCopies, bool isEpson);

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

  static String? _resolveInstalledPrinterName(String name) {
    final requested = name.trim().toLowerCase();
    if (requested.isEmpty) return null;
    for (final installedName in installedPrinterNames()) {
      if (installedName.trim().toLowerCase() == requested) {
        return installedName;
      }
    }
    return null;
  }

  static String? validateInstalledPrinter(String name) {
    if (_resolveInstalledPrinterName(name) == null) {
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
    int dotMatrixDriverRenderDpi = 240,
    bool forceNativeDotMatrix = false,
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
    final resolvedPrinterName = _resolveInstalledPrinterName(printerName);
    if (resolvedPrinterName == null) {
      return PrintOutcome(false,
          'Configured printer "$printerName" is not installed or unavailable on this Windows PC.');
    }
    final targetPrinterName = resolvedPrinterName;
    final driverPreferred = printerType == 'DotMatrix' &&
        !forceNativeDotMatrix &&
        nativePrinterPrefersDriverRendering(targetPrinterName);
    // The LX-310 V4 path used to wrap the monochrome preview in a PDF. The
    // Windows graphics pipeline then resampled/dithered that image and added
    // visible one-pin "ghost" dots around otherwise hard-edged glyphs. The
    // printer itself understands ESC/P, so preserve the server's exact 1-bit
    // dots and bypass PDF/PDFium/GDI for this model.
    final epsonExactDotRaw = printerType == 'DotMatrix' &&
        _isEpsonLx310(targetPrinterName);
    final useWindowsDriver = driverPreferred && !epsonExactDotRaw;
    List<int> bytes;
    try {
      final requestedUrl = useWindowsDriver
          ? _asPreviewUrl(documentUrl)
          : epsonExactDotRaw
              ? _withEpsonRawProfile(documentUrl)
              : documentUrl;
      final res = await ApiClient.instance.dio.get(requestedUrl,
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
          targetPrinterName,
          data,
          copies: safeCopies,
        );
        return ok
            ? PrintOutcome(true, 'Sent to printer "$targetPrinterName".')
            : PrintOutcome(false,
                'Printer "$targetPrinterName" did not accept the job. Check it is online.');
      }

      if (useWindowsDriver) {
        final driverPdf = await _dotMatrixPreviewToPdf(
          data,
          lineSpacingUnits: dotMatrixLineSpacingUnits,
        );
        final ok = await printNativePdf(
          targetPrinterName,
          driverPdf,
          copies: safeCopies,
          dotMatrixDriverMode: true,
          dotMatrixDriverDpi: dotMatrixDriverRenderDpi,
        );
        if (!ok) {
          return PrintOutcome(false,
              'Windows driver for "$targetPrinterName" did not accept the job. Check printer status and paper settings.');
        }
        if (dotMatrixTearOffParkingEnabled && confirmTearOff != null) {
          // LX-310's ESC/P mode does not expose a software reverse-feed
          // command. Its documented ESC j belongs to IBM-PPDS mode, while a
          // Windows V4/Class ESC/P queue silently discards RAW reverse bytes.
          // Epson's supported continuous-paper workflow is hardware Auto
          // Tear-Off: the completed page parks at the perforation and the
          // next Windows print job automatically retracts to TOF before
          // printing. Keep TVS' immediate RAW reverse path separate below.
          await _setPaperParked(targetPrinterName, true);
          final confirmed = await confirmTearOff(safeCopies, true);
          if (!confirmed) {
            return PrintOutcome(false,
                'Paper remains at the tear-off position. Use Recover Parked Paper before the next print.');
          }
          await _setPaperParked(targetPrinterName, false);
          return PrintOutcome(true,
              'Epson slip printed and torn. With LX-310 Auto Tear-Off ON, the next print job automatically retracts to the next TOF before printing.');
        }
        return PrintOutcome(true,
            'Sent to printer "$targetPrinterName" through its Windows V4 driver and restored the next TOF.');
      }

      if (epsonExactDotRaw) {
        final ok = await printNativeRaw(
          targetPrinterName,
          data,
          copies: safeCopies,
        );
        if (!ok) {
          return PrintOutcome(false,
              'Epson exact-dot RAW job was not accepted. ${nativePrintLastError ?? 'Check printer status, RAW spool data type and paper settings.'}');
        }
        if (dotMatrixTearOffParkingEnabled && confirmTearOff != null) {
          // LX-310 ESC/P has no dependable host-driven reverse-feed command.
          // Auto Tear-Off is firmware-controlled: it parks the perforation and
          // retracts to TOF when the next RAW job begins. Keep the confirmation
          // dialog, but never send the TVS-only ESC j reverse sequence.
          await _setPaperParked(targetPrinterName, true);
          final confirmed = await confirmTearOff(safeCopies, true);
          if (!confirmed) {
            return PrintOutcome(false,
                'Paper remains at the Epson tear-off position. Tear the slip or press the printer Tear Off button before continuing.');
          }
          await _setPaperParked(targetPrinterName, false);
          return PrintOutcome(true,
              'RAW job ${nativePrintLastJobId ?? '?'} entered the Windows queue for "$targetPrinterName". After tearing, confirm the paper position on the printer; physical output and automatic reverse need a device check.');
        }
        return PrintOutcome(true,
            'RAW job ${nativePrintLastJobId ?? '?'} entered the Windows queue for "$targetPrinterName". Check the printer for physical output.');
      }

      if (dotMatrixTearOffParkingEnabled && confirmTearOff != null) {
        return await _printDotMatrixWithTearOff(
          printerName: targetPrinterName,
          data: data,
          copies: safeCopies,
          feedLines: dotMatrixTearOffFeedLines,
          lineSpacingUnits: dotMatrixLineSpacingUnits,
          confirmTearOff: confirmTearOff,
        );
      }

      final ok =
          await printNativeRaw(targetPrinterName, data, copies: safeCopies);
      return ok
          ? PrintOutcome(true, 'Sent to printer "$targetPrinterName".')
          : PrintOutcome(false,
              'Printer "$targetPrinterName" did not accept the job. ${nativePrintLastError ?? 'Check it is online.'}');
    } catch (e) {
      return PrintOutcome(false, 'Local printing failed: $e');
    }
  }

  static String _asPreviewUrl(String documentUrl) {
    final uri = Uri.parse(documentUrl);
    final query = Map<String, String>.from(uri.queryParameters);
    query['format'] = 'preview';
    return uri.replace(queryParameters: query).toString();
  }

  static String _withEpsonRawProfile(String documentUrl) {
    final uri = Uri.parse(documentUrl);
    final query = Map<String, String>.from(uri.queryParameters);
    query['rawProfile'] = 'epson';
    return uri.replace(queryParameters: query).toString();
  }

  static bool _isEpsonLx310(String printerName) {
    final normalized =
        printerName.toLowerCase().replaceAll(RegExp(r'[-_]+'), ' ');
    return normalized.contains('epson lx 310');
  }

  static Future<Uint8List> _dotMatrixPreviewToPdf(
    Uint8List pngBytes, {
    required int lineSpacingUnits,
  }) async {
    final image = pw.MemoryImage(pngBytes);
    final safeSpacing = lineSpacingUnits.clamp(12, 30);

    // The logical server canvas is always 960 px = 8 inches at 120 DPI. Epson/V4
    // queues receive a 2x (240-DPI) preview; preserve the physical 8-inch width
    // and derive the logical band count so the driver cannot enlarge or shrink it.
    // RAW ESC/P advances every band by the configured n/180 inch. Recreate
    // those physical dimensions in a custom PDF so the Windows V4 driver
    // produces the same continuous-paper height without interpreting ESC/P.
    final codec = await ui.instantiateImageCodec(pngBytes);
    final frame = await codec.getNextFrame();
    final widthPx = frame.image.width;
    final heightPx = frame.image.height;
    frame.image.dispose();
    codec.dispose();
    final renderScale = (widthPx / 960.0).clamp(1.0, 4.0);
    const widthPoints = 8.0 * PdfPageFormat.inch;
    final bands = (heightPx / (8.0 * renderScale)).ceil();
    final heightPoints = bands * safeSpacing / 180.0 * PdfPageFormat.inch;
    final pageFormat = PdfPageFormat(widthPoints, heightPoints, marginAll: 0);
    final document = pw.Document(compress: true);
    document.addPage(
      pw.Page(
        pageFormat: pageFormat,
        margin: pw.EdgeInsets.zero,
        build: (_) => pw.SizedBox.expand(
          child: pw.Image(image, fit: pw.BoxFit.fill),
        ),
      ),
    );
    return document.save();
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
            'Printer "$printerName" did not accept slip $copy. ${nativePrintLastError ?? 'Check it is online.'}');
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

    final confirmed = await confirmTearOff(copies, false);
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
    if (nativePrinterPrefersDriverRendering(printerName)) {
      return PrintOutcome(false,
          'Epson V4/Class does not expose software reverse feed. Turn LX-310 Auto Tear-Off ON; for an immediate manual return press the printer Tear Off button once.');
    }
    bool wasParked = false;
    try {
      final prefs = await SharedPreferences.getInstance();
      wasParked = prefs.getBool(_paperParkedKey(printerName)) == true;
    } on FormatException {
      // A damaged legacy preferences file must never stop the actual print
      // job. Treat the paper as manually aligned and rebuild this one flag
      // after printing succeeds.
      wasParked = false;
    } catch (_) {
      wasParked = false;
    }
    if (!wasParked) {
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
    try {
      final prefs = await SharedPreferences.getInstance();
      return prefs.getBool(_paperParkedKey(printerName)) == true;
    } catch (_) {
      return false;
    }
  }

  static Future<bool> _sendPaperMotion({
    required String printerName,
    required bool reverse,
    required double feedLines,
    required int lineSpacingUnits,
  }) {
    final isEpsonLx = printerName.toLowerCase().contains('epson lx');
    if (isEpsonLx) {
      // LX-310 uses the modern ESC/P2 vertical-position command. ESC j is a
      // legacy/deleted command on many ESC/P queues and a V4 driver can accept
      // that RAW job without moving the platen. ESC ( v moves relative to the
      // current print position in the defined 1/360-inch unit. A signed
      // 16-bit negative value pulls the tractor paper back to TOF.
      final units360 =
          (feedLines * lineSpacingUnits * 2).round().clamp(1, 32767);
      // ESC/P2 receives a signed 16-bit displacement. The old calculation
      // cleared the sign bit (0x8000 - n and then & 0x7F), so a reverse reset
      // was sent as a huge invalid *forward* distance and LX-310 ignored it.
      // Keep the full two's-complement word: -n == 0x10000 - n.
      final signedUnits = reverse ? ((0x10000 - units360) & 0xFFFF) : units360;
      final bytes = <int>[
        0x1B, 0x40, // ESC @ initialise
        0x1B, 0x28, 0x55, 0x01, 0x00, 0x0A, // ESC ( U: unit = 10/3600 = 1/360"
        0x1B, 0x28, 0x76, 0x02, 0x00, // ESC ( v: relative vertical position
        signedUnits & 0xFF,
        (signedUnits >> 8) & 0xFF,
        0x0D,
      ];
      return printNativeRaw(printerName, Uint8List.fromList(bytes), copies: 1);
    }

    // TVS/legacy 9-pin queues retain the proven ESC j/ESC J path. ESC j is
    // expressed in 1/216-inch units and should be segmented to at most 1/2".
    final units216 = (feedLines * lineSpacingUnits * 216 / 180).round();
    final bytes = <int>[0x1B, 0x40, 0x0D];
    var remaining = units216;
    while (remaining > 0) {
      final chunk = remaining > 108 ? 108 : remaining;
      bytes.addAll([0x1B, reverse ? 0x6A : 0x4A, chunk]);
      remaining -= chunk;
    }
    bytes.add(0x0D);
    return printNativeRaw(printerName, Uint8List.fromList(bytes), copies: 1);
  }

  static String _paperParkedKey(String printerName) =>
      'dot_matrix_paper_parked_${base64Url.encode(utf8.encode(printerName))}';

  static Future<void> _setPaperParked(String printerName, bool parked) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setBool(_paperParkedKey(printerName), parked);
    } catch (_) {
      // Paper motion has already been accepted by the spooler. Failure to
      // persist advisory recovery state must not suppress the next print.
    }
  }
}
