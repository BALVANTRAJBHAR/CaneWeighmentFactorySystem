import 'dart:typed_data';

bool get nativePrintingSupported => false;

void initializeNativePrinting() {}

List<String> installedNativePrinterNames() => const [];

Future<bool> printNativePdf(
  String printerName,
  Uint8List bytes, {
  required int copies,
}) async =>
    false;

Future<bool> printNativeRaw(
  String printerName,
  Uint8List bytes, {
  required int copies,
}) async =>
    false;
