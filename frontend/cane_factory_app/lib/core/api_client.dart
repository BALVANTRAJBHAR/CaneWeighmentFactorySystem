import 'package:dio/dio.dart';

export 'package:dio/dio.dart' show Response;
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Central API client. Base URL comes from --dart-define=API_BASE_URL (never hard-coded secrets).
/// Handles bearer tokens, automatic refresh-token rotation and 401 recovery.
class ApiClient {
  ApiClient._();
  static final ApiClient instance = ApiClient._();

  // Dart defines work on Windows, Android, iOS and Web. The local fallback is deliberately
  // generic for same-PC development only; production/LAN builds must pass API_BASE_URL.
  static const String baseUrl = String.fromEnvironment('API_BASE_URL',
      defaultValue: 'http://localhost:5000');
  static Uri? get baseUri => Uri.tryParse(baseUrl);
  static String? get configurationError {
    final uri = baseUri;
    if (uri == null ||
        !uri.hasScheme ||
        !uri.hasAuthority ||
        (uri.scheme != 'http' && uri.scheme != 'https')) {
      return 'Invalid API_BASE_URL. Use http://server:5000 or https://api.example.com.';
    }
    return null;
  }

  final _storage = const FlutterSecureStorage();
  late final Dio dio = _build();
  void Function()? onSessionExpired;

  Dio _build() {
    final d = Dio(BaseOptions(
      baseUrl: baseUrl,
      connectTimeout: const Duration(seconds: 10),
      receiveTimeout: const Duration(seconds: 20),
      validateStatus: (s) => s != null && s < 500,
    ));
    d.interceptors.add(InterceptorsWrapper(
      onRequest: (options, handler) async {
        final token = await _storage.read(key: 'access_token');
        if (token != null) options.headers['Authorization'] = 'Bearer $token';
        handler.next(options);
      },
      onResponse: (response, handler) async {
        if (response.statusCode == 401 &&
            !(response.requestOptions.extra['retried'] == true) &&
            !response.requestOptions.path.contains('/auth/')) {
          final ok = await _tryRefresh();
          if (ok) {
            final opts = response.requestOptions..extra['retried'] = true;
            final token = await _storage.read(key: 'access_token');
            opts.headers['Authorization'] = 'Bearer $token';
            try {
              final retry = await dio.fetch(opts);
              return handler.resolve(retry);
            } catch (_) {}
          } else {
            onSessionExpired?.call();
          }
        }
        handler.next(response);
      },
    ));
    return d;
  }

  Future<bool> _tryRefresh() async {
    final rt = await _storage.read(key: 'refresh_token');
    if (rt == null) return false;
    try {
      final res = await Dio(BaseOptions(baseUrl: baseUrl)).post(
          '/api/auth/refresh',
          data: {'refreshToken': rt},
          options: Options(validateStatus: (s) => s != null && s < 500));
      if (res.statusCode == 200) {
        await saveTokens(res.data['accessToken'], res.data['refreshToken']);
        return true;
      }
    } catch (_) {}
    await clearTokens();
    return false;
  }

  Future<void> saveTokens(String access, String refresh) async {
    await _storage.write(key: 'access_token', value: access);
    await _storage.write(key: 'refresh_token', value: refresh);
  }

  Future<String?> get accessToken => _storage.read(key: 'access_token');
  Future<String?> get refreshToken => _storage.read(key: 'refresh_token');

  Future<void> clearTokens() async {
    await _storage.delete(key: 'access_token');
    await _storage.delete(key: 'refresh_token');
  }

  static String errorMessage(Response? res,
      [String fallback = 'Something went wrong. Please try again.']) {
    final data = res?.data;
    if (data is Map && data['message'] != null)
      return data['message'].toString();
    // ASP.NET Core ProblemDetails uses title/detail rather than the application's
    // normal message envelope. Preserve that safe server explanation for operators.
    if (data is Map && data['detail'] != null) return data['detail'].toString();
    if (data is Map && data['title'] != null) return data['title'].toString();
    if (data is Map && data['errors'] is Map) {
      final errs =
          (data['errors'] as Map).values.expand((v) => v is List ? v : [v]);
      return errs.join(' ');
    }
    return fallback;
  }

  /// Converts a Dio failure into the server's safe JSON error message when it
  /// has one. This lets operational forms distinguish a validation/server
  /// failure from an actual connectivity problem.
  static String exceptionMessage(Object error,
      [String fallback = 'Could not complete the request. Please try again.']) {
    if (error is DioException) return errorMessage(error.response, fallback);
    return fallback;
  }
}
