import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../providers/live_weight_provider.dart';

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
    context.read<LiveWeightProvider>().start();
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
    final liveWeight = context.watch<LiveWeightProvider>().current;
    final effectiveHealth = _health.map((raw) {
      final item = Map<String, dynamic>.from(raw as Map);
      if (item['name'] == 'Digitizer' && liveWeight.deviceConnected) {
        item['value'] =
            'Connected (${liveWeight.deviceName ?? 'Local Scale Bridge'})';
        item['color'] = 'Green';
      }
      return item;
    }).toList();
    final cards = <(String, String, IconData)>[
      (
        'Today Vehicles',
        '${_summary?['todayVehicles'] ?? '-'}',
        Icons.local_shipping_outlined
      ),
      (
        'Pending Tare',
        '${_summary?['pendingTare'] ?? '-'}',
        Icons.hourglass_top_outlined
      ),
      (
        'Today Final Weight (Qtl)',
        _summary == null
            ? '-'
            : (_summary!['todayFinalWeightQuintal'] as num).toStringAsFixed(2),
        Icons.scale_outlined
      ),
      (
        'Payment Pending',
        '${_summary?['paymentPending'] ?? '-'}',
        Icons.currency_rupee_outlined
      ),
      (
        'Active Growers',
        '${_summary?['totalGrowers'] ?? '-'}',
        Icons.agriculture_outlined
      ),
      (
        'Villages',
        '${_summary?['totalVillages'] ?? '-'}',
        Icons.location_city_outlined
      ),
    ];

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(padding: const EdgeInsets.all(16), children: [
        Row(children: [
          Text('Dashboard',
              style: Theme.of(context)
                  .textTheme
                  .headlineSmall
                  ?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          Chip(
            avatar: Icon(_online ? Icons.wifi : Icons.wifi_off,
                size: 16, color: Colors.white),
            label: Text(_online ? 'Online' : 'No Internet',
                style: const TextStyle(color: Colors.white, fontSize: 12)),
            backgroundColor: _online ? const Color(0xFF2E7D32) : Colors.red,
          ),
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ]),
        const SizedBox(height: 12),
        LayoutBuilder(builder: (context, constraints) {
          return GridView.builder(
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            itemCount: cards.length,
            gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
              maxCrossAxisExtent: 260,
              mainAxisExtent: 118,
              crossAxisSpacing: 10,
              mainAxisSpacing: 10,
            ),
            itemBuilder: (_, index) {
              final c = cards[index];
              return Card(
                clipBehavior: Clip.antiAlias,
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Row(children: [
                    Container(
                      width: 42,
                      height: 42,
                      decoration: BoxDecoration(
                        color: Theme.of(context)
                            .colorScheme
                            .primary
                            .withValues(alpha: 0.12),
                        borderRadius: BorderRadius.circular(12),
                      ),
                      child: Icon(c.$3,
                          size: 26,
                          color: Theme.of(context).colorScheme.primary),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          FittedBox(
                            fit: BoxFit.scaleDown,
                            alignment: Alignment.centerLeft,
                            child: Text(c.$2,
                                style: Theme.of(context)
                                    .textTheme
                                    .titleLarge
                                    ?.copyWith(fontWeight: FontWeight.w800)),
                          ),
                          const SizedBox(height: 3),
                          Text(c.$1,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                              style: Theme.of(context)
                                  .textTheme
                                  .bodySmall
                                  ?.copyWith(height: 1.15)),
                        ],
                      ),
                    ),
                  ]),
                ),
              );
            },
          );
        }),
        const SizedBox(height: 20),
        Text('System Health (role-aware)',
            style: Theme.of(context)
                .textTheme
                .titleMedium
                ?.copyWith(fontWeight: FontWeight.w700)),
        const SizedBox(height: 8),
        GridView.builder(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          itemCount: effectiveHealth.length,
          gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
            maxCrossAxisExtent: 430,
            mainAxisExtent: 64,
            crossAxisSpacing: 10,
            mainAxisSpacing: 10,
          ),
          itemBuilder: (_, index) {
            final h = effectiveHealth[index];
            final statusColor = switch (h['color']) {
              'Green' => const Color(0xFF2E7D32),
              'Yellow' => Colors.orange,
              _ => Colors.red,
            };
            return Card(
              margin: EdgeInsets.zero,
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 12),
                child: Row(children: [
                  Container(
                    width: 10,
                    height: 10,
                    decoration: BoxDecoration(
                        color: statusColor, shape: BoxShape.circle),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('${h['name']}',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                                fontSize: 11, fontWeight: FontWeight.w700)),
                        Text('${h['value']}',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: Theme.of(context).textTheme.bodySmall),
                      ],
                    ),
                  ),
                  Icon(Icons.circle, size: 7, color: statusColor),
                ]),
              ),
            );
          },
        ),
      ]),
    );
  }
}
