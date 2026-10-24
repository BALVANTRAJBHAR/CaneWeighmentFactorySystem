import 'dart:async';
import 'dart:ffi';
import 'dart:io';
import 'dart:isolate';
import 'dart:typed_data';

import 'package:ffi/ffi.dart';
import 'package:printing_ffi/printing_ffi.dart';
import 'package:win32/win32.dart';

bool get nativePrintingSupported => Platform.isWindows;

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

Future<bool> printNativePdf(
  String printerName,
  Uint8List bytes, {
  required int copies,
}) async {
  if (!Platform.isWindows) return false;
  final tempFile = File(
    '${Directory.systemTemp.path}${Platform.pathSeparator}'
    'cane_print_${DateTime.now().millisecondsSinceEpoch}.pdf',
  );
  await tempFile.writeAsBytes(bytes, flush: true);
  try {
    return await PrintingFfi.instance.printPdf(
      printerName,
      tempFile.path,
      copies: copies,
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
  var accepted = true;
  for (var i = 0; i < copies; i++) {
    // Send ESC/P directly to the Windows RAW spooler. printing_ffi's helper
    // protocol treats some ESC/P payloads as text/JSON and can throw a
    // FormatException before WritePrinter is reached. A direct Winspool job
    // keeps the byte stream binary from end to end.
    bool jobAccepted;
    try {
      jobAccepted =
          await Isolate.run(() => _writeRawWinspool(printerName, bytes))
              .timeout(const Duration(seconds: 12));
    } on TimeoutException {
      return false;
    } catch (_) {
      return false;
    }
    accepted = accepted && jobAccepted;
  }
  return accepted;
}

bool _writeRawWinspool(String printerName, Uint8List bytes) {
  if (bytes.isEmpty) return false;
  final printerNamePtr = printerName.toNativeUtf16();
  final printerHandlePtr = calloc<IntPtr>();
  final docInfo = calloc<DOC_INFO_1>();
  final docName = 'CaneFactorySlip'.toNativeUtf16();
  final dataType = 'RAW'.toNativeUtf16();
  final payload = calloc<Uint8>(bytes.length);
  final written = calloc<Uint32>();
  var printerHandle = 0;
  var documentStarted = false;
  var pageStarted = false;
  try {
    payload.asTypedList(bytes.length).setAll(0, bytes);
    if (OpenPrinter(printerNamePtr, printerHandlePtr, nullptr) == 0) {
      return false;
    }
    printerHandle = printerHandlePtr.value;
    docInfo.ref
      ..pDocName = docName
      ..pOutputFile = nullptr
      ..pDatatype = dataType;
    if (StartDocPrinter(printerHandle, 1, docInfo) == 0) return false;
    documentStarted = true;
    if (StartPagePrinter(printerHandle) == 0) return false;
    pageStarted = true;
    final ok = WritePrinter(
          printerHandle,
          payload.cast<Void>(),
          bytes.length,
          written,
        ) !=
        0;
    if (!ok || written.value != bytes.length) return false;
    if (EndPagePrinter(printerHandle) == 0) return false;
    pageStarted = false;
    if (EndDocPrinter(printerHandle) == 0) return false;
    documentStarted = false;
    return true;
  } finally {
    if (printerHandle != 0) {
      if (pageStarted || documentStarted) AbortPrinter(printerHandle);
      ClosePrinter(printerHandle);
    }
    calloc.free(written);
    calloc.free(payload);
    calloc.free(dataType);
    calloc.free(docName);
    calloc.free(docInfo);
    calloc.free(printerHandlePtr);
    calloc.free(printerNamePtr);
  }
}
