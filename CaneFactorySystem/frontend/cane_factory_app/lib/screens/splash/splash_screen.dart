import 'package:flutter/material.dart';
import '../../widgets/warrior_branding_footer.dart';

/// Modern enterprise splash screen. Purely presentational - the startup sequence
/// (config -> API check -> session check) is orchestrated by [RootGate] in main.dart;
/// this widget only reflects the current [statusText]/[hasError] and offers [onRetry].
/// No secrets/credentials are ever shown here.
class SplashScreen extends StatefulWidget {
  final String statusText;
  final bool hasError;
  final VoidCallback? onRetry;
  final String? companyName;

  const SplashScreen(
      {super.key,
      required this.statusText,
      this.hasError = false,
      this.onRetry,
      this.companyName});

  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  late final Animation<double> _fade;
  late final Animation<double> _scale;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
        vsync: this, duration: const Duration(milliseconds: 700));
    _fade = CurvedAnimation(
        parent: _controller,
        curve: const Interval(0.0, 0.7, curve: Curves.easeOut));
    _scale = Tween(begin: 0.85, end: 1.0).animate(
        CurvedAnimation(parent: _controller, curve: Curves.easeOutBack));
    _controller.forward();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final bg = isDark
        ? const LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: [Color(0xFF0D1B1E), Color(0xFF16232B), Color(0xFF0F1A20)])
        : const LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: [Color(0xFFEFF5F4), Color(0xFFE3ECEC), Color(0xFFEDF1F5)]);
    const accent = Color(0xFF00695C);
    final textColor = isDark ? Colors.white : const Color(0xFF12232A);

    return Scaffold(
      body: Container(
        decoration: BoxDecoration(gradient: bg),
        child: SafeArea(
          child: Column(children: [
            Expanded(
              child: Center(
                child: FadeTransition(
                  opacity: _fade,
                  child: ScaleTransition(
                    scale: _scale,
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Container(
                          width: 128,
                          height: 128,
                          decoration: BoxDecoration(
                            color: Colors.white,
                            shape: BoxShape.circle,
                            boxShadow: [
                              BoxShadow(
                                  color: accent.withValues(
                                      alpha: isDark ? 0.35 : 0.22),
                                  blurRadius: 32,
                                  spreadRadius: 2),
                            ],
                          ),
                          padding: const EdgeInsets.all(22),
                          child: Image.asset('assets/images/app_logo.png',
                              fit: BoxFit.contain),
                        ),
                        const SizedBox(height: 28),
                        Text('Cane Factory Management System',
                            textAlign: TextAlign.center,
                            style: TextStyle(
                                fontSize: 22,
                                fontWeight: FontWeight.w800,
                                color: textColor,
                                letterSpacing: 0.2)),
                        if (widget.companyName != null &&
                            widget.companyName!.isNotEmpty) ...[
                          const SizedBox(height: 6),
                          Text(widget.companyName!,
                              style: TextStyle(
                                  fontSize: 14,
                                  color: textColor.withValues(alpha: 0.65),
                                  fontWeight: FontWeight.w500)),
                        ],
                        const SizedBox(height: 40),
                        if (!widget.hasError)
                          const SizedBox(
                            width: 34,
                            height: 34,
                            child: CircularProgressIndicator(
                                strokeWidth: 3, color: accent),
                          )
                        else
                          Icon(Icons.cloud_off_outlined,
                              size: 34,
                              color: Theme.of(context).colorScheme.error),
                        const SizedBox(height: 16),
                        Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 32),
                          child: Text(widget.statusText,
                              textAlign: TextAlign.center,
                              style: TextStyle(
                                  fontSize: 13,
                                  color: widget.hasError
                                      ? Theme.of(context).colorScheme.error
                                      : textColor.withValues(alpha: 0.7))),
                        ),
                        if (widget.hasError && widget.onRetry != null) ...[
                          const SizedBox(height: 18),
                          FilledButton.icon(
                            onPressed: widget.onRetry,
                            icon: const Icon(Icons.refresh),
                            label: const Text('Retry'),
                            style:
                                FilledButton.styleFrom(backgroundColor: accent),
                          ),
                        ],
                      ],
                    ),
                  ),
                ),
              ),
            ),
            const WarriorBrandingFooter(showTagline: true),
          ]),
        ),
      ),
    );
  }
}
