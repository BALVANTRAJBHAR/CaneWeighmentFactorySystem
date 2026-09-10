import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/api_client.dart';
import '../../widgets/master_crud.dart';
import '../loans/loan_screens.dart';

/// Hub with tabs for all master forms (each tab is the generic permission-aware CRUD screen).
class MastersHubScreen extends StatelessWidget {
  const MastersHubScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final tabs = <String, Widget>{
      'Zones': const MasterCrudScreen(
        title: 'Zone', module: 'Zone', endpoint: '/api/zones',
        fields: [
          FieldSpec('zoneCode', 'Zone Code', hint: 'Example: Z1', required: false, maxLength: 20),
          FieldSpec('zoneName', 'Zone Name', hint: 'Example: North Zone', maxLength: 100, hindiKey: 'zoneNameHi'),
          FieldSpec('description', 'Description', hint: 'Optional description', required: false, maxLength: 250),
        ],
        columns: [ColumnSpec('id', 'Zone ID'), ColumnSpec('zoneCode', 'Code'), ColumnSpec('zoneName', 'Zone Name')],
      ),
      'Villages': const MasterCrudScreen(
        title: 'Village', module: 'Village', endpoint: '/api/villages',
        fields: [
          FieldSpec('zoneId', 'Zone', type: FieldType.dropdown, optionsEndpoint: '/api/zones', optionLabelKey: 'zoneName'),
          FieldSpec('villageName', 'Village Name', hint: 'Example: Rampur', maxLength: 100, hindiKey: 'villageNameHi'),
          FieldSpec('pradhanName', 'Pradhan Name', hint: 'Example: Suresh Singh', required: false, maxLength: 100),
          FieldSpec('mobile', 'Mobile', hint: 'Example: 9876543210', type: FieldType.mobile, required: false),
          FieldSpec('email', 'Email', hint: 'Example: pradhan@gmail.com', type: FieldType.email, required: false),
        ],
        columns: [
          ColumnSpec('id', 'Village ID'), ColumnSpec('villageName', 'Village'),
          ColumnSpec('zoneName', 'Zone'), ColumnSpec('pradhanName', 'Pradhan'), ColumnSpec('mobile', 'Mobile'),
        ],
      ),
      'Banks': const MasterCrudScreen(
        title: 'Bank', module: 'Bank', endpoint: '/api/banks',
        fields: [
          FieldSpec('bankName', 'Bank Name', hint: 'Example: State Bank of India', maxLength: 100),
          FieldSpec('branchName', 'Branch Name', hint: 'Example: Rampur Branch', maxLength: 100),
          FieldSpec('ifsc', 'IFSC', hint: 'Example: SBIN0001234', maxLength: 11),
          FieldSpec('address', 'Address', hint: 'Branch address', required: false, maxLength: 250),
          FieldSpec('managerName', 'Manager Name', required: false, maxLength: 100),
          FieldSpec('managerMobile', 'Manager Mobile', hint: 'Example: 9876543210', type: FieldType.mobile, required: false),
          FieldSpec('managerEmail', 'Manager Email', hint: 'Example: manager@sbi.co.in', type: FieldType.email, required: false),
        ],
        columns: [
          ColumnSpec('id', 'ID'), ColumnSpec('bankName', 'Bank'), ColumnSpec('branchName', 'Branch'),
          ColumnSpec('ifsc', 'IFSC'), ColumnSpec('managerName', 'Manager'),
        ],
      ),
      'Vehicle Types': const MasterCrudScreen(
        title: 'Vehicle Type', module: 'Vehicle', endpoint: '/api/vehicle-types',
        fields: [FieldSpec('vehicleTypeName', 'Vehicle Type Name', hint: 'Example: Truck', maxLength: 50, hindiKey: 'vehicleTypeNameHi')],
        columns: [ColumnSpec('id', 'ID'), ColumnSpec('vehicleTypeName', 'Vehicle Type')],
      ),
      'Variety Types': const MasterCrudScreen(
        title: 'Variety Type', module: 'VarietyType', endpoint: '/api/variety-types',
        fields: [FieldSpec('varietyTypeName', 'Variety Type Name', hint: 'Example: Early', maxLength: 50)],
        columns: [ColumnSpec('id', 'ID'), ColumnSpec('varietyTypeName', 'Variety Type')],
      ),
      'Varieties': const MasterCrudScreen(
        title: 'Variety', module: 'Variety', endpoint: '/api/varieties',
        fields: [
          FieldSpec('varietyTypeId', 'Variety Type', type: FieldType.dropdown,
              optionsEndpoint: '/api/variety-types', optionLabelKey: 'varietyTypeName'),
          FieldSpec('varietyName', 'Variety Name', hint: 'Example: CO 18', maxLength: 50),
        ],
        columns: [ColumnSpec('id', 'ID'), ColumnSpec('varietyTypeName', 'Variety Type'), ColumnSpec('varietyName', 'Variety')],
      ),
      'Rates': const RateScreen(),
      'Items': const MasterCrudScreen(
        title: 'Item', module: 'Item', endpoint: '/api/items',
        fields: [FieldSpec('itemName', 'Item Name', hint: 'Example: Sugar', maxLength: 50, hindiKey: 'itemNameHi')],
        columns: [ColumnSpec('id', 'ID'), ColumnSpec('itemName', 'Item')],
      ),
      'Parties': const MasterCrudScreen(
        title: 'Party', module: 'Party', endpoint: '/api/parties',
        fields: [
          FieldSpec('partyName', 'Party Name', hint: 'Example: Gupta Traders', maxLength: 100, hindiKey: 'partyNameHi'),
          FieldSpec('mobile', 'Mobile', hint: 'Example: 9876543210', type: FieldType.mobile),
          FieldSpec('email', 'Email', hint: 'Example: party@gmail.com', type: FieldType.email, required: false),
          FieldSpec('address', 'Address', required: false, maxLength: 250),
          FieldSpec('gst', 'GST Number', hint: 'Optional GSTIN', required: false, maxLength: 15),
        ],
        columns: [
          ColumnSpec('id', 'ID'), ColumnSpec('partyName', 'Party'),
          ColumnSpec('mobile', 'Mobile'), ColumnSpec('gst', 'GST'),
        ],
      ),
      'Seasons': const MasterCrudScreen(
        title: 'Season', module: 'Season', endpoint: '/api/seasons',
        fields: [
          FieldSpec('seasonName', 'Season Name', hint: 'Example: 2026-27', maxLength: 20),
          FieldSpec('startDate', 'Start Date (yyyy-MM-dd)', hint: 'Example: 2026-10-01'),
          FieldSpec('isActive', 'Active Season', type: FieldType.toggle, required: false),
        ],
        columns: [ColumnSpec('id', 'ID'), ColumnSpec('seasonName', 'Season'), ColumnSpec('isActive', 'Active')],
      ),
      'Payment Modes': const MasterCrudScreen(
        title: 'Payment Mode', module: 'PaymentMode', endpoint: '/api/payment-modes',
        fields: [
          FieldSpec('modeCode', 'Mode Code', hint: 'Example: CASH', maxLength: 20),
          FieldSpec('modeName', 'Mode Name', hint: 'Example: Cash', maxLength: 50),
        ],
        columns: [ColumnSpec('id', 'ID'), ColumnSpec('modeCode', 'Code'), ColumnSpec('modeName', 'Name')],
      ),
      'Loan Types': const LoanTypesScreen(),
    };

    return DefaultTabController(
      length: tabs.length,
      child: Column(children: [
        Material(
          color: Theme.of(context).colorScheme.surface,
          child: TabBar(isScrollable: true, tabs: [for (final t in tabs.keys) Tab(text: t)]),
        ),
        Expanded(child: TabBarView(children: tabs.values.toList())),
      ]),
    );
  }
}

/// Rate Master: versioned effective-dated rates (no in-place edits of history).
class RateScreen extends StatefulWidget {
  const RateScreen({super.key});
  @override
  State<RateScreen> createState() => _RateScreenState();
}

class _RateScreenState extends State<RateScreen> {
  List _items = [];
  List _varietyTypes = [];
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final r1 = await ApiClient.instance.dio.get('/api/rates', queryParameters: {'includeInactive': true});
      final r2 = await ApiClient.instance.dio.get('/api/variety-types');
      if (r1.statusCode == 200) _items = r1.data['items'];
      if (r2.statusCode == 200) _varietyTypes = r2.data['items'];
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  String _date(dynamic value) {
    if (value == null) return 'Open';
    return DateFormat('dd-MM-yyyy').format(DateTime.parse(value.toString()).toLocal());
  }

  Future<void> _newRate({Map<String, dynamic>? revisionOf}) async {
    int? typeId = revisionOf?['varietyTypeId'] as int?;
    final rateCtl = TextEditingController(
        text: revisionOf == null ? '' : '${revisionOf['rate']}');
    DateTime from = DateTime.now();
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setD) => AlertDialog(
          title: Text(revisionOf == null ? 'New Rate Period' : 'Revise Rate Period'),
          content: SizedBox(
            width: 400,
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              DropdownButtonFormField<int>(
                decoration: const InputDecoration(labelText: 'Variety Type', hintText: 'Select Variety Type'),
                items: [for (final v in _varietyTypes) DropdownMenuItem(value: v['id'] as int, child: Text(v['varietyTypeName']))],
                onChanged: revisionOf == null ? (v) => typeId = v : null,
              ),
              const SizedBox(height: 12),
              TextField(
                  controller: rateCtl,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  decoration: const InputDecoration(labelText: 'Rate (per Quintal)', hintText: 'Example: 375.00')),
              const SizedBox(height: 12),
              Row(children: [
                Expanded(child: Text('Effective From: ${DateFormat('dd-MM-yyyy').format(from)}')),
                TextButton(
                    onPressed: () async {
                      final d = await showDatePicker(
                          context: ctx, initialDate: from, firstDate: DateTime(2020), lastDate: DateTime(2035));
                      if (d != null) setD(() => from = d);
                    },
                    child: const Text('Pick Date')),
              ]),
            ]),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: Text(revisionOf == null ? 'Save' : 'Save Revision')),
          ],
        ),
      ),
    );
    if (ok != true || typeId == null) return;
    final res = await ApiClient.instance.dio.post('/api/rates', data: {
      'varietyTypeId': typeId,
      'rate': double.tryParse(rateCtl.text) ?? 0,
      'effectiveFrom': from.toIso8601String(),
    });
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(res.statusCode == 200 ? res.data['message'] : ApiClient.errorMessage(res)),
        backgroundColor: res.statusCode == 200 ? const Color(0xFF2E7D32) : Theme.of(context).colorScheme.error));
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Row(children: [
          Text('Rate Master (versioned)', style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          FilledButton.icon(onPressed: _newRate, icon: const Icon(Icons.add), label: const Text('New Rate Period')),
        ]),
        const SizedBox(height: 10),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : SingleChildScrollView(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(columns: const [
                      DataColumn(label: Text('ID')),
                      DataColumn(label: Text('Variety Type')),
                      DataColumn(label: Text('Rate')),
                      DataColumn(label: Text('Effective From')),
                      DataColumn(label: Text('Effective To')),
                      DataColumn(label: Text('Action')),
                    ], rows: [
                      for (final r in _items)
                        DataRow(cells: [
                          DataCell(Text('${r['id']}')),
                          DataCell(Text('${r['varietyTypeName']}')),
                          DataCell(Text((r['rate'] as num).toStringAsFixed(2))),
                          DataCell(Text(_date(r['effectiveFrom']))),
                          DataCell(Text(_date(r['effectiveTo']))),
                          DataCell(TextButton.icon(
                            onPressed: () => _newRate(revisionOf: Map<String, dynamic>.from(r)),
                            icon: const Icon(Icons.edit_calendar_outlined, size: 16),
                            label: const Text('Revise'),
                          )),
                        ]),
                    ]),
                    ),
                  ),
          ),
        ),
      ]),
    );
  }
}

/// Purchases list with lifecycle/status view.
class PurchasesScreen extends StatefulWidget {
  const PurchasesScreen({super.key});
  @override
  State<PurchasesScreen> createState() => _PurchasesScreenState();
}

class _PurchasesScreenState extends State<PurchasesScreen> {
  List _items = [];
  bool _loading = true;
  String _growerCode = '';

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final res = await ApiClient.instance.dio.get('/api/purchases',
          queryParameters: {if (_growerCode.isNotEmpty) 'growerCode': _growerCode});
      if (res.statusCode == 200) _items = res.data['items'];
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Row(children: [
          Text('Purchases', style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          SizedBox(
            width: 220,
            child: TextField(
              decoration: const InputDecoration(hintText: 'Grower Code e.g. 101/1', prefixIcon: Icon(Icons.search, size: 18)),
              onSubmitted: (v) {
                _growerCode = v.trim();
                _load();
              },
            ),
          ),
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ]),
        const SizedBox(height: 10),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : SingleChildScrollView(
                    scrollDirection: Axis.vertical,
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(columns: const [
                        DataColumn(label: Text('Purchase ID')),
                        DataColumn(label: Text('Grower')),
                        DataColumn(label: Text('Village')),
                        DataColumn(label: Text('Vehicle')),
                        DataColumn(label: Text('Gross Qtl')),
                        DataColumn(label: Text('Tare Qtl')),
                        DataColumn(label: Text('Final Qtl')),
                        DataColumn(label: Text('Rate')),
                        DataColumn(label: Text('Amount')),
                        DataColumn(label: Text('Status')),
                        DataColumn(label: Text('Payment')),
                      ], rows: [
                        for (final p in _items)
                          DataRow(cells: [
                            DataCell(Text('${p['purchaseId']}')),
                            DataCell(Text('${p['growerCode']} ${p['growerName']}')),
                            DataCell(Text('${p['villageName']}')),
                            DataCell(Text('${p['vehicleNumber']}')),
                            DataCell(Text((p['grossWeightQuintal'] as num).toStringAsFixed(2))),
                            DataCell(Text(p['tareWeightQuintal'] == null ? '-' : (p['tareWeightQuintal'] as num).toStringAsFixed(2))),
                            DataCell(Text(p['finalWeightQuintal'] == null ? '-' : (p['finalWeightQuintal'] as num).toStringAsFixed(2))),
                            DataCell(Text((p['rate'] as num).toStringAsFixed(2))),
                            DataCell(Text(p['purchaseAmount'] == null ? '-' : (p['purchaseAmount'] as num).toStringAsFixed(2))),
                            DataCell(_statusChip(p['grossTareStatus'])),
                            DataCell(_statusChip(p['paymentStatus'])),
                          ]),
                      ]),
                    ),
                  ),
          ),
        ),
      ]),
    );
  }

  Widget _statusChip(String? s) {
    final color = switch (s) {
      'GROSS_DONE' => Colors.orange,
      'TARE_DONE' => const Color(0xFF2E7D32),
      'PENDING' => Colors.orange,
      'PAID' => const Color(0xFF2E7D32),
      'CANCELLED' => Colors.red,
      _ => Colors.grey,
    };
    return Chip(
        label: Text(s ?? '-', style: const TextStyle(fontSize: 10, color: Colors.white)),
        backgroundColor: color,
        visualDensity: VisualDensity.compact);
  }
}
