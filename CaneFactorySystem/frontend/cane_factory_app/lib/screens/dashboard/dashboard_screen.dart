import 'package:flutter/material.dart';
import '../../core/api_client.dart';

class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key});
  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  Map<String, dynamic>? _summary;
  List _health = [];
  bool _online = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final s = await ApiClient.instance.dio.get('/api/dashboard/summary');
      final h = await ApiClient.instance.dio.get('/api/dashboard/health');
      if (mounted) {
        setState(() {
          _online = true;
          if (s.statusCode == 200) _summary = Map<String, dynamic>.from(s.data);
          if (h.statusCode == 200) _health = h.data['items'] ?? [];
        });
      }
    } catch (_) {
      if (mounted) setState(() => _online = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final cards = <(String, String, IconData)>[
      ('Today Vehicles', '${_summary?['todayVehicles'] ?? '-'}', Icons.local_shipping_outlined),
      ('Pending Tare', '${_summary?['pendingTare'] ?? '-'}', Icons.hourglass_top_outlined),
      ('Today Final Weight (Qtl)',
          _summary == null ? '-' : (_summary!['todayFinalWeightQuintal'] as num).toStringAsFixed(2),
          Icons.scale_outlined),
      ('Payment Pending', '${_summary?['paymentPending'] ?? '-'}', Icons.currency_rupee_outlined),
      ('Active Growers', '${_summary?['totalGrowers'] ?? '-'}', Icons.agriculture_outlined),
      ('Villages', '${_summary?['totalVillages'] ?? '-'}', Icons.location_city_outlined),
    ];

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(padding: const EdgeInsets.all(16), children: [
        Row(children: [
          Text('Dashboard', style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          Chip(
            avatar: Icon(_online ? Icons.wifi : Icons.wifi_off, size: 16, color: Colors.white),
            label: Text(_online ? 'Online' : 'No Internet', style: const TextStyle(color: Colors.white, fontSize: 12)),
            backgroundColor: _online ? const Color(0xFF2E7D32) : Colors.red,
          ),
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ]),
        const SizedBox(height: 12),
        Wrap(spacing: 12, runSpacing: 12, children: [
          for (final c in cards)
            SizedBox(
              width: 240,
              child: Card(
                child: Padding(
                  padding: const EdgeInsets.all(18),
                  child: Row(children: [
                    Icon(c.$3, size: 34, color: Theme.of(context).colorScheme.primary),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text(c.$2, style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w800)),
                        Text(c.$1, style: Theme.of(context).textTheme.bodySmall),
                      ]),
                    ),
                  ]),
                ),
              ),
            ),
        ]),
        const SizedBox(height: 20),
        Text('System Health (role-aware)', style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700)),
        const SizedBox(height: 8),
        Wrap(spacing: 10, runSpacing: 10, children: [
          for (final h in _health)
            Chip(
              avatar: CircleAvatar(
                  radius: 6,
                  backgroundColor: switch (h['color']) {
                    'Green' => const Color(0xFF2E7D32),
                    'Yellow' => Colors.orange,
                    _ => Colors.red,
                  }),
              label: Text('${h['name']}: ${h['value']}', style: const TextStyle(fontSize: 12)),
            ),
        ]),
      ]),
    );
  }
}
