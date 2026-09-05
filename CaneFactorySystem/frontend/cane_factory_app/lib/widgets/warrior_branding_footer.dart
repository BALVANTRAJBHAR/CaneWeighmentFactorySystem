import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

/// Shared, subtle product attribution. It contains no user or business data and is used by the
/// splash screen and authenticated shell so branding stays consistent on every platform.
class WarriorBrandingFooter extends StatelessWidget {
  const WarriorBrandingFooter(
      {super.key, this.showTagline = false, this.compact = false});

  static final Uri _businessProfile =
      Uri.parse('https://share.google/B4HHXLPuBJTF87C4n');

  final bool showTagline;
  final bool compact;

  Future<void> _openBusinessProfile() async {
    // External mode opens the platform browser/Google Business profile without embedding an
    // untrusted web view in the factory application. A failed launch is intentionally harmless.
    try {
      await launchUrl(_businessProfile, mode: LaunchMode.externalApplication);
    } catch (_) {
      // Browser/OS availability must never interrupt factory operations.
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final muted = scheme.onSurfaceVariant;
    final spacing = compact ? 2.0 : 5.0;
    return Semantics(
      container: true,
      label: 'Powered by Warrior Softech',
      child: Padding(
        padding: EdgeInsets.symmetric(
            horizontal: compact ? 12 : 20, vertical: compact ? 7 : 12),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Wrap(
            alignment: WrapAlignment.center,
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: spacing,
            children: [
              Text('POWERED BY',
                  style: TextStyle(
                      fontSize: compact ? 9 : 10,
                      fontWeight: FontWeight.w700,
                      letterSpacing: 1.1,
                      color: muted)),
              TextButton(
                onPressed: _openBusinessProfile,
                style: TextButton.styleFrom(
                  foregroundColor: scheme.primary,
                  padding:
                      const EdgeInsets.symmetric(horizontal: 4, vertical: 0),
                  minimumSize: Size.zero,
                  tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                ),
                child: Text('WARRIOR SOFTECH',
                    style: TextStyle(
                        fontSize: compact ? 11 : 13,
                        fontWeight: FontWeight.w800,
                        letterSpacing: .5)),
              ),
            ],
          ),
          if (showTagline) ...[
            const SizedBox(height: 3),
            Text('Software Development & Technology Solutions',
                textAlign: TextAlign.center,
                style: TextStyle(
                    fontSize: 11, fontWeight: FontWeight.w500, color: muted)),
          ],
        ]),
      ),
    );
  }
}
