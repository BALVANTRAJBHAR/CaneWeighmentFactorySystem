import 'dart:io';
import 'dart:typed_data';

import 'package:printing_ffi/printing_ffi.dart';

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
    final jobAccepted = await PrintingFfi.instance.rawDataToPrinter(
      printerName,
      bytes,
      docName: 'CaneFactorySlip',
    );
    accepted = accepted && jobAccepted;
  }
  return accepted;
}
