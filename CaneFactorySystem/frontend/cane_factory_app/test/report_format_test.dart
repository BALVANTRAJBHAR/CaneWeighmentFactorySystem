import 'package:flutter_test/flutter_test.dart';
import 'package:cane_factory_app/core/report_format.dart';
import 'package:cane_factory_app/core/hindi_transliteration.dart';

void main() {
  test('all report dates use day-month-year without raw ISO timestamps', () {
    for (final column in reportDateColumns) {
      expect(formatReportCell(column, '2026-09-08T08:24:56.8197576'), '08-09-2026');
      expect(formatReportCell(column, null), '-');
      expect(formatReportCell(column, 'invalid'), '-');
    }
    expect(formatReportCell('growerCode', '101/1'), '101/1');
    expect(formatReportCell('date', '2028-02-29T00:00:00'), '29-02-2028');
  });

  test('phonetic transliteration uses Hindi matras', () {
    expect(HindiTransliterator.transliterate('Ramesh Kumar'), 'रमेश कुमार');
    expect(HindiTransliterator.transliterate('Rampur'), 'रामपुर');
    expect(HindiTransliterator.transliterate('हिंदी'), 'हिंदी');
  });
}
