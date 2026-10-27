import 'package:affllp/providers/auth_provider.dart';
import 'package:affllp/providers/live_weight_provider.dart';
import 'package:affllp/providers/theme_provider.dart';
import 'package:affllp/widgets/app_shell.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

void main() {
  testWidgets('multi-role user keeps menus granted by any assigned role',
      (tester) async {
    await tester.binding.setSurfaceSize(const Size(500, 900));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final auth = AuthProvider()
      ..user = {
        'username': 'multi-role-user',
        'roles': ['Admin', 'SubAdmin', 'Accountant', 'Operator'],
        'permissions': [
          'Dashboard.View',
          'CashBook.View',
          'Expense.View',
          'Report.View',
        ],
      };

    await tester.pumpWidget(MultiProvider(
      providers: [
        ChangeNotifierProvider.value(value: auth),
        ChangeNotifierProvider(create: (_) => ThemeProvider()),
        ChangeNotifierProvider(create: (_) => LiveWeightProvider()),
      ],
      child: const MaterialApp(home: AppShell()),
    ));
    await tester.pump();
    await tester.tap(find.byTooltip('Open navigation menu'));
    await tester.pumpAndSettle();

    expect(find.text('Cash Book'), findsOneWidget);
    expect(find.text('Expenses'), findsOneWidget);
    expect(find.text('Reports'), findsOneWidget);
    expect(find.text('Configuration'), findsNothing);
  });

  testWidgets('admin receives configuration entry from Print.Configure',
      (tester) async {
    await tester.binding.setSurfaceSize(const Size(500, 900));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final auth = AuthProvider()
      ..user = {
        'username': 'admin-print-user',
        'roles': ['Admin'],
        'permissions': [
          'Dashboard.View',
          'Print.View',
          'Print.Configure',
        ],
      };

    await tester.pumpWidget(MultiProvider(
      providers: [
        ChangeNotifierProvider.value(value: auth),
        ChangeNotifierProvider(create: (_) => ThemeProvider()),
        ChangeNotifierProvider(create: (_) => LiveWeightProvider()),
      ],
      child: const MaterialApp(home: AppShell()),
    ));
    await tester.pump();
    await tester.tap(find.byTooltip('Open navigation menu'));
    await tester.pumpAndSettle();

    expect(find.text('Configuration'), findsOneWidget);
    expect(find.text('Weighing Device'), findsNothing);
  });
}
