import 'package:intl/intl.dart';

const reportDateColumns = {
  'date',
};

const reportDateTimeColumns = {
  'purchaseDate',
  'grossDateTime',
  'tareDateTime',
  'paymentDate',
  'issueDate',
  'overrideDateTime',
  'transactionDate',
  'entryDate',
};

String formatReportCell(String column, dynamic value) {
  if (value == null || value.toString().isEmpty) return '-';
  if (reportDateColumns.contains(column)) {
    final parsed = DateTime.tryParse(value.toString());
    return parsed == null
        ? '-'
        : DateFormat('dd-MM-yyyy').format(parsed.toLocal());
  }
  if (reportDateTimeColumns.contains(column)) {
    final parsed = DateTime.tryParse(value.toString());
    return parsed == null
        ? '-'
        : DateFormat('dd-MM-yyyy HH:mm:ss').format(parsed.toLocal());
  }
  return value.toString();
}
