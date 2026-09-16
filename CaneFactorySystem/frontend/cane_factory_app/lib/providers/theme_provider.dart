import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../core/app_theme.dart';

/// Light/Dark mode + configurable theme color, persisted per device.
class ThemeProvider extends ChangeNotifier {
  ThemeMode mode = ThemeMode.light;
  Color seed = AppTheme.themeColors.values.first;

  ThemeProvider() {
    _load();
  }

  Future<void> _load() async {
    final p = await SharedPreferences.getInstance();
    mode = ThemeMode.values[p.getInt('theme_mode') ?? ThemeMode.light.index];
    seed = Color(p.getInt('theme_seed') ?? AppTheme.themeColors.values.first.toARGB32());
    notifyListeners();
  }

  Future<void> setMode(ThemeMode m) async {
    mode = m;
    notifyListeners();
    (await SharedPreferences.getInstance()).setInt('theme_mode', m.index);
  }

  Future<void> setSeed(Color c) async {
    seed = c;
    notifyListeners();
    (await SharedPreferences.getInstance()).setInt('theme_seed', c.toARGB32());
  }
}
