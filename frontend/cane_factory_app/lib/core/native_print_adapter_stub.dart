import 'dart:typed_data';

bool get nativePrintingSupported => false;

String? get nativePrintLastError => null;
int? get nativePrintLastJobId => null;

void initializeNativePrinting() {}

List<String> installedNativePrinterNames() => const [];

bool nativePrinterPrefersDriverRendering(String printerName) => false;

Future<bool> printNativePdf(
  String printerName,
  Uint8List bytes, {
  required int copies,
  bool dotMatrixDriverMode = false,
  int dotMatrixDriverDpi = 240,
}) async =>
    false;

Future<bool> printNativeRaw(
  String printerName,
  Uint8List bytes, {
  required int copies,
}) async =>
    false;
