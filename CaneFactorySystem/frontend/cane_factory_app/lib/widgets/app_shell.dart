import 'dart:async';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../core/api_client.dart';
import '../providers/auth_provider.dart';
import '../providers/theme_provider.dart';
import '../screens/audit/audit_screen.dart';
import '../screens/auth/change_password_screen.dart';
import '../screens/dashboard/dashboard_screen.dart';
import '../screens/developer/device_config_screen.dart';
import '../screens/developer/settings_screens.dart';
import '../screens/farmer/farmer_dashboard_screen.dart';
import '../screens/guide/user_guide_screen.dart';
import '../screens/loans/loan_screens.dart';
import '../screens/masters/grower_screen.dart';
import '../screens/masters/master_screens.dart';
import '../screens/payments/payment_screens.dart';
import '../screens/reports/reports_screen.dart';
import '../screens/sale_purchase/sale_purchase_weighment_screen.dart';
import '../screens/search/grower_search_screen.dart';
import '../screens/users/users_screen.dart';
import '../screens/weighment/weighment_screen.dart';
import 'warrior_branding_footer.dart';

class _NavItem {
  final String label;
  final IconData icon;
  final String permission;
  final Widget Function() builder;
  final String? roleOnly;
  const _NavItem(this.label, this.icon, this.permission, this.builder,
      {this.roleOnly});
}

/// Role-aware application shell. Menu items are hidden without permission,
/// but the backend independently enforces every API call regardless.
class AppShell extends StatefulWidget {
  const AppShell({super.key});
  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  int _index = 0;
  Timer? _clock;
  DateTime _now = DateTime.now();
  Map<String, dynamic>? _header;

  static final List<_NavItem> _allItems = [
    _NavItem('Dashboard', Icons.dashboard_outlined, 'Dashboard.View',
        () => const DashboardScreen()),
    _NavItem('My Dashboard', Icons.eco_outlined, 'Dashboard.View',
        () => const FarmerDashboardScreen(),
        roleOnly: 'Farmer'),
    _NavItem('Weighment', Icons.scale_outlined, 'Weighment.View',
        () => const WeighmentScreen()),
    _NavItem('Grower Search', Icons.person_search_outlined, 'Grower.View',
        () => const GrowerSearchScreen()),
    _NavItem('Growers', Icons.agriculture_outlined, 'Grower.View',
        () => const GrowerScreen()),
    _NavItem('Masters', Icons.folder_open_outlined, 'Zone.View',
        () => const MastersHubScreen()),
    _NavItem('Purchases', Icons.receipt_long_outlined, 'Purchase.View',
        () => const PurchasesScreen()),
    _NavItem('Payments', Icons.payments_outlined, 'Payment.View',
        () => const PaymentScreen()),
    _NavItem('SalePurchase Weighment', Icons.local_shipping_outlined,
        'SalePurchase.View', () => const SalePurchaseWeighmentScreen()),
    _NavItem(
        'Loans', Icons.savings_outlined, 'Loan.View', () => const LoanScreen()),
    _NavItem('Loan Recovery', Icons.currency_rupee_outlined,
        'LoanRecovery.View', () => const LoanRecoveryScreen()),
    _NavItem('Reports', Icons.summarize_outlined, 'Report.View',
        () => const ReportsScreen()),
    _NavItem('Users & Roles', Icons.group_outlined, 'User.View',
        () => const UsersScreen()),
    _NavItem('Weighing Device', Icons.settings_input_component_outlined,
        'Device.Configure', () => const DeviceConfigScreen()),
    _NavItem('Configuration', Icons.tune_outlined, 'WeightRule.Configure',
        () => const DeveloperSettingsScreen()),
    _NavItem('Audit Log', Icons.history_outlined, 'Audit.View',
        () => const AuditScreen()),
    _NavItem('User Guide', Icons.menu_book_outlined, 'UserGuide.View',
        () => const UserGuideScreen()),
  ];

  @override
  void initState() {
    super.initState();
    _clock = Timer.periodic(const Duration(seconds: 1),
        (_) => setState(() => _now = DateTime.now()));
    _loadHeader();
  }

  Future<void> _loadHeader() async {
    try {
      final res = await ApiClient.instance.dio.get('/api/dashboard/header');
      if (res.statusCode == 200 && mounted) {
        setState(() => _header = Map<String, dynamic>.from(res.data));
        // Cache for the splash screen to show instantly on next app launch.
        if (_header?['companyName'] != null) {
          SharedPreferences.getInstance().then((p) =>
              p.setString('cached_company_name', _header!['companyName']));
        }
      }
    } catch (_) {}
  }

  @override
  void dispose() {
    _clock?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final theme = context.watch<ThemeProvider>();
    final items = _allItems
        .where((i) =>
            auth.can(i.permission) &&
            (i.roleOnly == null || auth.hasRole(i.roleOnly!)))
        .toList();
    if (items.isEmpty) {
      return Scaffold(
          body: Center(
              child: Text(
                  'No modules available for your account. Contact your administrator.',
                  style: Theme.of(context).textTheme.titleMedium)));
    }
    final safeIndex = _index.clamp(0, items.length - 1);
    final wide = MediaQuery.of(context).size.width > 900;

    return Scaffold(
      appBar: AppBar(
        titleSpacing: 12,
        title: Row(children: [
          Icon(Icons.factory_outlined,
              color: Theme.of(context).colorScheme.primary),
          const SizedBox(width: 8),
          Flexible(
            child:
                Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(_header?['companyName'] ?? 'Cane Factory',
                  style: const TextStyle(
                      fontSize: 15, fontWeight: FontWeight.w700),
                  overflow: TextOverflow.ellipsis),
              Text('Season: ${_header?['season'] ?? '-'}',
                  style: const TextStyle(fontSize: 11)),
            ]),
          ),
        ]),
        actions: [
          if (wide)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text(DateFormat('dd MMM yyyy').format(_now),
                        style: const TextStyle(
                            fontSize: 12, fontWeight: FontWeight.w600)),
                    Text(DateFormat('HH:mm:ss').format(_now),
                        style: const TextStyle(fontSize: 12)),
                  ]),
            ),
          if (wide)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 6),
              child: Chip(
                avatar: const Icon(Icons.person, size: 16),
                label: Text(
                    '${auth.user?['username'] ?? ''} • ${auth.roles.join(", ")}',
                    style: const TextStyle(fontSize: 12)),
                visualDensity: VisualDensity.compact,
              ),
            ),
          IconButton(
            tooltip: 'Light / Dark mode',
            icon: Icon(theme.mode == ThemeMode.dark
                ? Icons.light_mode_outlined
                : Icons.dark_mode_outlined),
            onPressed: () => theme.setMode(theme.mode == ThemeMode.dark
                ? ThemeMode.light
                : ThemeMode.dark),
          ),
          if (wide)
            PopupMenuButton<Color>(
              tooltip: 'Theme color',
              icon: const Icon(Icons.palette_outlined),
              onSelected: theme.setSeed,
              itemBuilder: (_) => [
                for (final e in _themeColors.entries)
                  PopupMenuItem(
                      value: e.value,
                      child: Row(children: [
                        CircleAvatar(backgroundColor: e.value, radius: 8),
                        const SizedBox(width: 8),
                        Text(e.key)
                      ])),
              ],
            ),
          PopupMenuButton<String>(
            tooltip: 'Account',
            icon: const Icon(Icons.account_circle_outlined),
            onSelected: (v) {
              if (v == 'password') {
                Navigator.push(
                    context,
                    MaterialPageRoute(
                        builder: (_) => const ChangePasswordScreen()));
              } else if (v == 'logout') {
                context.read<AuthProvider>().logout();
              } else if (v == 'logoutAll') {
                context.read<AuthProvider>().logout(allSessions: true);
              }
            },
            itemBuilder: (_) => const [
              PopupMenuItem(value: 'password', child: Text('Change Password')),
              PopupMenuItem(value: 'logout', child: Text('Logout')),
              PopupMenuItem(
                  value: 'logoutAll', child: Text('Logout All Sessions')),
            ],
          ),
          const SizedBox(width: 8),
        ],
      ),
      drawer: wide
          ? null
          : Drawer(
              child: SafeArea(
                child: ListView(
                  children: [
                    const DrawerHeader(
                        child: Text('Navigation',
                            style: TextStyle(
                                fontSize: 22, fontWeight: FontWeight.w700))),
                    for (var i = 0; i < items.length; i++)
                      ListTile(
                        leading: Icon(items[i].icon),
                        title: Text(items[i].label),
                        selected: i == safeIndex,
                        onTap: () {
                          setState(() => _index = i);
                          Navigator.pop(context);
                        },
                      ),
                  ],
                ),
              ),
            ),
      body: Column(children: [
        Expanded(
          child: wide
              ? Row(children: [
                  NavigationRail(
                    selectedIndex: safeIndex,
                    onDestinationSelected: (i) => setState(() => _index = i),
                    labelType: NavigationRailLabelType.all,
                    minWidth: 84,
                    scrollable: true,
                    destinations: [
                      for (final i in items)
                        NavigationRailDestination(
                            icon: Icon(i.icon),
                            label: Text(i.label, textAlign: TextAlign.center)),
                    ],
                  ),
                  const VerticalDivider(width: 1),
                  Expanded(child: items[safeIndex].builder()),
                ])
              : SafeArea(top: false, child: items[safeIndex].builder()),
        ),
        const SafeArea(top: false, child: WarriorBrandingFooter(compact: true)),
      ]),
    );
  }

  static const _themeColors = <String, Color>{
    'Forest Green': Color(0xFF1B5E20),
    'Steel Blue': Color(0xFF1565C0),
    'Deep Teal': Color(0xFF00695C),
    'Indigo': Color(0xFF283593),
    'Maroon': Color(0xFF6D1B24),
    'Amber Brown': Color(0xFF8D5B00),
  };
}
