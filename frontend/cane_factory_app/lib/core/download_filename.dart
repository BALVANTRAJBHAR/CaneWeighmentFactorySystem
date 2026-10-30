import 'package:intl/intl.dart';

String datedDownloadName(String filename) {
  final dot = filename.lastIndexOf('.');
  final stem = dot < 0 ? filename : filename.substring(0, dot);
  if (RegExp(r'-\d{8}-\d{6}$').hasMatch(stem)) return filename;
  final extension = dot < 0 ? '' : filename.substring(dot);
  return '$stem-${DateFormat('MMddyyyy-HHmmss').format(DateTime.now())}$extension';
}
