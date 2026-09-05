// Basic smoke test: the app boots without throwing and shows the Splash Screen on the
// very first frame (before the async config/API/session checks resolve).
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

import 'package:cane_factory_app/main.dart';
import 'package:cane_factory_app/providers/auth_provider.dart';
import 'package:cane_factory_app/providers/live_weight_provider.dart';
import 'package:cane_factory_app/providers/theme_provider.dart';

void main() {
  testWidgets('App boots and shows the splash screen on first frame', (WidgetTester tester) async {
    await tester.pumpWidget(MultiProvider(
      providers: [
        ChangeNotifierProvider(create: (_) => ThemeProvider()),
        ChangeNotifierProvider(create: (_) => AuthProvider()),
        ChangeNotifierProvider(create: (_) => LiveWeightProvider()),
      ],
      child: const CaneFactoryApp(),
    ));

    // First frame only - the startup sequence (config/API/session checks) hasn't
    // resolved yet, so the splash screen with the app name must be visible.
    expect(find.text('Cane Factory Management System'), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsWidgets);
  });
}
