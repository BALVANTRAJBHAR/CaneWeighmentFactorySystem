import 'package:flutter/foundation.dart';
import '../core/api_client.dart';

class AuthProvider extends ChangeNotifier {
  Map<String, dynamic>? user;
  bool loading = false;
  bool get isLoggedIn => user != null;
  bool get mustChangePassword => user?['mustChangePassword'] == true;
  List<String> get roles => List<String>.from(user?['roles'] ?? []);
  List<String> get permissions => List<String>.from(user?['permissions'] ?? []);

  bool can(String permission) => permissions.contains(permission);
  bool hasRole(String role) => roles.contains(role);

  AuthProvider() {
    ApiClient.instance.onSessionExpired = () {
      user = null;
      notifyListeners();
    };
  }

  Future<String?> login(String username, String password) async {
    final isFarmerMobile = RegExp(r'^\d{10}$').hasMatch(username.trim());
    final uri = ApiClient.baseUri;
    final isLoopback = uri != null &&
        (uri.host == 'localhost' ||
            uri.host == '127.0.0.1' ||
            uri.host == '::1');
    if (isFarmerMobile && uri?.scheme != 'https' && !isLoopback) {
      return 'Farmer mobile-number login requires an HTTPS API URL.';
    }
    loading = true;
    notifyListeners();
    try {
      final res = await ApiClient.instance.dio.post('/api/auth/login', data: {
        'username': username,
        'password': password,
        'deviceInfo': defaultTargetPlatform.name,
      });
      if (res.statusCode == 200) {
        await ApiClient.instance
            .saveTokens(res.data['accessToken'], res.data['refreshToken']);
        user = Map<String, dynamic>.from(res.data['user']);
        user!['mustChangePassword'] = res.data['mustChangePassword'];
        return null;
      }
      return ApiClient.errorMessage(res, 'Login failed.');
    } catch (e) {
      return ApiClient.exceptionMessage(
          e, 'Cannot reach the server. Check network/API URL.');
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<bool> tryRestoreSession() async {
    final token = await ApiClient.instance.accessToken;
    if (token == null) return false;
    try {
      final res = await ApiClient.instance.dio.get('/api/auth/me');
      if (res.statusCode == 200) {
        user = Map<String, dynamic>.from(res.data);
        notifyListeners();
        return true;
      }
    } catch (_) {}
    return false;
  }

  Future<String?> changePassword(
      String current, String newPwd, String confirm) async {
    try {
      final res = await ApiClient.instance.dio.post('/api/auth/change-password',
          data: {
            'currentPassword': current,
            'newPassword': newPwd,
            'confirmPassword': confirm
          });
      if (res.statusCode == 200) {
        await ApiClient.instance
            .saveTokens(res.data['accessToken'], res.data['refreshToken']);
        user = Map<String, dynamic>.from(res.data['user']);
        user!['mustChangePassword'] = res.data['mustChangePassword'];
        notifyListeners();
        return null;
      }
      return ApiClient.errorMessage(res);
    } catch (e) {
      return ApiClient.exceptionMessage(e);
    }
  }

  Future<void> logout({bool allSessions = false}) async {
    try {
      final rt = await ApiClient.instance.refreshToken;
      if (allSessions) {
        await ApiClient.instance.dio.post('/api/auth/logout-all');
      } else if (rt != null) {
        await ApiClient.instance.dio
            .post('/api/auth/logout', data: {'refreshToken': rt});
      }
    } catch (_) {}
    await ApiClient.instance.clearTokens();
    user = null;
    notifyListeners();
  }
}
