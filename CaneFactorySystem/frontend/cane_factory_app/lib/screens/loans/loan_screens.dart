import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/print_service.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/master_crud.dart';

/// Loan Type master (Phase 8) - reuses the generic permission-aware CRUD engine.
class LoanTypesScreen extends StatelessWidget {
  const LoanTypesScreen({super.key});
  @override
  Widget build(BuildContext context) => const MasterCrudScreen(
        title: 'Loan Type',
        module: 'LoanType',
        endpoint: '/api/loan-types',
        fields: [
          FieldSpec('loanTypeName', 'Loan Type Name', hint: 'Example: Fertilizer Loan', maxLength: 80),
          FieldSpec('description', 'Description', hint: 'Optional description', required: false, maxLength: 250),
        ],
        columns: [ColumnSpec('id', 'ID'), ColumnSpec('loanTypeName', 'Loan Type'), ColumnSpec('description', 'Description')],
      );
}

/// Issue Loan + outstanding lookup + Loan register with Cancel action (Phase 8).
class LoanScreen extends StatefulWidget {
  const LoanScreen({super.key});
  @override
  State<LoanScreen> createState() => _LoanScreenState();
}

class _LoanScreenState extends State<LoanScreen> {
  final _growerCode = TextEditingController();
  Map<String, dynamic>? _grower;
  String? _growerError;
  int? _loanTypeId;
  final _amount = TextEditingController();
  final _remarks = TextEditingController();
  List _loanTypes = [];
  List _items = [];
  bool _loading = true;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    _loadTypes();
    _load();
  }

  Future<void> _loadTypes() async {
    try {
      final res = await ApiClient.instance.dio.get('/api/loan-types');
      if (res.statusCode == 200 && mounted) setState(() => _loanTypes = res.data['items']);
    } catch (_) {}
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final res = await ApiClient.instance.dio.get('/api/loans');
      if (res.statusCode == 200 && mounted) setState(() => _items = res.data['items']);
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  void _toast(String msg, {bool error = false}) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(msg),
        backgroundColor: error ? Theme.of(context).colorScheme.error : const Color(0xFF2E7D32)));
  }

  Future<void> _lookupGrower() async {
    setState(() {
      _grower = null;
      _growerError = null;
    });
    final res = await ApiClient.instance.dio
        .get('/api/growers/by-code', queryParameters: {'code': _growerCode.text.trim()});
    setState(() {
      if (res.statusCode == 200) {
        _grower = Map<String, dynamic>.from(res.data);
      } else {
        _growerError = ApiClient.errorMessage(res);
      }
    });
  }

  Future<void> _issue() async {
    if (_grower == null) return _toast('Lookup a valid Grower Code first (press ENTER).', error: true);
    if (_loanTypeId == null) return _toast('Select Loan Type.', error: true);
    final amt = double.tryParse(_amount.text) ?? 0;
    if (amt <= 0) return _toast('Enter a valid Loan Amount greater than 0.', error: true);
    setState(() => _saving = true);
    final res = await ApiClient.instance.dio.post('/api/loans', data: {
      'growerCode': _grower!['growerCode'],
      'loanTypeId': _loanTypeId,
      'loanAmount': amt,
      'remarks': _remarks.text.trim().isEmpty ? null : _remarks.text.trim(),
      'idempotencyKey': 'loan-${_grower!['growerCode']}-${DateTime.now().millisecondsSinceEpoch ~/ 30000}',
    });
    setState(() => _saving = false);
    if (res.statusCode == 200) {
      _toast(res.data['message']);
      setState(() {
        _grower = null;
        _growerCode.clear();
        _amount.clear();
        _remarks.clear();
        _loanTypeId = null;
      });
      _load();
      final autoPrint = res.data['autoPrint'];
      if (autoPrint != null) {
        final outcome = await PrintService.printDocument(
          documentUrl: autoPrint['documentUrl'],
          printerType: autoPrint['printerType'] ?? 'DotMatrix',
          printerName: autoPrint['printerName'] ?? '',
          copies: autoPrint['copies'] ?? 1,
        );
        if (mounted) _toast(outcome.message, error: !outcome.success);
      }
    } else {
      _toast(ApiClient.errorMessage(res), error: true);
    }
  }

  Future<void> _cancelLoan(int loanId) async {
    final reasonCtl = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('Cancel Loan $loanId?'),
        content: TextField(
            controller: reasonCtl,
            decoration: const InputDecoration(labelText: 'Cancellation Reason (min 5 characters)')),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Back')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Cancel Loan')),
        ],
      ),
    );
    if (ok != true) return;
    final res = await ApiClient.instance.dio
        .post('/api/loans/$loanId/cancel', data: {'reason': reasonCtl.text.trim()});
    if (!mounted) return;
    _toast(res.statusCode == 200 ? res.data['message'] : ApiClient.errorMessage(res), error: res.statusCode != 200);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final canCreate = auth.can('Loan.Create');
    final canCancel = auth.can('Loan.Cancel');
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        if (canCreate)
          Card(
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('ISSUE LOAN', style: TextStyle(fontWeight: FontWeight.w800, color: Theme.of(context).colorScheme.primary)),
                const SizedBox(height: 10),
                Wrap(spacing: 12, runSpacing: 12, crossAxisAlignment: WrapCrossAlignment.center, children: [
                  SizedBox(
                    width: 200,
                    child: TextField(
                      controller: _growerCode,
                      decoration: const InputDecoration(
                          labelText: 'Grower Code', hintText: 'Example: 101/1', prefixIcon: Icon(Icons.badge_outlined, size: 18)),
                      onSubmitted: (_) => _lookupGrower(),
                    ),
                  ),
                  FilledButton.tonal(onPressed: _lookupGrower, child: const Text('Lookup (Enter)')),
                  SizedBox(
                    width: 220,
                    child: DropdownButtonFormField<int>(
                      value: _loanTypeId,
                      decoration: const InputDecoration(labelText: 'Loan Type', hintText: 'Select Loan Type'),
                      items: [for (final t in _loanTypes) DropdownMenuItem(value: t['id'] as int, child: Text(t['loanTypeName']))],
                      onChanged: (v) => setState(() => _loanTypeId = v),
                    ),
                  ),
                  SizedBox(
                    width: 160,
                    child: TextField(
                      controller: _amount,
                      keyboardType: const TextInputType.numberWithOptions(decimal: true),
                      inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'^\d*\.?\d{0,2}'))],
                      decoration: const InputDecoration(labelText: 'Loan Amount (Rs)', hintText: 'Example: 5000.00'),
                    ),
                  ),
                  SizedBox(
                    width: 260,
                    child: TextField(
                      controller: _remarks,
                      decoration: const InputDecoration(labelText: 'Remarks (optional)'),
                    ),
                  ),
                ]),
                if (_grower != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 10),
                    child: Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                          color: const Color(0xFF2E7D32).withOpacity(0.10), borderRadius: BorderRadius.circular(8)),
                      child: Text('${_grower!['growerName']}  S/o ${_grower!['fatherName']}  •  Village: ${_grower!['villageName']}',
                          style: const TextStyle(fontWeight: FontWeight.w600)),
                    ),
                  ),
                if (_growerError != null)
                  Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: Text(_growerError!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
                const SizedBox(height: 14),
                FilledButton.icon(
                  onPressed: _saving ? null : _issue,
                  icon: const Icon(Icons.savings_outlined),
                  label: Text(_saving ? 'Issuing...' : 'ISSUE LOAN'),
                  style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(horizontal: 30, vertical: 16)),
                ),
              ]),
            ),
          ),
        const SizedBox(height: 10),
        Row(children: [
          Text('Loan Register', style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ]),
        const SizedBox(height: 6),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : SingleChildScrollView(
                    scrollDirection: Axis.vertical,
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(columns: [
                        const DataColumn(label: Text('Loan ID')),
                        const DataColumn(label: Text('Grower')),
                        const DataColumn(label: Text('Village')),
                        const DataColumn(label: Text('Loan Type')),
                        const DataColumn(label: Text('Amount')),
                        const DataColumn(label: Text('Recovered')),
                        const DataColumn(label: Text('Outstanding')),
                        const DataColumn(label: Text('Status')),
                        if (canCancel) const DataColumn(label: Text('Actions')),
                      ], rows: [
                        for (final l in _items)
                          DataRow(cells: [
                            DataCell(Text('${l['loanId']}', style: const TextStyle(fontWeight: FontWeight.w700))),
                            DataCell(Text('${l['growerCode']} ${l['growerName']}')),
                            DataCell(Text('${l['villageName']}')),
                            DataCell(Text('${l['loanTypeName']}')),
                            DataCell(Text((l['loanAmount'] as num).toStringAsFixed(2))),
                            DataCell(Text((l['recoveredAmount'] as num).toStringAsFixed(2))),
                            DataCell(Text((l['outstandingAmount'] as num).toStringAsFixed(2))),
                            DataCell(_statusChip(l['loanStatus'])),
                            if (canCancel)
                              DataCell(l['loanStatus'] == 'ACTIVE' && l['recoveredAmount'] == 0
                                  ? IconButton(
                                      tooltip: 'Cancel Loan',
                                      icon: const Icon(Icons.cancel_outlined, size: 18),
                                      onPressed: () => _cancelLoan(l['loanId']))
                                  : const SizedBox.shrink()),
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
      'ACTIVE' => Colors.orange,
      'CLOSED' => const Color(0xFF2E7D32),
      'CANCELLED' => Colors.red,
      _ => Colors.grey,
    };
    return Chip(
        label: Text(s ?? '-', style: const TextStyle(fontSize: 10, color: Colors.white)),
        backgroundColor: color,
        visualDensity: VisualDensity.compact);
  }
}

/// Record manual loan recovery + recovery register with Reverse action (Phase 8).
class LoanRecoveryScreen extends StatefulWidget {
  const LoanRecoveryScreen({super.key});
  @override
  State<LoanRecoveryScreen> createState() => _LoanRecoveryScreenState();
}

class _LoanRecoveryScreenState extends State<LoanRecoveryScreen> {
  final _loanIdCtl = TextEditingController();
  Map<String, dynamic>? _loan;
  String? _loanError;
  final _amount = TextEditingController();
  final _remarks = TextEditingController();
  List _items = [];
  bool _loading = true;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final res = await ApiClient.instance.dio.get('/api/loan-recoveries');
      if (res.statusCode == 200 && mounted) setState(() => _items = res.data['items']);
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  void _toast(String msg, {bool error = false}) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(msg),
        backgroundColor: error ? Theme.of(context).colorScheme.error : const Color(0xFF2E7D32)));
  }

  Future<void> _fetchLoan() async {
    setState(() {
      _loan = null;
      _loanError = null;
    });
    final id = int.tryParse(_loanIdCtl.text.trim());
    if (id == null) return;
    final res = await ApiClient.instance.dio.get('/api/loans/$id');
    setState(() {
      if (res.statusCode == 200) {
        _loan = Map<String, dynamic>.from(res.data);
      } else {
        _loanError = ApiClient.errorMessage(res);
      }
    });
  }

  Future<void> _record() async {
    if (_loan == null) return _toast('Fetch a valid Loan ID first (press ENTER).', error: true);
    final amt = double.tryParse(_amount.text) ?? 0;
    if (amt <= 0) return _toast('Enter a valid Recovery Amount greater than 0.', error: true);
    setState(() => _saving = true);
    final res = await ApiClient.instance.dio.post('/api/loan-recoveries', data: {
      'loanId': _loan!['loanId'],
      'recoveryAmount': amt,
      'remarks': _remarks.text.trim().isEmpty ? null : _remarks.text.trim(),
      'idempotencyKey': 'lr-${_loan!['loanId']}-${DateTime.now().millisecondsSinceEpoch ~/ 15000}',
    });
    setState(() => _saving = false);
    if (res.statusCode == 200) {
      _toast(res.data['message']);
      setState(() {
        _loan = null;
        _loanIdCtl.clear();
        _amount.clear();
        _remarks.clear();
      });
      _load();
      final autoPrint = res.data['autoPrint'];
      if (autoPrint != null) {
        final outcome = await PrintService.printDocument(
          documentUrl: autoPrint['documentUrl'],
          printerType: autoPrint['printerType'] ?? 'DotMatrix',
          printerName: autoPrint['printerName'] ?? '',
          copies: autoPrint['copies'] ?? 1,
        );
        if (mounted) _toast(outcome.message, error: !outcome.success);
      }
    } else {
      _toast(ApiClient.errorMessage(res), error: true);
    }
  }

  Future<void> _reverse(int recoveryId) async {
    final reasonCtl = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('Reverse Recovery $recoveryId?'),
        content: TextField(
            controller: reasonCtl,
            decoration: const InputDecoration(labelText: 'Reversal Reason (min 5 characters)')),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Back')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Reverse')),
        ],
      ),
    );
    if (ok != true) return;
    final res = await ApiClient.instance.dio
        .post('/api/loan-recoveries/$recoveryId/reverse', data: {'reason': reasonCtl.text.trim()});
    if (!mounted) return;
    _toast(res.statusCode == 200 ? res.data['message'] : ApiClient.errorMessage(res), error: res.statusCode != 200);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final canCreate = auth.can('LoanRecovery.Create');
    final canReverse = auth.can('LoanRecovery.Reverse');
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        if (canCreate)
          Card(
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('RECORD RECOVERY', style: TextStyle(fontWeight: FontWeight.w800, color: Theme.of(context).colorScheme.primary)),
                const SizedBox(height: 10),
                Wrap(spacing: 12, runSpacing: 12, crossAxisAlignment: WrapCrossAlignment.center, children: [
                  SizedBox(
                    width: 200,
                    child: TextField(
                      controller: _loanIdCtl,
                      keyboardType: TextInputType.number,
                      inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                      decoration: const InputDecoration(
                          labelText: 'Loan ID', hintText: 'Example: 1', prefixIcon: Icon(Icons.receipt_long, size: 18)),
                      onSubmitted: (_) => _fetchLoan(),
                    ),
                  ),
                  FilledButton.tonal(onPressed: _fetchLoan, child: const Text('Fetch (Enter)')),
                  SizedBox(
                    width: 160,
                    child: TextField(
                      controller: _amount,
                      keyboardType: const TextInputType.numberWithOptions(decimal: true),
                      inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'^\d*\.?\d{0,2}'))],
                      decoration: const InputDecoration(labelText: 'Recovery Amount (Rs)', hintText: 'Example: 1000.00'),
                    ),
                  ),
                  SizedBox(
                    width: 260,
                    child: TextField(
                      controller: _remarks,
                      decoration: const InputDecoration(labelText: 'Remarks (optional)'),
                    ),
                  ),
                ]),
                if (_loan != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 10),
                    child: Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                          color: Theme.of(context).colorScheme.primary.withOpacity(0.08), borderRadius: BorderRadius.circular(8)),
                      child: Text(
                        '${_loan!['growerCode']} ${_loan!['growerName']}  •  Loan Amount: Rs ${(_loan!['loanAmount'] as num).toStringAsFixed(2)}'
                        '  •  Outstanding: Rs ${(_loan!['outstandingAmount'] as num).toStringAsFixed(2)}  •  Status: ${_loan!['loanStatus']}',
                        style: const TextStyle(fontWeight: FontWeight.w600),
                      ),
                    ),
                  ),
                if (_loanError != null)
                  Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: Text(_loanError!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
                const SizedBox(height: 14),
                FilledButton.icon(
                  onPressed: _saving ? null : _record,
                  icon: const Icon(Icons.currency_rupee_outlined),
                  label: Text(_saving ? 'Recording...' : 'RECORD RECOVERY'),
                  style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(horizontal: 30, vertical: 16)),
                ),
              ]),
            ),
          ),
        const SizedBox(height: 10),
        Row(children: [
          Text('Recovery Register', style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ]),
        const SizedBox(height: 6),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : SingleChildScrollView(
                    scrollDirection: Axis.vertical,
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(columns: [
                        const DataColumn(label: Text('Recovery ID')),
                        const DataColumn(label: Text('Loan ID')),
                        const DataColumn(label: Text('Grower')),
                        const DataColumn(label: Text('Amount')),
                        const DataColumn(label: Text('Date')),
                        const DataColumn(label: Text('By')),
                        const DataColumn(label: Text('Status')),
                        if (canReverse) const DataColumn(label: Text('Actions')),
                      ], rows: [
                        for (final r in _items)
                          DataRow(cells: [
                            DataCell(Text('${r['recoveryId']}', style: const TextStyle(fontWeight: FontWeight.w700))),
                            DataCell(Text('${r['loanId']}')),
                            DataCell(Text('${r['growerCode']} ${r['growerName']}')),
                            DataCell(Text((r['recoveryAmount'] as num).toStringAsFixed(2))),
                            DataCell(Text('${r['recoveryDate']}'.replaceFirst('T', ' ').split('.').first)),
                            DataCell(Text('${r['recoveredByUserName']}')),
                            DataCell(Chip(
                                label: Text(r['recoveryStatus'] ?? '-', style: const TextStyle(fontSize: 10, color: Colors.white)),
                                backgroundColor: r['recoveryStatus'] == 'ACTIVE' ? const Color(0xFF2E7D32) : Colors.red,
                                visualDensity: VisualDensity.compact)),
                            if (canReverse)
                              DataCell(r['recoveryStatus'] == 'ACTIVE'
                                  ? IconButton(
                                      tooltip: 'Reverse',
                                      icon: const Icon(Icons.undo_outlined, size: 18),
                                      onPressed: () => _reverse(r['recoveryId']))
                                  : const SizedBox.shrink()),
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
