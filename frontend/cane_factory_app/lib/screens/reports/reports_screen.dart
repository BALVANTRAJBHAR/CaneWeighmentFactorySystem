import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/api_client.dart';
import '../../core/file_download.dart';
import '../../core/report_format.dart';

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
    'rate-overrides': 'Rate Change Approval Report',
    'payments': 'Payment Report',
    'sale-purchases': 'SalePurchase Weighment Report',
    'loans': 'Loan Report',
    'daily-collection': 'Daily Collection Report',
    'cash-book': 'Cash Book Report',
    'profit-loss': 'Profit / Loss Report',
  };

  String _reportType = 'purchases';
  DateTime? _fromDate;
  DateTime? _toDate;
  final _growerId = TextEditingController();
  final _statusCtrl = TextEditingController();
  bool _loading = false;
  String? _error;
  List<dynamic> _items = [];
  Map<String, dynamic>? _totals;
  final ScrollController _reportVerticalController = ScrollController();
  final ScrollController _reportHorizontalController = ScrollController();

  Future<void> _pickDate({required bool from}) async {
    final picked = await showDatePicker(
      context: context,
      initialDate: (from ? _fromDate : _toDate) ?? DateTime.now(),
      firstDate: DateTime(2020),
      lastDate: DateTime(2035),
    );
    if (picked != null)
      setState(() => from ? _fromDate = picked : _toDate = picked);
  }

  Map<String, dynamic> _query({String? format}) {
    final q = <String, dynamic>{
      if (_fromDate != null)
        'fromDate': DateFormat('yyyy-MM-dd').format(_fromDate!),
      if (_toDate != null) 'toDate': DateFormat('yyyy-MM-dd').format(_toDate!),
      if (format != null) 'format': format,
    };
    if (_reportType == 'profit-loss' || _reportType == 'cash-book') {
      // Profit/loss and Cash Book use only the date-range filters.
    } else if (_reportType == 'rate-overrides') {
      if (_growerId.text.trim().isNotEmpty) {
        final id = int.tryParse(_growerId.text.trim());
        if (id != null) q['transactionId'] = id;
      }
      if (_statusCtrl.text.trim().isNotEmpty) {
        q['transactionType'] = _statusCtrl.text.trim().toUpperCase();
      }
    } else if (_reportType == 'sale-purchases') {
      if (_growerId.text.trim().isNotEmpty)
        q['vehicleNumber'] = _growerId.text.trim();
      if (_statusCtrl.text.trim().isNotEmpty)
        q['status'] = _statusCtrl.text.trim();
    } else if (_reportType != 'daily-collection') {
      if (_growerId.text.trim().isNotEmpty) {
        final id = int.tryParse(_growerId.text.trim());
        if (id != null) q['growerId'] = id;
      }
      if (_statusCtrl.text.trim().isNotEmpty) {
        q[_reportType == 'purchases' ? 'paymentStatus' : 'status'] =
            _statusCtrl.text.trim();
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
      final res = await ApiClient.instance.dio.get('/api/reports/$_reportType',
          queryParameters: _query(format: 'json'));
      if (res.statusCode == 200) {
        setState(() {
          _items =
              List<dynamic>.from(res.data['items'] ?? res.data['rows'] ?? []);
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
    await downloadAndNotify(
        context, '/api/reports/$_reportType', '$_reportType-report.$ext',
        queryParameters: _query(format: format));
  }

  Future<void> _openRateEvidence(dynamic item) async {
    final id = item['overrideId'];
    if (id == null) return;
    try {
      final response = await ApiClient.instance.dio.get(
        '/api/reports/rate-overrides/$id/evidence',
        options: Options(responseType: ResponseType.bytes),
      );
      if (response.statusCode != 200) {
        throw Exception(ApiClient.errorMessage(response));
      }
      final data = response.data;
      final bytes = data is Uint8List
          ? data
          : Uint8List.fromList(List<int>.from(data as List));
      if (!mounted) return;
      await showDialog<void>(
        context: context,
        builder: (dialogContext) => Dialog(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 960, maxHeight: 760),
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              ListTile(
                title: Text('Rate Change Attachment #$id'),
                subtitle: Text(item['evidenceImageName']?.toString() ?? ''),
                trailing: IconButton(
                  tooltip: 'Close',
                  icon: const Icon(Icons.close),
                  onPressed: () => Navigator.pop(dialogContext),
                ),
              ),
              const Divider(height: 1),
              Flexible(
                child: InteractiveViewer(
                  minScale: 0.5,
                  maxScale: 5,
                  child: Image.memory(bytes, fit: BoxFit.contain),
                ),
              ),
            ]),
          ),
        ),
      );
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(ApiClient.exceptionMessage(
            error, 'Could not open the rate-change attachment.')),
      ));
    }
  }

  @override
  void initState() {
    super.initState();
    _search();
  }

  @override
  void dispose() {
    _growerId.dispose();
    _statusCtrl.dispose();
    _reportVerticalController.dispose();
    _reportHorizontalController.dispose();
    super.dispose();
  }

  List<String> get _columns {
    switch (_reportType) {
      case 'payments':
        return [
          'paymentId',
          'growerId',
          'growerName',
          'netPayableAmount',
          'paymentModeName',
          'paymentDate',
          'paymentStatus'
        ];
      case 'loans':
        return [
          'loanId',
          'growerId',
          'growerName',
          'loanAmount',
          'outstandingAmount',
          'issueDate',
          'loanStatus'
        ];
      case 'rate-overrides':
        return [
          'overrideId',
          'overrideDateTime',
          'transactionType',
          'transactionId',
          'transactionDescription',
          'vehicleNumber',
          'transactionDate',
          'masterRate',
          'approvedRate',
          'rateDifference',
          'approvedByUserName',
          'rateReasonText',
          'remark',
          'recordedByUserName',
          'evidenceSource',
          'attachment'
        ];
      case 'sale-purchases':
        return [
          'salePurchaseId',
          'tareDateTime',
          'grossDateTime',
          'item',
          'party',
          'vehicleNumber',
          'driver',
          'tareWeightQuintal',
          'grossWeightQuintal',
          'finalWeightQuintal',
          'rate',
          'amount',
          'status'
        ];
      case 'daily-collection':
        return ['date', 'vehicleCount', 'finalWeightQuintal', 'purchaseAmount'];
      case 'profit-loss':
        return [
          'expenseDate',
          'expenseName',
          'quantity',
          'unitCharge',
          'totalAmount',
          'remarks'
        ];
      case 'cash-book':
        return [
          'entryDate',
          'entryType',
          'sourceType',
          'sourceName',
          'growerId',
          'growerName',
          'paymentId',
          'netPayableAmount',
          'amount',
          'runningBalance',
          'referenceNumber'
        ];
      default:
        return [
          'purchaseId',
          'purchaseDate',
          'growerId',
          'growerName',
          'villageName',
          'vehicleNumber',
          'cuttingWeightQuintal',
          'finalWeightQuintal',
          'purchaseAmount',
          'paymentStatus'
        ];
    }
  }

  @override
  Widget build(BuildContext context) {
    final df = DateFormat('dd-MM-yyyy');
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child:
            Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text('Reports',
              style: Theme.of(context)
                  .textTheme
                  .titleLarge
                  ?.copyWith(fontWeight: FontWeight.w700)),
          const SizedBox(height: 10),
          Wrap(
              spacing: 12,
              runSpacing: 12,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                SizedBox(
                  width: 260,
                  child: DropdownButtonFormField<String>(
                    value: _reportType,
                    isExpanded: true,
                    decoration: const InputDecoration(labelText: 'Report'),
                    items: [
                      for (final e in _reportTypes.entries)
                        DropdownMenuItem(
                            value: e.key,
                            child:
                                Text(e.value, overflow: TextOverflow.ellipsis))
                    ],
                    onChanged: (v) => setState(() => _reportType = v!),
                  ),
                ),
                OutlinedButton.icon(
                  icon: const Icon(Icons.date_range),
                  onPressed: () => _pickDate(from: true),
                  label: Text(
                      _fromDate == null ? 'From Date' : df.format(_fromDate!)),
                ),
                OutlinedButton.icon(
                  icon: const Icon(Icons.date_range),
                  onPressed: () => _pickDate(from: false),
                  label:
                      Text(_toDate == null ? 'To Date' : df.format(_toDate!)),
                ),
                if (_reportType != 'daily-collection' &&
                    _reportType != 'profit-loss' &&
                    _reportType != 'cash-book')
                  SizedBox(
                      width: 160,
                      child: TextField(
                          controller: _growerId,
                          decoration: InputDecoration(
                              labelText: _reportType == 'sale-purchases'
                                  ? 'Vehicle Number'
                                  : _reportType == 'rate-overrides'
                                      ? 'Transaction ID'
                                      : 'Grower ID',
                              hintText: _reportType == 'sale-purchases'
                                  ? 'UP32AB1234'
                                  : _reportType == 'rate-overrides'
                                      ? 'Purchase / Sale ID'
                                      : '100001'))),
                if (_reportType != 'daily-collection' &&
                    _reportType != 'profit-loss' &&
                    _reportType != 'cash-book')
                  SizedBox(
                      width: 160,
                      child: TextField(
                          controller: _statusCtrl,
                          decoration: InputDecoration(
                              labelText: _reportType == 'purchases'
                                  ? 'Payment/Lock Status'
                                  : _reportType == 'rate-overrides'
                                      ? 'Transaction Type'
                                      : 'Status',
                              hintText: _reportType == 'rate-overrides'
                                  ? 'CANE or SALE'
                                  : 'e.g. PENDING'))),
                FilledButton.icon(
                    onPressed: _search,
                    icon: const Icon(Icons.search),
                    label: const Text('Search')),
                OutlinedButton.icon(
                    onPressed: () => _export('pdf'),
                    icon: const Icon(Icons.picture_as_pdf_outlined),
                    label: const Text('Print (PDF)')),
                OutlinedButton.icon(
                    onPressed: () => _export('excel'),
                    icon: const Icon(Icons.grid_on_outlined),
                    label: const Text('Export (Excel)')),
              ]),
          const SizedBox(height: 10),
          if (_error != null)
            Text(_error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error)),
          if (_totals != null && _totals!.isNotEmpty)
            Card(
              color: Theme.of(context).colorScheme.primaryContainer,
              child: Padding(
                padding: const EdgeInsets.all(10),
                child: Wrap(spacing: 20, children: [
                  for (final e in _totals!.entries)
                    Text('${e.key}: ${e.value}',
                        style: const TextStyle(fontWeight: FontWeight.w600)),
                ]),
              ),
            ),
          const SizedBox(height: 10),
          Expanded(
            child: Card(
              child: _loading
                  ? const Center(child: CircularProgressIndicator())
                  : _items.isEmpty
                      ? const Center(
                          child: Text(
                              'No records found for the selected filters.'))
                      : LayoutBuilder(
                          builder: (context, constraints) => Scrollbar(
                            controller: _reportVerticalController,
                            thumbVisibility: true,
                            trackVisibility: true,
                            interactive: true,
                            child: SingleChildScrollView(
                              controller: _reportVerticalController,
                              scrollDirection: Axis.vertical,
                              child: Scrollbar(
                                controller: _reportHorizontalController,
                                thumbVisibility: true,
                                trackVisibility: true,
                                interactive: true,
                                scrollbarOrientation:
                                    ScrollbarOrientation.bottom,
                                child: SingleChildScrollView(
                                  controller: _reportHorizontalController,
                                  scrollDirection: Axis.horizontal,
                                  child: ConstrainedBox(
                                    constraints: BoxConstraints(
                                        minWidth: constraints.maxWidth),
                                    child: DataTable(
                                      columns: [
                                        for (final c in _columns)
                                          DataColumn(label: Text(c))
                                      ],
                                      rows: [
                                        for (final item in _items)
                                          DataRow(cells: [
                                            for (final c in _columns)
                                              DataCell(c == 'attachment'
                                                  ? TextButton.icon(
                                                      onPressed: item[
                                                                  'hasAttachment'] ==
                                                              true
                                                          ? () =>
                                                              _openRateEvidence(
                                                                  item)
                                                          : null,
                                                      icon: const Icon(
                                                          Icons.image_outlined,
                                                          size: 18),
                                                      label: const Text('View'),
                                                    )
                                                  : Tooltip(
                                                      message: formatReportCell(
                                                          c, item[c]),
                                                      child: Text(
                                                          formatReportCell(
                                                              c, item[c]),
                                                          overflow: TextOverflow
                                                              .ellipsis))),
                                          ]),
                                      ],
                                    ),
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ),
            ),
          ),
        ]),
      ),
    );
  }
}
