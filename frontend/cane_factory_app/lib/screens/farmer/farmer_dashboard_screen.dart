import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/api_client.dart';
import '../../core/file_download.dart';

/// Phase 12: Farmer self-service portal. Everything here is read-only and comes from
/// /api/farmer/* which is scoped server-side to the logged-in farmer's own Grower record only.
class FarmerDashboardScreen extends StatefulWidget {
  const FarmerDashboardScreen({super.key});
  @override
  State<FarmerDashboardScreen> createState() => _FarmerDashboardScreenState();
}

class _FarmerDashboardScreenState extends State<FarmerDashboardScreen> {
  bool _loading = true;
  String? _error;
  Map<String, dynamic>? _data;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final res = await ApiClient.instance.dio.get('/api/farmer/dashboard');
      if (res.statusCode == 200 && mounted) {
        setState(() => _data = Map<String, dynamic>.from(res.data));
      } else if (mounted) {
        setState(() => _error = ApiClient.errorMessage(res));
      }
    } catch (e) {
      if (mounted) setState(() => _error = 'Could not load your dashboard: $e');
    }
    if (mounted) setState(() => _loading = false);
  }

  Widget _statCard(String label, String value, IconData icon, Color color) => Expanded(
        child: Card(
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [Icon(icon, color: color, size: 20), const SizedBox(width: 8), Expanded(child: Text(label, style: const TextStyle(fontSize: 12)))]),
              const SizedBox(height: 8),
              Text(value, style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700)),
            ]),
          ),
        ),
      );

  @override
  Widget build(BuildContext context) {
    if (_loading) return const Center(child: CircularProgressIndicator());
    if (_error != null) {
      return Center(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
          const SizedBox(height: 12),
          FilledButton(onPressed: _load, child: const Text('Retry')),
        ]),
      );
    }
    final profile = Map<String, dynamic>.from(_data!['profile']);
    final summary = Map<String, dynamic>.from(_data!['summary']);
    final recentPurchases = List<dynamic>.from(_data!['recentPurchases']);
    final recentPayments = List<dynamic>.from(_data!['recentPayments']);
    final currency = NumberFormat.currency(locale: 'en_IN', symbol: 'Rs ', decimalDigits: 2);

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(padding: const EdgeInsets.all(16), children: [
        Card(
          color: Theme.of(context).colorScheme.primaryContainer,
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(profile['growerName'] ?? '', style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700)),
              Text('Grower Code: ${profile['growerCode'] ?? '-'}  •  Village: ${profile['villageName'] ?? '-'}'),
              Text('Father: ${profile['fatherName'] ?? '-'}  •  Mobile: ${profile['mobile'] ?? '-'}'),
              if (profile['bankName'] != null) Text('Bank: ${profile['bankName']} (${profile['accountMasked'] ?? '-'})'),
            ]),
          ),
        ),
        const SizedBox(height: 16),
        Row(children: [
          _statCard('Total Vehicles', '${summary['totalVehicles']}', Icons.local_shipping_outlined, Colors.brown),
          const SizedBox(width: 10),
          _statCard('Final Weight (Qtl)', (summary['totalFinalWeight'] as num).toStringAsFixed(2), Icons.scale_outlined, Colors.teal),
          const SizedBox(width: 10),
          _statCard('Pending Payment', '${summary['pendingPayment']}', Icons.pending_actions_outlined, Colors.orange),
        ]),
        const SizedBox(height: 10),
        Row(children: [
          _statCard('Total Purchase Amount', currency.format(summary['totalPurchaseAmount']), Icons.receipt_long_outlined, Colors.indigo),
          const SizedBox(width: 10),
          _statCard('Total Paid', currency.format(summary['totalPaidAmount']), Icons.payments_outlined, Colors.green),
          const SizedBox(width: 10),
          _statCard('Loan Outstanding', currency.format(summary['totalOutstandingLoan']), Icons.savings_outlined, Colors.red),
        ]),
        const SizedBox(height: 20),
        Row(children: [
          Text('My Statement', style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          OutlinedButton.icon(
              onPressed: () => downloadAndNotify(context, '/api/farmer/statement?format=pdf', 'my-statement.pdf'),
              icon: const Icon(Icons.picture_as_pdf_outlined),
              label: const Text('PDF')),
          const SizedBox(width: 8),
          OutlinedButton.icon(
              onPressed: () => downloadAndNotify(context, '/api/farmer/statement?format=excel', 'my-statement.xlsx'),
              icon: const Icon(Icons.grid_on_outlined),
              label: const Text('Excel')),
        ]),
        const SizedBox(height: 10),
        Text('Recent Purchases', style: Theme.of(context).textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700)),
        const SizedBox(height: 6),
        Card(
          child: recentPurchases.isEmpty
              ? const Padding(padding: EdgeInsets.all(16), child: Text('No purchases yet.'))
              : Column(
                  children: [
                    for (final p in recentPurchases)
                      ListTile(
                        leading: const Icon(Icons.local_shipping_outlined),
                        title: Text('Vehicle ${p['vehicleNumber']} • ${p['finalWeightQuintal'] ?? '-'} Qtl'),
                        subtitle: Text('${p['grossDateTime']}'),
                        trailing: Chip(label: Text(p['paymentStatus'] ?? ''), visualDensity: VisualDensity.compact),
                      ),
                  ],
                ),
        ),
        const SizedBox(height: 16),
        Text('Recent Payments', style: Theme.of(context).textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700)),
        const SizedBox(height: 6),
        Card(
          child: recentPayments.isEmpty
              ? const Padding(padding: EdgeInsets.all(16), child: Text('No payments yet.'))
              : Column(
                  children: [
                    for (final p in recentPayments)
                      ListTile(
                        leading: const Icon(Icons.payments_outlined),
                        title: Text('Advice No ${p['adviceNumber']} • ${currency.format(p['netPayableAmount'])}'),
                        subtitle: Text('${p['paymentDate']}'),
                        trailing: Chip(label: Text(p['paymentStatus'] ?? ''), visualDensity: VisualDensity.compact),
                      ),
                  ],
                ),
        ),
      ]),
    );
  }
}
