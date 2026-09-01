import 'dart:io';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:printing_ffi/printing_ffi.dart';
import 'core/app_theme.dart';
import 'providers/auth_provider.dart';
import 'providers/live_weight_provider.dart';
import 'providers/theme_provider.dart';
import 'screens/auth/change_password_screen.dart';
import 'screens/auth/login_screen.dart';
import 'widgets/app_shell.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  // Phase 7: printing_ffi is the only PDFium-based plugin in this app - must init once on Windows.
  if (Platform.isWindows) {
    PrintingFfi.instance.initPdfium();
  }
  runApp(MultiProvider(
    providers: [
      ChangeNotifierProvider(create: (_) => ThemeProvider()),
      ChangeNotifierProvider(create: (_) => AuthProvider()),
      ChangeNotifierProvider(create: (_) => LiveWeightProvider()),
    ],
    child: const CaneFactoryApp(),
  ));
}

class CaneFactoryApp extends StatelessWidget {
  const CaneFactoryApp({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeProvider>();
    return MaterialApp(
      title: 'Cane Factory Management',
      debugShowCheckedModeBanner: false,
      themeMode: theme.mode,
      theme: AppTheme.light(theme.seed),
      darkTheme: AppTheme.dark(theme.seed),
      home: const RootGate(),
    );
  }
}

/// Route guard: unauthenticated -> Login; must-change-password -> forced change screen;
/// otherwise the role-aware shell. The backend independently enforces every permission.
class RootGate extends StatefulWidget {
  const RootGate({super.key});
  @override
  State<RootGate> createState() => _RootGateState();
}

class _RootGateState extends State<RootGate> {
  bool _checking = true;

  @override
  void initState() {
    super.initState();
    context.read<AuthProvider>().tryRestoreSession().whenComplete(() {
      if (mounted) setState(() => _checking = false);
    });
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    if (_checking) return const Scaffold(body: Center(child: CircularProgressIndicator()));
    if (!auth.isLoggedIn) return const LoginScreen();
    if (auth.mustChangePassword) return const ChangePasswordScreen(forced: true);
    return const AppShell();
  }
}
