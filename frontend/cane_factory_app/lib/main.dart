import 'dart:async';
import 'dart:ui';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'core/api_client.dart';
import 'core/app_theme.dart';
import 'core/native_print_adapter.dart';
import 'providers/auth_provider.dart';
import 'providers/live_weight_provider.dart';
import 'providers/theme_provider.dart';
import 'screens/auth/change_password_screen.dart';
import 'screens/auth/login_screen.dart';
import 'screens/splash/splash_screen.dart';
import 'widgets/app_shell.dart';

void main() {
  runZonedGuarded(() async {
    WidgetsFlutterBinding.ensureInitialized();
    FlutterError.onError = (details) {
      FlutterError.presentError(details);
    };
    PlatformDispatcher.instance.onError = (error, stackTrace) {
      // Keep a recoverable asynchronous/plugin error from terminating the
      // desktop process while still writing the diagnostic in debug/console runs.
      debugPrint('Unhandled application error: $error\n$stackTrace');
      return true;
    };
    try {
      initializeNativePrinting();
    } catch (error, stackTrace) {
      debugPrint('PDFium initialization failed: $error\n$stackTrace');
    }
    runApp(MultiProvider(
      providers: [
        ChangeNotifierProvider(create: (_) => ThemeProvider()),
        ChangeNotifierProvider(create: (_) => AuthProvider()),
        ChangeNotifierProvider(create: (_) => LiveWeightProvider()),
      ],
      child: const CaneFactoryApp(),
    ));
  }, (error, stackTrace) {
    debugPrint('Application zone error: $error\n$stackTrace');
  });
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
    final startedAt = DateTime.now();
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
      await _ensureMinimumSplashTime(startedAt);
      if (!mounted) return;
      setState(() {
        _apiError = true;
        _status = urlError;
      });
      return;
    }
    setState(() => _status = 'Checking server connection...');
    try {
      final ping = await ApiClient.instance.dio.get('/api/ping',
          options: Options(
              sendTimeout: const Duration(seconds: 8),
              receiveTimeout: const Duration(seconds: 8)));
      if (ping.statusCode != 200) {
        throw Exception('Server responded with ${ping.statusCode}');
      }
    } catch (error) {
      await _ensureMinimumSplashTime(startedAt);
      if (!mounted) return;
      final statusCode =
          error is DioException ? error.response?.statusCode : null;
      setState(() {
        _apiError = true;
        _status = statusCode != null && statusCode >= 500
            ? 'IIS is reachable at ${ApiClient.baseUrl}, but the API failed to start (HTTP $statusCode). Restore the server configuration and check the IIS application log.'
            : 'Cannot reach ${ApiClient.baseUrl}. Check the IIS binding/port, server firewall, and the API application pool.';
      });
      return;
    }

    setState(() => _status = 'Checking database connection...');
    try {
      final health = await ApiClient.instance.dio.get('/api/health',
          options: Options(
              sendTimeout: const Duration(seconds: 8),
              receiveTimeout: const Duration(seconds: 8)));
      if (health.statusCode != 200) {
        final database =
            health.data is Map ? health.data['database']?.toString() : null;
        await _ensureMinimumSplashTime(startedAt);
        if (!mounted) return;
        setState(() {
          _apiError = true;
          _status =
              'Server is reachable at ${ApiClient.baseUrl}, but its database is ${database ?? 'unavailable'} (HTTP ${health.statusCode}). Check the server connection string and SQL Server.';
        });
        return;
      }
    } catch (_) {
      await _ensureMinimumSplashTime(startedAt);
      if (!mounted) return;
      setState(() {
        _apiError = true;
        _status =
            'Server is reachable at ${ApiClient.baseUrl}, but the database health check failed.';
      });
      return;
    }

    try {
      final license = await ApiClient.instance.dio.get('/api/license/status');
      if (license.statusCode != 200 || license.data['isValid'] != true) {
        await _ensureMinimumSplashTime(startedAt);
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
      await _ensureMinimumSplashTime(startedAt);
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

    await _ensureMinimumSplashTime(startedAt);
    if (!mounted) return;
    setState(() => _checking = false);
  }

  Future<void> _ensureMinimumSplashTime(DateTime startedAt) async {
    const minimum = Duration(seconds: 4);
    final remaining = minimum - DateTime.now().difference(startedAt);
    if (!remaining.isNegative && remaining > Duration.zero) {
      await Future<void>.delayed(remaining);
    }
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
