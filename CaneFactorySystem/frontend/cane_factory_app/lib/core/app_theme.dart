import 'package:flutter/material.dart';

/// Enterprise theme: trustworthy industrial palette, subtle elevation, strong contrast
/// in both light and dark modes. Seed color is user-configurable.
class AppTheme {
  static ThemeData light(Color seed) => _base(ColorScheme.fromSeed(seedColor: seed, brightness: Brightness.light));
  static ThemeData dark(Color seed) => _base(ColorScheme.fromSeed(seedColor: seed, brightness: Brightness.dark));

  static ThemeData _base(ColorScheme scheme) {
    final isDark = scheme.brightness == Brightness.dark;
    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      scaffoldBackgroundColor: isDark ? const Color(0xFF12151A) : const Color(0xFFF4F6F8),
      cardTheme: CardThemeData(
        elevation: 1.5,
        shadowColor: Colors.black.withValues(alpha: isDark ? 0.5 : 0.15),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        margin: const EdgeInsets.all(6),
      ),
      inputDecorationTheme: InputDecorationTheme(
        isDense: true,
        filled: true,
        fillColor: isDark ? const Color(0xFF1C2129) : Colors.white,
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
        contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
        ),
      ),
      dataTableTheme: DataTableThemeData(
        headingRowColor: WidgetStatePropertyAll(scheme.primary.withValues(alpha: isDark ? 0.25 : 0.08)),
        headingTextStyle: TextStyle(fontWeight: FontWeight.w700, fontSize: 13, color: scheme.onSurface),
        dataTextStyle: TextStyle(fontSize: 13, color: scheme.onSurface),
        dividerThickness: 0.4,
      ),
      appBarTheme: AppBarTheme(
        elevation: 0,
        backgroundColor: isDark ? const Color(0xFF171B22) : Colors.white,
        foregroundColor: scheme.onSurface,
      ),
      snackBarTheme: const SnackBarThemeData(behavior: SnackBarBehavior.floating),
      visualDensity: VisualDensity.compact,
    );
  }

  static const themeColors = <String, Color>{
    'Forest Green': Color(0xFF1B5E20),
    'Steel Blue': Color(0xFF1565C0),
    'Deep Teal': Color(0xFF00695C),
    'Indigo': Color(0xFF283593),
    'Maroon': Color(0xFF6D1B24),
    'Amber Brown': Color(0xFF8D5B00),
  };
}
