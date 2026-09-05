import 'dart:io';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:printing_ffi/printing_ffi.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'core/api_client.dart';
import 'core/app_theme.dart';
import 'providers/auth_provider.dart';
import 'providers/live_weight_provider.dart';
import 'providers/theme_provider.dart';
import 'screens/auth/change_password_screen.dart';
import 'screens/auth/login_screen.dart';
import 'screens/splash/splash_screen.dart';
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
  bool _apiError = false;
  String _status = 'Initializing...';
  String? _companyName;

  @override
  void initState() {
    super.initState();
    _startup();
  }

  Future<void> _startup() async {
    setState(() {
      _checking = true;
      _apiError = false;
      _status = 'Loading configuration...';
    });

    // Instant branding from the last successful login, if any (no network needed).
    try {
      final prefs = await SharedPreferences.getInstance();
      _companyName = prefs.getString('cached_company_name');
    } catch (_) {}

    final urlError = ApiClient.configurationError;
    if (urlError != null) {
      setState(() {
        _apiError = true;
        _status = urlError;
      });
      return;
    }
    setState(() => _status = 'Checking server connection...');
    try {
      final res = await ApiClient.instance.dio.get('/api/health',
          options: Options(
              sendTimeout: const Duration(seconds: 8),
              receiveTimeout: const Duration(seconds: 8)));
      if (res.statusCode != 200)
        throw Exception('Server responded with ${res.statusCode}');
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _apiError = true;
        _status =
            'Cannot reach the server. Check your network connection and try again.';
      });
      return;
    }

    try {
      final license = await ApiClient.instance.dio.get('/api/license/status');
      if (license.statusCode != 200 || license.data['isValid'] != true) {
        if (!mounted) return;
        setState(() {
          _apiError = true;
          _status = license.data['message']?.toString() ??
              'Software license validation failed.';
        });
        return;
      }
      if (license.data['isWarning'] == true && mounted) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted)
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(
                content: Text(license.data['message'].toString()),
                duration: const Duration(seconds: 8)));
        });
      }
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _apiError = true;
        _status = 'Could not validate the software license.';
      });
      return;
    }

    if (!mounted) return;
    setState(() => _status = 'Checking your session...');
    await context.read<AuthProvider>().tryRestoreSession();

    if (!mounted) return;
    setState(() => _checking = false);
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    if (_checking || _apiError) {
      return SplashScreen(
          statusText: _status,
          hasError: _apiError,
          onRetry: _apiError ? _startup : null,
          companyName: _companyName);
    }
    if (!auth.isLoggedIn) return const LoginScreen();
    if (auth.mustChangePassword)
      return const ChangePasswordScreen(forced: true);
    return const AppShell();
  }
}
