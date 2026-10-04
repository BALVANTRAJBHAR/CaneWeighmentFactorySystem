import 'package:intl/intl.dart';

const reportDateColumns = {
  'purchaseDate',
  'grossDateTime',
  'tareDateTime',
  'paymentDate',
  'issueDate',
  'date',
};

const reportDateTimeColumns = {'overrideDateTime', 'transactionDate'};

String formatReportCell(String column, dynamic value) {
  if (value == null || value.toString().isEmpty) return '-';
  if (reportDateColumns.contains(column)) {
    final parsed = DateTime.tryParse(value.toString());
    return parsed == null ? '-' : DateFormat('dd-MM-yyyy').format(parsed);
  }
  if (reportDateTimeColumns.contains(column)) {
    final parsed = DateTime.tryParse(value.toString());
    return parsed == null ? '-' : DateFormat('dd-MM-yyyy HH:mm').format(parsed);
  }
  return value.toString();
}
