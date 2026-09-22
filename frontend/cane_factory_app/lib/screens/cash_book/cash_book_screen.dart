import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/file_download.dart';
import '../../providers/auth_provider.dart';

class CashBookScreen extends StatefulWidget {
  const CashBookScreen({super.key});

  @override
  State<CashBookScreen> createState() => _CashBookScreenState();
}

class _CashBookScreenState extends State<CashBookScreen> {
  final _sourceName = TextEditingController();
  final _amount = TextEditingController();
  final _reference = TextEditingController();
  final _remarks = TextEditingController();
  final _paidTo = TextEditingController();
  final _cashOutAmount = TextEditingController();
  final _cashOutReference = TextEditingController();
  final _cashOutRemarks = TextEditingController();
  final _search = TextEditingController();
  final _dateFormat = DateFormat('dd-MM-yyyy');
  String _sourceType = 'BANK';
  String _entryTab = 'RECEIPT';
  DateTime _receiptDate = DateTime.now();
  DateTime _paymentDate = DateTime.now();
  DateTime? _fromDate;
  DateTime? _toDate;
  bool _loading = true;
  bool _saving = false;
  String? _error;
  List<Map<String, dynamic>> _items = [];
  Map<String, dynamic> _totals = {};

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _sourceName.dispose();
    _amount.dispose();
    _reference.dispose();
    _remarks.dispose();
    _paidTo.dispose();
    _cashOutAmount.dispose();
    _cashOutReference.dispose();
    _cashOutRemarks.dispose();
    _search.dispose();
    super.dispose();
  }

  Map<String, dynamic> _query() => {
        if (_fromDate != null)
          'fromDate': DateFormat('yyyy-MM-dd').format(_fromDate!),
        if (_toDate != null)
          'toDate': DateFormat('yyyy-MM-dd').format(_toDate!),
        if (_search.text.trim().isNotEmpty) 'search': _search.text.trim(),
      };

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final res = await ApiClient.instance.dio
          .get('/api/cash-book', queryParameters: _query());
      if (res.statusCode == 200) {
        final data = Map<String, dynamic>.from(res.data);
        _items = List<Map<String, dynamic>>.from((data['items'] as List? ?? [])
            .map((x) => Map<String, dynamic>.from(x)));
        _totals = Map<String, dynamic>.from(data['totals'] ?? {});
      } else {
        _error = ApiClient.errorMessage(res);
      }
    } catch (e) {
      _error = 'Could not load Cash Book: $e';
    }
    if (mounted) setState(() => _loading = false);
  }

  Future<void> _pickDate({required bool receipt, required bool from}) async {
    final current =
        receipt
            ? _receiptDate
            : (from ? _fromDate : _toDate) ?? DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: current,
      firstDate: DateTime(2020),
      lastDate: DateTime(2035),
    );
    if (picked == null || !mounted) return;
    setState(() {
      if (receipt) {
        _receiptDate = picked;
      } else if (from) {
        _fromDate = picked;
      } else {
        _toDate = picked;
      }
    });
  }

  void _toast(String message, {bool error = false}) =>
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(message),
          backgroundColor: error
              ? Theme.of(context).colorScheme.error
              : const Color(0xFF2E7D32),
        ),
      );

  Future<void> _saveReceipt() async {
    final amount = double.tryParse(_amount.text.trim());
    if (_sourceName.text.trim().isEmpty)
      return _toast('Enter bank, party, or other source name.', error: true);
    if (amount == null || amount <= 0)
      return _toast('Enter a valid cash amount.', error: true);
    setState(() => _saving = true);
    try {
      final res =
          await ApiClient.instance.dio.post('/api/cash-book/receipts', data: {
        'entryDate': _receiptDate.toIso8601String(),
        'sourceType': _sourceType,
        'sourceName': _sourceName.text.trim(),
        'amount': amount,
        'referenceNumber':
            _reference.text.trim().isEmpty ? null : _reference.text.trim(),
        'remarks': _remarks.text.trim().isEmpty ? null : _remarks.text.trim(),
      });
      if (!mounted) return;
      if (res.statusCode == 200) {
        _toast(res.data['message'] ?? 'Cash receipt saved.');
        setState(() {
          _sourceName.clear();
          _amount.clear();
          _reference.clear();
          _remarks.clear();
          _receiptDate = DateTime.now();
        });
        _load();
      } else {
        _toast(ApiClient.errorMessage(res), error: true);
      }
    } catch (e) {
      if (mounted) _toast(ApiClient.exceptionMessage(e), error: true);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _saveOtherPayment() async {
    final amount = double.tryParse(_cashOutAmount.text.trim());
    if (_paidTo.text.trim().isEmpty) {
      return _toast('Enter the person or party paid.', error: true);
    }
    if (amount == null || amount <= 0) {
      return _toast('Enter a valid cash amount.', error: true);
    }
    if (_cashOutRemarks.text.trim().isEmpty) {
      return _toast('Enter the purpose / remarks for this payment.', error: true);
    }
    setState(() => _saving = true);
    try {
      final res = await ApiClient.instance.dio.post('/api/cash-book/payments',
          data: {
            'entryDate': _paymentDate.toIso8601String(),
            'paidTo': _paidTo.text.trim(),
            'amount': amount,
            'referenceNumber': _cashOutReference.text.trim().isEmpty
                ? null
                : _cashOutReference.text.trim(),
            'remarks': _cashOutRemarks.text.trim(),
          });
      if (!mounted) return;
      if (res.statusCode == 200) {
        _toast(res.data['message'] ?? 'Other cash payment saved.');
        setState(() {
          _paidTo.clear();
          _cashOutAmount.clear();
          _cashOutReference.clear();
          _cashOutRemarks.clear();
          _paymentDate = DateTime.now();
        });
        _load();
      } else {
        _toast(ApiClient.errorMessage(res), error: true);
      }
    } catch (e) {
      if (mounted) _toast(ApiClient.exceptionMessage(e), error: true);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _export(String format) async {
    await downloadAndNotify(context, '/api/reports/cash-book',
        'cash-book-report.${format == 'pdf' ? 'pdf' : 'xlsx'}',
        queryParameters: {..._query(), 'format': format});
  }

  String _money(dynamic value) =>
      (num.tryParse('$value') ?? 0).toStringAsFixed(2);
  String _date(dynamic value) {
    final date = DateTime.tryParse('$value');
    return date == null ? '-' : _dateFormat.format(date.toLocal());
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final canCreate = auth.can('CashBook.Create');
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Text('Cash Book',
            style: Theme.of(context)
                .textTheme
                .titleLarge
                ?.copyWith(fontWeight: FontWeight.w700)),
        const SizedBox(height: 6),
        if (canCreate) ...[
          _entryTabs(),
          const SizedBox(height: 12),
          _entryTab == 'RECEIPT' ? _receiptForm() : _otherPaymentForm(),
        ],
        const SizedBox(height: 10),
        _filters(),
        const SizedBox(height: 8),
        if (_error != null)
          Text(_error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error)),
        _summary(),
        const SizedBox(height: 8),
        Expanded(child: _table()),
      ]),
    );
  }

  /// Cash entry types have separate, focused forms. The running register remains
  /// below the tabs, so operators can still verify every entry in one place.
  Widget _entryTabs() => Container(
        decoration: BoxDecoration(
            border: Border(
                bottom: BorderSide(
                    color: Theme.of(context).dividerColor.withValues(alpha: 0.6)))),
        child: Row(children: [
          _entryTabButton('Cash Receipt', 'RECEIPT', Icons.south_west_outlined),
          _entryTabButton(
              'Other Cash Payment', 'PAYMENT', Icons.north_east_outlined),
        ]),
      );

  Widget _entryTabButton(String label, String value, IconData icon) {
    final selected = _entryTab == value;
    final color = selected ? Theme.of(context).colorScheme.primary : null;
    return InkWell(
      onTap: () => setState(() => _entryTab = value),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
        decoration: BoxDecoration(
          border: Border(
              bottom: BorderSide(
                  color: selected ? Theme.of(context).colorScheme.primary : Colors.transparent,
                  width: 3)),
        ),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          Icon(icon, size: 18, color: color),
          const SizedBox(width: 8),
          Text(label,
              style: TextStyle(
                  color: color,
                  fontWeight: selected ? FontWeight.w800 : FontWeight.w600)),
        ]),
      ),
    );
  }

  Widget _receiptForm() => Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('CASH RECEIVED',
                style: TextStyle(
                    fontWeight: FontWeight.w800,
                    color: Theme.of(context).colorScheme.primary)),
            const SizedBox(height: 10),
            Wrap(
                spacing: 12,
                runSpacing: 12,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  SizedBox(
                    width: 150,
                    child: DropdownButtonFormField<String>(
                      value: _sourceType,
                      decoration:
                          const InputDecoration(labelText: 'Received From'),
                      items: const [
                        DropdownMenuItem(value: 'BANK', child: Text('Bank')),
                        DropdownMenuItem(value: 'PARTY', child: Text('Party')),
                        DropdownMenuItem(value: 'OTHER', child: Text('Other')),
                      ],
                      onChanged: (value) =>
                          setState(() => _sourceType = value ?? 'BANK'),
                    ),
                  ),
                  SizedBox(
                      width: 220,
                      child: TextField(
                          controller: _sourceName,
                          decoration: const InputDecoration(
                              labelText: 'Bank / Party Name'))),
                  SizedBox(
                    width: 150,
                    child: TextField(
                      controller: _amount,
                      keyboardType:
                          const TextInputType.numberWithOptions(decimal: true),
                      inputFormatters: [
                        FilteringTextInputFormatter.allow(
                            RegExp(r'^\d*\.?\d{0,2}'))
                      ],
                      decoration:
                          const InputDecoration(labelText: 'Cash Amount (₹)'),
                    ),
                  ),
                  OutlinedButton.icon(
                      onPressed: () => _pickDate(receipt: true, from: true),
                      icon: const Icon(Icons.date_range),
                      label: Text(_dateFormat.format(_receiptDate))),
                  SizedBox(
                      width: 180,
                      child: TextField(
                          controller: _reference,
                          decoration: const InputDecoration(
                              labelText: 'Reference No. (optional)'))),
                  SizedBox(
                      width: 200,
                      child: TextField(
                          controller: _remarks,
                          decoration: const InputDecoration(
                              labelText: 'Remarks (optional)'))),
                  FilledButton.icon(
                    onPressed: _saving ? null : _saveReceipt,
                    icon: const Icon(Icons.save_outlined),
                    label: Text(_saving ? 'Saving...' : 'Save Cash Receipt'),
                  ),
                ]),
          ]),
        ),
      );

  Widget _otherPaymentForm() => Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('OTHER CASH PAYMENT',
                style: TextStyle(
                    fontWeight: FontWeight.w800,
                    color: Theme.of(context).colorScheme.error)),
            const SizedBox(height: 4),
            const Text(
                'Farmer cash payments are posted automatically from the Payment form. Add only non-farmer cash payments here.'),
            const SizedBox(height: 10),
            Wrap(
                spacing: 12,
                runSpacing: 12,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  SizedBox(
                      width: 220,
                      child: TextField(
                          controller: _paidTo,
                          decoration: const InputDecoration(
                              labelText: 'Paid To (person / party)'))),
                  SizedBox(
                    width: 150,
                    child: TextField(
                      controller: _cashOutAmount,
                      keyboardType:
                          const TextInputType.numberWithOptions(decimal: true),
                      inputFormatters: [
                        FilteringTextInputFormatter.allow(
                            RegExp(r'^\d*\.?\d{0,2}'))
                      ],
                      decoration:
                          const InputDecoration(labelText: 'Cash Amount (₹)'),
                    ),
                  ),
                  OutlinedButton.icon(
                      onPressed: () async {
                        final picked = await showDatePicker(
                            context: context,
                            initialDate: _paymentDate,
                            firstDate: DateTime(2020),
                            lastDate: DateTime(2035));
                        if (picked != null && mounted) {
                          setState(() => _paymentDate = picked);
                        }
                      },
                      icon: const Icon(Icons.date_range),
                      label: Text(_dateFormat.format(_paymentDate))),
                  SizedBox(
                      width: 180,
                      child: TextField(
                          controller: _cashOutReference,
                          decoration: const InputDecoration(
                              labelText: 'Reference No. (optional)'))),
                  SizedBox(
                      width: 250,
                      child: TextField(
                          controller: _cashOutRemarks,
                          decoration: const InputDecoration(
                              labelText: 'Purpose / Remarks'))),
                  FilledButton.icon(
                    onPressed: _saving ? null : _saveOtherPayment,
                    icon: const Icon(Icons.payments_outlined),
                    label: Text(_saving ? 'Saving...' : 'Save Cash Payment'),
                  ),
                ]),
          ]),
        ),
      );

  Widget _filters() => Wrap(
          spacing: 10,
          runSpacing: 8,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            OutlinedButton.icon(
                onPressed: () => _pickDate(receipt: false, from: true),
                icon: const Icon(Icons.date_range),
                label: Text(_fromDate == null
                    ? 'From Date'
                    : _dateFormat.format(_fromDate!))),
            OutlinedButton.icon(
                onPressed: () => _pickDate(receipt: false, from: false),
                icon: const Icon(Icons.date_range),
                label: Text(_toDate == null
                    ? 'To Date'
                    : _dateFormat.format(_toDate!))),
            SizedBox(
                width: 220,
                child: TextField(
                    controller: _search,
                    onSubmitted: (_) => _load(),
                    decoration: const InputDecoration(
                        labelText: 'Search source / grower / reference',
                        prefixIcon: Icon(Icons.search)))),
            FilledButton.icon(
                onPressed: _load,
                icon: const Icon(Icons.search),
                label: const Text('Search')),
            OutlinedButton(
                onPressed: () {
                  setState(() {
                    _fromDate = null;
                    _toDate = null;
                    _search.clear();
                  });
                  _load();
                },
                child: const Text('Clear')),
            OutlinedButton.icon(
                onPressed: () => _export('pdf'),
                icon: const Icon(Icons.picture_as_pdf_outlined),
                label: const Text('Print (PDF)')),
            OutlinedButton.icon(
                onPressed: () => _export('excel'),
                icon: const Icon(Icons.grid_on_outlined),
                label: const Text('Export (Excel)')),
          ]);

  Widget _summary() => Card(
        color: Theme.of(context).colorScheme.primaryContainer,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Wrap(spacing: 28, runSpacing: 8, children: [
            _summaryValue('Opening Cash', _totals['openingBalance']),
            _summaryValue('Cash Received', _totals['received']),
            _summaryValue('Paid to Farmers', _totals['farmerCashPaid']),
            _summaryValue('Other Cash Paid', _totals['otherCashPaid']),
            _summaryValue('Total Cash Paid', _totals['paid']),
            _summaryValue('Closing Balance', _totals['closingBalance'],
                bold: true),
          ]),
        ),
      );

  Widget _summaryValue(String label, dynamic amount, {bool bold = false}) =>
      Text('$label: ₹${_money(amount)}',
          style:
              TextStyle(fontWeight: bold ? FontWeight.w800 : FontWeight.w600));

  Widget _table() => Card(
        child: _loading
            ? const Center(child: CircularProgressIndicator())
            : _items.isEmpty
                ? const Center(
                    child: Text(
                        'No Cash Book entries found for the selected date range.'))
                : SingleChildScrollView(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(
                        columns: const [
                          DataColumn(label: Text('Date')),
                          DataColumn(label: Text('Entry')),
                          DataColumn(label: Text('Source')),
                          DataColumn(label: Text('Grower Code / Name')),
                          DataColumn(label: Text('Payment ID')),
                          DataColumn(label: Text('Net Payable (₹)')),
                          DataColumn(label: Text('Cash In (₹)')),
                          DataColumn(label: Text('Cash Out (₹)')),
                          DataColumn(label: Text('Balance (₹)')),
                          DataColumn(label: Text('Reference / Remarks')),
                        ],
                        rows: [
                          for (final row in _items)
                            DataRow(cells: [
                              DataCell(Text(_date(row['entryDate']))),
                              DataCell(Text(_entryLabel(row))),
                              DataCell(Text(
                                  '${row['sourceType']} • ${row['sourceName'] ?? '-'}')),
                              DataCell(Text(row['growerCode'] == null
                                  ? '-'
                                  : '${row['growerCode']} ${row['growerName'] ?? ''}')),
                              DataCell(Text('${row['paymentId'] ?? '-'}')),
                              DataCell(Text(row['netPayableAmount'] == null
                                  ? '-'
                                  : _money(row['netPayableAmount']))),
                              DataCell(Text(row['entryType'] == 'CASH_IN'
                                  ? _money(row['amount'])
                                  : '-')),
                              DataCell(Text(row['entryType'] == 'CASH_OUT'
                                  ? _money(row['amount'])
                                  : '-')),
                              DataCell(Text(_money(row['runningBalance']),
                                  style: const TextStyle(
                                      fontWeight: FontWeight.w700))),
                              DataCell(SizedBox(
                                  width: 250,
                                  child: Text(
                                      '${row['referenceNumber'] ?? ''} ${row['remarks'] ?? ''}',
                                      overflow: TextOverflow.ellipsis))),
                            ]),
                        ],
                      ),
                    ),
                  ),
      );

  String _entryLabel(Map<String, dynamic> row) {
    if (row['entryType'] == 'CASH_IN') return 'Received';
    return row['sourceType'] == 'FARMER_PAYMENT'
        ? 'Farmer Payment'
        : 'Other Cash Payment';
  }
}
