import 'package:affllp/widgets/pinned_table_scroll.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('keeps two-axis table viewport bounded and scrollable',
      (tester) async {
    final horizontal = ScrollController();
    final vertical = ScrollController();
    addTearDown(horizontal.dispose);
    addTearDown(vertical.dispose);

    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: SizedBox(
          width: 600,
          height: 360,
          child: PinnedTableScroll(
            horizontalController: horizontal,
            verticalController: vertical,
            minTableWidth: 1400,
            child: DataTable(
              columns: [
                for (var column = 0; column < 10; column++)
                  DataColumn(label: Text('Column $column')),
              ],
              rows: [
                for (var row = 0; row < 40; row++)
                  DataRow(cells: [
                    for (var column = 0; column < 10; column++)
                      DataCell(Text('R$row C$column')),
                  ]),
              ],
            ),
          ),
        ),
      ),
    ));

    expect(tester.takeException(), isNull);
    expect(find.byType(Scrollbar), findsNWidgets(2));
    expect(horizontal.position.maxScrollExtent, greaterThan(0));
    expect(vertical.position.maxScrollExtent, greaterThan(0));

    vertical.jumpTo(vertical.position.maxScrollExtent / 2);
    horizontal.jumpTo(horizontal.position.maxScrollExtent / 2);
    await tester.pump();

    expect(tester.takeException(), isNull);
    expect(find.byType(Scrollbar), findsNWidgets(2));
  });
}
