import 'dart:async';
import 'dart:ffi';
import 'dart:io';
import 'dart:isolate';
import 'dart:typed_data';

import 'package:ffi/ffi.dart';
import 'package:printing_ffi/printing_ffi.dart';
import 'package:win32/win32.dart';

bool get nativePrintingSupported => Platform.isWindows;

String? _nativePrintLastError;
int? _nativePrintLastJobId;

String? get nativePrintLastError => _nativePrintLastError;
int? get nativePrintLastJobId => _nativePrintLastJobId;

void initializeNativePrinting() {
  if (Platform.isWindows) {
    PrintingFfi.instance.initPdfium();
  }
}

List<String> installedNativePrinterNames() {
  if (!Platform.isWindows) return const [];
  return PrintingFfi.instance
      .listPrinters()
      .where((printer) => printer.isAvailable)
      .map((printer) => printer.name)
      .toList(growable: false);
}

bool nativePrinterPrefersDriverRendering(String printerName) {
  if (!Platform.isWindows) return false;
  final requested = printerName.trim().toLowerCase();
  for (final printer in PrintingFfi.instance.listPrinters()) {
    if (printer.name.trim().toLowerCase() != requested) continue;
    final model = (printer.model ?? '').toLowerCase();
    return model.contains('v4 class driver') ||
        model.contains('class driver') ||
        requested.contains('epson lx-310') ||
        requested.contains('epson lx 310');
  }
  return requested.contains('epson lx-310') ||
      requested.contains('epson lx 310');
}

Future<bool> printNativePdf(
  String printerName,
  Uint8List bytes, {
  required int copies,
  bool dotMatrixDriverMode = false,
  int dotMatrixDriverDpi = 240,
}) async {
  if (!Platform.isWindows) return false;
  final tempFile = File(
    '${Directory.systemTemp.path}${Platform.pathSeparator}'
    'cane_print_${DateTime.now().millisecondsSinceEpoch}.pdf',
  );
  await tempFile.writeAsBytes(bytes, flush: true);
  try {
    // Epson V4/Class treats a PDF image as a graphics job. Asking the driver
    // for high quality avoids its draft/normal bidirectional bitmap shortcut,
    // which creates the visible one-pin halo around otherwise black glyphs.
    // A 120-DPI TVS-compatible preview keeps Normal for speed.
    final driverOptions = <PrintOption>[
      PrintQualityOption(dotMatrixDriverDpi >= 240
          ? PrintQuality.high
          : PrintQuality.normal),
      const ColorModeOption(ColorMode.monochrome),
      const AlignmentOption(PdfPrintAlignment.topLeft),
    ];
    return await PrintingFfi.instance.printPdf(
      printerName,
      tempFile.path,
      copies: copies,
      scaling: dotMatrixDriverMode
          ? PdfPrintScaling.actualSize
          : PdfPrintScaling.fitToPrintableArea,
      options: dotMatrixDriverMode ? driverOptions : const [],
    );
  } finally {
    try {
      await tempFile.delete();
    } catch (_) {}
  }
}

Future<bool> printNativeRaw(
  String printerName,
  Uint8List bytes, {
  required int copies,
}) async {
  if (!Platform.isWindows) return false;
  _nativePrintLastError = null;
  _nativePrintLastJobId = null;
  var accepted = true;
  for (var i = 0; i < copies; i++) {
    // Send ESC/P directly to the Windows RAW spooler. printing_ffi's helper
    // protocol treats some ESC/P payloads as text/JSON and can throw a
    // FormatException before WritePrinter is reached. A direct Winspool job
    // keeps the byte stream binary from end to end.
    Map<String, Object?> result;
    try {
      result = await Isolate.run(() => _writeRawWinspool(printerName, bytes))
          .timeout(const Duration(seconds: 30));
    } on TimeoutException {
      result = const {
        'accepted': false,
        'error': 'Windows RAW spooler has not responded within 30 seconds; check the queue before retrying because the job may still be in progress.',
      };
    } catch (error) {
      result = {
        'accepted': false,
        'error': 'Windows RAW spooler failed: $error',
      };
    }
    final jobAccepted = result['accepted'] == true;
    _nativePrintLastJobId = result['jobId'] as int?;
    if (!jobAccepted) _nativePrintLastError = result['error']?.toString();
    accepted = accepted && jobAccepted;
    if (!jobAccepted) return false;
  }
  return accepted;
}

Map<String, Object?> _writeRawWinspool(
  String printerName,
  Uint8List bytes,
) {
  if (bytes.isEmpty) {
    return const {
      'accepted': false,
      'error': 'The RAW print payload is empty.'
    };
  }
  final printerNamePtr = printerName.toNativeUtf16();
  final printerHandlePtr = calloc<IntPtr>();
  final printerDefaults = calloc<PRINTER_DEFAULTS>();
  final docInfo = calloc<DOC_INFO_1>();
  final docName = 'CaneFactorySlip'.toNativeUtf16();
  final dataType = 'RAW'.toNativeUtf16();
  final payload = calloc<Uint8>(bytes.length);
  final written = calloc<Uint32>();
  final jobInfoSize = calloc<Uint32>();
  var printerHandle = 0;
  var jobId = 0;
  var documentStarted = false;
  var pageStarted = false;
  Map<String, Object?> failed(String stage) => {
        'accepted': false,
        'error': '$stage failed (Windows error ${GetLastError()}).',
      };
  try {
    payload.asTypedList(bytes.length).setAll(0, bytes);
    printerDefaults.ref
      ..pDatatype = dataType
      ..pDevMode = nullptr
      ..DesiredAccess = PRINTER_ACCESS_USE;
    if (OpenPrinter(printerNamePtr, printerHandlePtr, printerDefaults) == 0) {
      return failed('OpenPrinter');
    }
    printerHandle = printerHandlePtr.value;
    docInfo.ref
      ..pDocName = docName
      ..pOutputFile = nullptr
      ..pDatatype = dataType;
    jobId = StartDocPrinter(printerHandle, 1, docInfo);
    if (jobId == 0) {
      return failed('StartDocPrinter');
    }
    documentStarted = true;
    if (StartPagePrinter(printerHandle) == 0) {
      return failed('StartPagePrinter');
    }
    pageStarted = true;

    // Epson and other V4 dot-matrix drivers may accept only part of a large
    // ESC/P raster job per call. Continue from the reported byte count.
    const chunkSize = 32 * 1024;
    var offset = 0;
    while (offset < bytes.length) {
      final remaining = bytes.length - offset;
      final requested = remaining < chunkSize ? remaining : chunkSize;
      written.value = 0;
      final ok = WritePrinter(
            printerHandle,
            (payload + offset).cast<Void>(),
            requested,
            written,
          ) !=
          0;
      if (!ok) return failed('WritePrinter');
      if (written.value == 0) {
        return const {
          'accepted': false,
          'error': 'WritePrinter accepted zero bytes.',
        };
      }
      offset += written.value;
    }

    // Check the spooler while the document is still open. A successful
    // WritePrinter alone does not prove that a RAW job was actually queued.
    GetJob(printerHandle, jobId, 1, nullptr, 0, jobInfoSize);
    if (jobInfoSize.value == 0) {
      return {
        'accepted': false,
        'jobId': jobId,
        'error': 'RAW job $jobId is not visible in the Windows print queue (GetJob error ${GetLastError()}).',
      };
    }
    final jobInfoBuffer = calloc<Uint8>(jobInfoSize.value);
    try {
      if (GetJob(printerHandle, jobId, 1, jobInfoBuffer,
              jobInfoSize.value, jobInfoSize) == 0) {
        return {
          'accepted': false,
          'jobId': jobId,
          'error': 'RAW job $jobId could not be verified (GetJob error ${GetLastError()}).',
        };
      }
      final queued = jobInfoBuffer.cast<JOB_INFO_1>().ref;
      if (queued.pDatatype == nullptr ||
          queued.pDatatype.toDartString().toUpperCase() != 'RAW') {
        return {
          'accepted': false,
          'jobId': jobId,
          'error': 'Windows queue changed job $jobId from RAW to another data type.',
        };
      }
    } finally {
      calloc.free(jobInfoBuffer);
    }

    if (EndPagePrinter(printerHandle) == 0) {
      return failed('EndPagePrinter');
    }
    pageStarted = false;
    if (EndDocPrinter(printerHandle) == 0) {
      return failed('EndDocPrinter');
    }
    documentStarted = false;
    return {'accepted': true, 'jobId': jobId};
  } finally {
    if (printerHandle != 0) {
      if (pageStarted || documentStarted) AbortPrinter(printerHandle);
      ClosePrinter(printerHandle);
    }
    calloc.free(written);
    calloc.free(jobInfoSize);
    calloc.free(payload);
    calloc.free(dataType);
    calloc.free(docName);
    calloc.free(docInfo);
    calloc.free(printerDefaults);
    calloc.free(printerHandlePtr);
    calloc.free(printerNamePtr);
  }
}
