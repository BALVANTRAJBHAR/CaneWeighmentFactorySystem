import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/api_client.dart';
import '../../core/file_download.dart';

/// Phase 11: Reports hub - Purchases (covers Daily Weighment, Gross/Tare/Net, Village-wise,
/// Grower-wise, Date-range, Rate-wise, Variety-wise, Vehicle-wise, Pending Payment, Lock report),
/// Payments (+ Cancel report), Loans (+ Cancel report) and Daily Collection. Every report supports
/// date range, key filters, on-screen totals and Print (PDF) / Export (Excel) download.
class ReportsScreen extends StatefulWidget {
  const ReportsScreen({super.key});
  @override
  State<ReportsScreen> createState() => _ReportsScreenState();
}

class _ReportsScreenState extends State<ReportsScreen> {
  static const _reportTypes = {
    'purchases': 'Purchase / Weighment Report',
    'payments': 'Payment Report',
    'loans': 'Loan Report',
    'daily-collection': 'Daily Collection Report',
  };

  String _reportType = 'purchases';
  DateTime? _fromDate;
  DateTime? _toDate;
  final _growerCode = TextEditingController();
  final _statusCtrl = TextEditingController();
  bool _loading = false;
  String? _error;
  List<dynamic> _items = [];
  Map<String, dynamic>? _totals;

  Future<void> _pickDate({required bool from}) async {
    final picked = await showDatePicker(
      context: context,
      initialDate: (from ? _fromDate : _toDate) ?? DateTime.now(),
      firstDate: DateTime(2020),
      lastDate: DateTime(2035),
    );
    if (picked != null) setState(() => from ? _fromDate = picked : _toDate = picked);
  }

  Map<String, dynamic> _query({String? format}) {
    final q = <String, dynamic>{
      if (_fromDate != null) 'fromDate': _fromDate!.toIso8601String(),
      if (_toDate != null) 'toDate': _toDate!.toIso8601String(),
      if (format != null) 'format': format,
    };
    if (_reportType != 'daily-collection') {
      if (_growerCode.text.trim().isNotEmpty) q['growerCode'] = _growerCode.text.trim();
      if (_statusCtrl.text.trim().isNotEmpty) {
        q[_reportType == 'purchases' ? 'paymentStatus' : 'status'] = _statusCtrl.text.trim();
      }
    }
    return q;
  }

  Future<void> _search() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final res = await ApiClient.instance.dio.get('/api/reports/$_reportType', queryParameters: _query(format: 'json'));
      if (res.statusCode == 200) {
        setState(() {
          _items = List<dynamic>.from(res.data['items'] ?? res.data['rows'] ?? []);
          _totals = Map<String, dynamic>.from(res.data['totals'] ?? {});
        });
      } else {
        setState(() => _error = ApiClient.errorMessage(res));
      }
    } catch (e) {
      setState(() => _error = 'Could not load report: $e');
    }
    if (mounted) setState(() => _loading = false);
  }

  Future<void> _export(String format) async {
    final ext = format == 'pdf' ? 'pdf' : 'xlsx';
    await downloadAndNotify(context, '/api/reports/$_reportType', '$_reportType-report.$ext');
  }

  @override
  void initState() {
    super.initState();
    _search();
  }

  List<String> get _columns {
    switch (_reportType) {
      case 'payments':
        return ['paymentId', 'growerCode', 'growerName', 'netPayableAmount', 'paymentModeName', 'paymentDate', 'paymentStatus'];
      case 'loans':
        return ['loanId', 'growerCode', 'growerName', 'loanAmount', 'outstandingAmount', 'issueDate', 'loanStatus'];
      case 'daily-collection':
        return ['date', 'vehicleCount', 'finalWeightQuintal', 'purchaseAmount'];
      default:
        return ['purchaseId', 'growerCode', 'growerName', 'villageName', 'vehicleNumber', 'finalWeightQuintal', 'purchaseAmount', 'paymentStatus'];
    }
  }

  @override
  Widget build(BuildContext context) {
    final df = DateFormat('dd-MM-yyyy');
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Text('Reports', style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w700)),
        const SizedBox(height: 10),
        Wrap(spacing: 12, runSpacing: 12, crossAxisAlignment: WrapCrossAlignment.center, children: [
          SizedBox(
            width: 260,
            child: DropdownButtonFormField<String>(
              value: _reportType,
              decoration: const InputDecoration(labelText: 'Report'),
              items: [for (final e in _reportTypes.entries) DropdownMenuItem(value: e.key, child: Text(e.value))],
              onChanged: (v) => setState(() => _reportType = v!),
            ),
          ),
          OutlinedButton.icon(
            icon: const Icon(Icons.date_range),
            onPressed: () => _pickDate(from: true),
            label: Text(_fromDate == null ? 'From Date' : df.format(_fromDate!)),
          ),
          OutlinedButton.icon(
            icon: const Icon(Icons.date_range),
            onPressed: () => _pickDate(from: false),
            label: Text(_toDate == null ? 'To Date' : df.format(_toDate!)),
          ),
          if (_reportType != 'daily-collection')
            SizedBox(width: 160, child: TextField(controller: _growerCode, decoration: const InputDecoration(labelText: 'Grower Code', hintText: '101/1'))),
          if (_reportType != 'daily-collection')
            SizedBox(
                width: 160,
                child: TextField(
                    controller: _statusCtrl,
                    decoration: InputDecoration(labelText: _reportType == 'purchases' ? 'Payment/Lock Status' : 'Status', hintText: 'e.g. PENDING'))),
          FilledButton.icon(onPressed: _search, icon: const Icon(Icons.search), label: const Text('Search')),
          OutlinedButton.icon(onPressed: () => _export('pdf'), icon: const Icon(Icons.picture_as_pdf_outlined), label: const Text('Print (PDF)')),
          OutlinedButton.icon(onPressed: () => _export('excel'), icon: const Icon(Icons.grid_on_outlined), label: const Text('Export (Excel)')),
        ]),
        const SizedBox(height: 10),
        if (_error != null) Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
        if (_totals != null && _totals!.isNotEmpty)
          Card(
            color: Theme.of(context).colorScheme.primaryContainer,
            child: Padding(
              padding: const EdgeInsets.all(10),
              child: Wrap(spacing: 20, children: [
                for (final e in _totals!.entries) Text('${e.key}: ${e.value}', style: const TextStyle(fontWeight: FontWeight.w600)),
              ]),
            ),
          ),
        const SizedBox(height: 10),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _items.isEmpty
                    ? const Center(child: Text('No records found for the selected filters.'))
                    : SingleChildScrollView(
                        child: SingleChildScrollView(
                          scrollDirection: Axis.horizontal,
                          child: DataTable(
                            columns: [for (final c in _columns) DataColumn(label: Text(c))],
                            rows: [
                              for (final item in _items)
                                DataRow(cells: [
                                  for (final c in _columns) DataCell(Text('${item[c] ?? ''}', overflow: TextOverflow.ellipsis)),
                                ]),
                            ],
                          ),
                        ),
                      ),
          ),
        ),
      ]),
    );
  }
}
