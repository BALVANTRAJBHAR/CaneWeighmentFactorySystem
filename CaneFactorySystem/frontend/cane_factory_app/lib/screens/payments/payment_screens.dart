import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/print_service.dart';
import '../../providers/auth_provider.dart';

/// Payment + Advice (Phase 9): SINGLE/DATE_RANGE/FARMER purchase selection, live eligible-purchases
/// + loan-deduction preview before committing, then CASH/BANK/MOBILE_UPI payment with Cash Evidence.
class PaymentScreen extends StatefulWidget {
  const PaymentScreen({super.key});
  @override
  State<PaymentScreen> createState() => _PaymentScreenState();
}

class _PaymentScreenState extends State<PaymentScreen> {
  String _mode = 'FARMER';
  final _growerCode = TextEditingController();
  final _purchaseId = TextEditingController();
  DateTime? _fromDate;
  DateTime? _toDate;
  int? _paymentModeId;
  final _txnRef = TextEditingController();
  List _paymentModes = [];
  Map<String, dynamic>? _preview;
  String? _previewError;
  bool _previewing = false;
  bool _paying = false;
  List _items = [];
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _loadModes();
    _load();
  }

  Future<void> _loadModes() async {
    try {
      final res = await ApiClient.instance.dio.get('/api/payment-modes');
      if (res.statusCode == 200 && mounted) setState(() => _paymentModes = res.data['items']);
    } catch (_) {}
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final res = await ApiClient.instance.dio.get('/api/payments');
      if (res.statusCode == 200 && mounted) setState(() => _items = res.data['items']);
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  void _toast(String msg, {bool error = false}) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(msg),
        backgroundColor: error ? Theme.of(context).colorScheme.error : const Color(0xFF2E7D32)));
  }

  Future<void> _pickDate({required bool from}) async {
    final d = await showDatePicker(
        context: context, initialDate: DateTime.now(), firstDate: DateTime(2020), lastDate: DateTime(2035));
    if (d == null) return;
    setState(() => from ? _fromDate = d : _toDate = d);
  }

  Future<void> _preview() async {
    setState(() {
      _preview = null;
      _previewError = null;
      _previewing = true;
    });
    final params = <String, dynamic>{'selectionMode': _mode, 'growerCode': _growerCode.text.trim()};
    if (_mode == 'SINGLE') params['purchaseId'] = _purchaseId.text.trim();
    if (_mode == 'DATE_RANGE') {
      if (_fromDate != null) params['fromDate'] = _fromDate!.toIso8601String();
      if (_toDate != null) params['toDate'] = _toDate!.toIso8601String();
    }
    final res = await ApiClient.instance.dio.get('/api/payments/eligible-purchases', queryParameters: params);
    setState(() {
      _previewing = false;
      if (res.statusCode == 200) {
        _preview = Map<String, dynamic>.from(res.data);
      } else {
        _previewError = ApiClient.errorMessage(res);
      }
    });
  }

  Future<void> _pay() async {
    if (_preview == null) return _toast('Preview eligible purchases first.', error: true);
    if ((_preview!['eligiblePurchases'] as List).isEmpty) return _toast('No eligible purchases to pay.', error: true);
    if (_paymentModeId == null) return _toast('Select a Payment Mode.', error: true);
    setState(() => _paying = true);
    final data = <String, dynamic>{
      'selectionMode': _mode,
      'growerCode': _growerCode.text.trim(),
      'paymentModeId': _paymentModeId,
      'transactionRefNumber': _txnRef.text.trim().isEmpty ? null : _txnRef.text.trim(),
      'idempotencyKey': 'pay-${_growerCode.text.trim()}-${DateTime.now().millisecondsSinceEpoch ~/ 30000}',
    };
    if (_mode == 'SINGLE') data['purchaseId'] = int.tryParse(_purchaseId.text.trim());
    if (_mode == 'DATE_RANGE') {
      if (_fromDate != null) data['fromDate'] = _fromDate!.toIso8601String();
      if (_toDate != null) data['toDate'] = _toDate!.toIso8601String();
    }
    final res = await ApiClient.instance.dio.post('/api/payments', data: data);
    setState(() => _paying = false);
    if (res.statusCode == 200) {
      _toast(res.data['message']);
      setState(() {
        _preview = null;
        _growerCode.clear();
        _purchaseId.clear();
        _txnRef.clear();
        _fromDate = null;
        _toDate = null;
        _paymentModeId = null;
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

  Future<void> _cancelPayment(int paymentId) async {
    final reasonCtl = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('Cancel Payment $paymentId?'),
        content: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('Purchases become payable again and any auto-deducted loan recovery is reversed.'),
          const SizedBox(height: 10),
          TextField(controller: reasonCtl, decoration: const InputDecoration(labelText: 'Cancellation Reason (min 5 characters)')),
        ]),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Back')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Cancel Payment')),
        ],
      ),
    );
    if (ok != true) return;
    final res = await ApiClient.instance.dio.post('/api/payments/$paymentId/cancel', data: {'reason': reasonCtl.text.trim()});
    if (!mounted) return;
    _toast(res.statusCode == 200 ? res.data['message'] : ApiClient.errorMessage(res), error: res.statusCode != 200);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final canCreate = auth.can('Payment.Create');
    final canCancel = auth.can('Payment.Cancel');
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        if (canCreate) _buildForm(context),
        const SizedBox(height: 10),
        Row(children: [
          Text('Payment Register', style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ]),
        const SizedBox(height: 6),
        Expanded(child: Card(child: _loading ? const Center(child: CircularProgressIndicator()) : _buildTable(canCancel))),
      ]),
    );
  }

  Widget _buildForm(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('MAKE PAYMENT', style: TextStyle(fontWeight: FontWeight.w800, color: Theme.of(context).colorScheme.primary)),
          const SizedBox(height: 10),
          SegmentedButton<String>(
            segments: const [
              ButtonSegment(value: 'SINGLE', label: Text('Single Purchase')),
              ButtonSegment(value: 'DATE_RANGE', label: Text('Date Range')),
              ButtonSegment(value: 'FARMER', label: Text('Farmer-wise (All Pending)')),
            ],
            selected: {_mode},
            onSelectionChanged: (s) => setState(() {
              _mode = s.first;
              _preview = null;
            }),
          ),
          const SizedBox(height: 12),
          Wrap(spacing: 12, runSpacing: 12, crossAxisAlignment: WrapCrossAlignment.center, children: [
            SizedBox(
              width: 200,
              child: TextField(
                controller: _growerCode,
                decoration: const InputDecoration(labelText: 'Grower Code', hintText: 'Example: 101/1'),
              ),
            ),
            if (_mode == 'SINGLE')
              SizedBox(
                width: 160,
                child: TextField(
                  controller: _purchaseId,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(labelText: 'Purchase ID', hintText: 'Example: 105'),
                ),
              ),
            if (_mode == 'DATE_RANGE') ...[
              OutlinedButton(
                  onPressed: () => _pickDate(from: true),
                  child: Text(_fromDate == null ? 'From Date' : '${_fromDate!.day}-${_fromDate!.month}-${_fromDate!.year}')),
              OutlinedButton(
                  onPressed: () => _pickDate(from: false),
                  child: Text(_toDate == null ? 'To Date' : '${_toDate!.day}-${_toDate!.month}-${_toDate!.year}')),
            ],
            FilledButton.tonal(
                onPressed: _previewing ? null : _preview, child: Text(_previewing ? 'Loading...' : 'Preview')),
          ]),
          if (_previewError != null)
            Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(_previewError!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
          if (_preview != null) _buildPreview(context),
          if (_preview != null) ...[
            const SizedBox(height: 14),
            Wrap(spacing: 12, runSpacing: 12, crossAxisAlignment: WrapCrossAlignment.center, children: [
              SizedBox(
                width: 220,
                child: DropdownButtonFormField<int>(
                  value: _paymentModeId,
                  decoration: const InputDecoration(labelText: 'Payment Mode'),
                  items: [for (final m in _paymentModes) DropdownMenuItem(value: m['id'] as int, child: Text(m['modeName']))],
                  onChanged: (v) => setState(() => _paymentModeId = v),
                ),
              ),
              SizedBox(
                width: 220,
                child: TextField(
                  controller: _txnRef,
                  decoration: const InputDecoration(labelText: 'Transaction Ref (optional)', hintText: 'UPI/Bank Ref No.'),
                ),
              ),
            ]),
            const SizedBox(height: 14),
            FilledButton.icon(
              onPressed: _paying ? null : _pay,
              icon: const Icon(Icons.payments_outlined),
              label: Text(_paying ? 'Processing...' : 'COMPLETE PAYMENT'),
              style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(horizontal: 30, vertical: 16)),
            ),
          ],
        ]),
      ),
    );
  }

  Widget _buildPreview(BuildContext context) {
    final eligible = _preview!['eligiblePurchases'] as List;
    final loans = _preview!['outstandingLoans'] as List;
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Container(
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.primary.withOpacity(0.06), borderRadius: BorderRadius.circular(8)),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('${eligible.length} eligible purchase(s)  •  Total: Rs ${(_preview!['totalPurchaseAmount'] as num).toStringAsFixed(2)}',
              style: const TextStyle(fontWeight: FontWeight.w700)),
          if (loans.isNotEmpty) ...[
            const SizedBox(height: 6),
            Text('Outstanding Loans: Rs ${(_preview!['totalOutstandingLoan'] as num).toStringAsFixed(2)}  '
                '(auto-deducting Rs ${(_preview!['estimatedLoanDeduction'] as num).toStringAsFixed(2)})',
                style: const TextStyle(color: Colors.orange, fontWeight: FontWeight.w600)),
          ],
          const SizedBox(height: 6),
          Text('Estimated Net Payable: Rs ${(_preview!['estimatedNetPayable'] as num).toStringAsFixed(2)}',
              style: TextStyle(fontWeight: FontWeight.w800, color: Theme.of(context).colorScheme.primary, fontSize: 15)),
        ]),
      ),
    );
  }

  Widget _buildTable(bool canCancel) {
    return SingleChildScrollView(
      scrollDirection: Axis.vertical,
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: DataTable(columns: [
          const DataColumn(label: Text('Payment ID')),
          const DataColumn(label: Text('Advice #')),
          const DataColumn(label: Text('Grower')),
          const DataColumn(label: Text('Total')),
          const DataColumn(label: Text('Loan Deducted')),
          const DataColumn(label: Text('Net Payable')),
          const DataColumn(label: Text('Mode')),
          const DataColumn(label: Text('Status')),
          if (canCancel) const DataColumn(label: Text('Actions')),
        ], rows: [
          for (final p in _items)
            DataRow(cells: [
              DataCell(Text('${p['paymentId']}', style: const TextStyle(fontWeight: FontWeight.w700))),
              DataCell(Text('${p['adviceNumber']}')),
              DataCell(Text('${p['growerCode']} ${p['growerName']}')),
              DataCell(Text((p['totalPurchaseAmount'] as num).toStringAsFixed(2))),
              DataCell(Text((p['loanDeductedAmount'] as num).toStringAsFixed(2))),
              DataCell(Text((p['netPayableAmount'] as num).toStringAsFixed(2))),
              DataCell(Text('${p['paymentModeName']}')),
              DataCell(Chip(
                  label: Text(p['paymentStatus'] ?? '-', style: const TextStyle(fontSize: 10, color: Colors.white)),
                  backgroundColor: p['paymentStatus'] == 'COMPLETED' ? const Color(0xFF2E7D32) : Colors.red,
                  visualDensity: VisualDensity.compact)),
              if (canCancel)
                DataCell(p['paymentStatus'] == 'COMPLETED'
                    ? IconButton(
                        tooltip: 'Cancel Payment',
                        icon: const Icon(Icons.cancel_outlined, size: 18),
                        onPressed: () => _cancelPayment(p['paymentId']))
                    : const SizedBox.shrink()),
            ]),
        ]),
      ),
    );
  }
}
