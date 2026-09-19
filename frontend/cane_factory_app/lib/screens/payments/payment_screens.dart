import 'dart:async';
import 'dart:typed_data';
import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';
import 'package:dio/dio.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/file_download.dart';
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
  Timer? _searchTimer;
  int _previewGeneration = 0;

  void _invalidatePreview() {
    _searchTimer?.cancel();
    _previewGeneration++;
    _preview = null;
    _previewError = null;
    _previewing = false;
  }

  @override
  void dispose() {
    _searchTimer?.cancel();
    _growerCode.dispose();
    _purchaseId.dispose();
    _txnRef.dispose();
    super.dispose();
  }

  @override
  void initState() {
    super.initState();
    _loadModes();
    _load();
  }

  Future<void> _loadModes() async {
    try {
      final res = await ApiClient.instance.dio.get('/api/payment-modes');
      if (res.statusCode == 200 && mounted)
        setState(() => _paymentModes = res.data['items']);
    } catch (_) {}
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final res = await ApiClient.instance.dio.get('/api/payments');
      if (res.statusCode == 200 && mounted)
        setState(() => _items = res.data['items']);
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  void _toast(String msg, {bool error = false}) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(msg),
        backgroundColor: error
            ? Theme.of(context).colorScheme.error
            : const Color(0xFF2E7D32)));
  }

  Future<void> _pickDate({required bool from}) async {
    final d = await showDatePicker(
        context: context,
        initialDate: DateTime.now(),
        firstDate: DateTime(2020),
        lastDate: DateTime(2035));
    if (d == null || !mounted) return;
    setState(() {
      _invalidatePreview();
      from ? _fromDate = d : _toDate = d;
    });
  }

  Future<void> _loadPreview() async {
    _searchTimer?.cancel();
    final generation = ++_previewGeneration;
    if (_fromDate != null && _toDate != null && _fromDate!.isAfter(_toDate!)) {
      setState(() {
        _preview = null;
        _previewing = false;
        _previewError = 'From Date must not be after To Date.';
      });
      return;
    }
    setState(() {
      _preview = null;
      _previewError = null;
      _previewing = true;
    });
    final params = <String, dynamic>{'selectionMode': _mode};
    if (_mode == 'FARMER') params['growerCode'] = _growerCode.text.trim();
    if (_mode == 'SINGLE') params['purchaseId'] = _purchaseId.text.trim();
    if (_mode == 'DATE_RANGE' || _mode == 'FARMER') {
      if (_fromDate != null) params['fromDate'] = _fromDate!.toIso8601String();
      if (_toDate != null) params['toDate'] = _toDate!.toIso8601String();
    }
    try {
      final res = await ApiClient.instance.dio
          .get('/api/payments/eligible-purchases', queryParameters: params);
      if (!mounted || generation != _previewGeneration) return;
      setState(() {
        if (res.statusCode == 200) {
          _preview = Map<String, dynamic>.from(res.data);
        } else {
          _previewError = ApiClient.errorMessage(res);
        }
      });
    } catch (e) {
      if (mounted && generation == _previewGeneration)
        setState(() => _previewError = ApiClient.exceptionMessage(e));
    } finally {
      if (mounted && generation == _previewGeneration) setState(() => _previewing = false);
    }
  }

  Future<void> _pay() async {
    if (_preview == null)
      return _toast('Preview eligible purchases first.', error: true);
    if ((_preview!['eligiblePurchases'] as List).isEmpty)
      return _toast('No eligible purchases to pay.', error: true);
    if (_paymentModeId == null)
      return _toast('Select a Payment Mode.', error: true);
    setState(() => _paying = true);
    try {
      final data = <String, dynamic>{
        'selectionMode': _mode,
        'paymentModeId': _paymentModeId,
        'transactionRefNumber':
            _txnRef.text.trim().isEmpty ? null : _txnRef.text.trim(),
        'idempotencyKey':
            'pay-${_growerCode.text.trim()}-${DateTime.now().microsecondsSinceEpoch}',
      };
      if (_mode == 'FARMER') data['growerCode'] = _growerCode.text.trim();
      if (_mode == 'SINGLE')
        data['purchaseId'] = int.tryParse(_purchaseId.text.trim());
      if (_mode == 'DATE_RANGE' || _mode == 'FARMER') {
        if (_fromDate != null) data['fromDate'] = _fromDate!.toIso8601String();
        if (_toDate != null) data['toDate'] = _toDate!.toIso8601String();
      }
      final res =
          await ApiClient.instance.dio.post('/api/payments', data: data);
      if (!mounted) return;
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
        if (res.data['isBatch'] == true) {
          // One landscape PDF keeps every farmer's Advice/bank/payable row and all
          // purchase lines together, so a date-range payment is auditable as one batch.
          final batchAutoPrint = res.data['autoPrint'];
          if (batchAutoPrint != null) {
            await _handleAutoPrint(batchAutoPrint, 'date-range-batch');
          } else {
            // Compatibility fallback for an API that has not yet been upgraded.
            for (final raw in (res.data['payments'] as List? ?? [])) {
              final payment = Map<String, dynamic>.from(raw as Map);
              await _handleAutoPrint(payment['autoPrint'], payment['paymentId']);
            }
          }
        } else {
          await _handleAutoPrint(res.data['autoPrint'], res.data['paymentId']);
        }
      } else {
        _toast(ApiClient.errorMessage(res), error: true);
      }
    } catch (e) {
      if (mounted) _toast(ApiClient.exceptionMessage(e), error: true);
    } finally {
      if (mounted) setState(() => _paying = false);
    }
  }

  Future<void> _handleAutoPrint(dynamic autoPrint, dynamic paymentId) async {
    if (autoPrint == null) return;
    if (autoPrint['shouldAutoPrint'] != true) {
      await openPdfAfterSave(context, autoPrint['documentUrl'], 'payment-$paymentId.pdf');
      return;
    }
    final outcome = await PrintService.printDocument(
      documentUrl: autoPrint['documentUrl'],
      printerType: autoPrint['printerType'] ?? 'DotMatrix',
      printerName: autoPrint['printerName'] ?? '',
      copies: autoPrint['copies'] ?? 1,
    );
    if (mounted) _toast(outcome.message, error: !outcome.success);
  }

  Future<void> _cancelPayment(int paymentId) async {
    final reasonCtl = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('Cancel Payment $paymentId?'),
        content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                  'Purchases become payable again and any auto-deducted loan recovery is reversed.'),
              const SizedBox(height: 10),
              TextField(
                  controller: reasonCtl,
                  decoration: const InputDecoration(
                      labelText: 'Cancellation Reason (min 5 characters)')),
            ]),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(ctx, false),
              child: const Text('Back')),
          FilledButton(
              onPressed: () => Navigator.pop(ctx, true),
              child: const Text('Cancel Payment')),
        ],
      ),
    );
    if (ok != true) return;
    final res = await ApiClient.instance.dio.post(
        '/api/payments/$paymentId/cancel',
        data: {'reason': reasonCtl.text.trim()});
    if (!mounted) return;
    _toast(
        res.statusCode == 200
            ? res.data['message']
            : ApiClient.errorMessage(res),
        error: res.statusCode != 200);
    _load();
  }

  Future<void> _openPaymentEvidence(int paymentId) async {
    final res =
        await ApiClient.instance.dio.get('/api/payments/$paymentId/evidence-context');
    if (!mounted) return;
    if (res.statusCode != 200) {
      _toast(ApiClient.errorMessage(res), error: true);
      return;
    }
    await showDialog<void>(
        context: context,
        barrierDismissible: false,
        builder: (_) => _PaymentEvidenceDialog(
            paymentId: paymentId, initialContext: Map<String, dynamic>.from(res.data)));
  }

  Future<void> _uploadEvidence(int paymentId) async {
    final picked = await FilePicker.platform.pickFiles(
        type: FileType.custom, allowedExtensions: const ['jpg', 'jpeg', 'png']);
    final path = picked?.files.single.path;
    if (path == null) return;
    final res = await ApiClient.instance.dio.post('/api/payments/$paymentId/images/upload',
        data: FormData.fromMap({'image': await MultipartFile.fromFile(path)}));
    if (!mounted) return;
    _toast(res.statusCode == 200 ? res.data['message'] : ApiClient.errorMessage(res),
        error: res.statusCode != 200);
  }

  Future<void> _viewEvidence(int paymentId) async {
    final res = await ApiClient.instance.dio.get('/api/payments/$paymentId/images');
    if (!mounted) return;
    if (res.statusCode != 200) return _toast(ApiClient.errorMessage(res), error: true);
    final images = List<dynamic>.from(res.data);
    await showDialog<void>(context: context, builder: (ctx) => AlertDialog(
      title: Text('Cash Evidence • Payment $paymentId'),
      content: SizedBox(width: 720, height: 480, child: images.isEmpty
          ? const Center(child: Text('No evidence images saved.'))
          : GridView.builder(itemCount: images.length, gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(maxCrossAxisExtent: 220, mainAxisSpacing: 10, crossAxisSpacing: 10), itemBuilder: (_, index) {
              final image = images[index];
              return FutureBuilder<Response<List<int>>>(future: ApiClient.instance.dio.get<List<int>>('/api/images/payment/${image['id']}/file', options: Options(responseType: ResponseType.bytes)), builder: (_, snap) {
                if (!snap.hasData || snap.data!.data == null) return const Center(child: CircularProgressIndicator());
                return InkWell(onTap: () => showDialog<void>(context: ctx, builder: (_) => Dialog(child: InteractiveViewer(child: Image.memory(Uint8List.fromList(snap.data!.data!))))), child: Column(children: [Expanded(child: Image.memory(Uint8List.fromList(snap.data!.data!), fit: BoxFit.cover)), Text(image['imageName'].toString(), overflow: TextOverflow.ellipsis)]));
              });
            })),
      actions: [TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Close'))],
    ));
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final canCreate = auth.can('Payment.Create');
    final canCancel = auth.can('Payment.Cancel');
    final canEvidence = auth.can('CashEvidence.Create');
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        if (canCreate) _buildForm(context),
        const SizedBox(height: 10),
        Row(children: [
          Text('Payment Register',
              style: Theme.of(context)
                  .textTheme
                  .titleMedium
                  ?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ]),
        const SizedBox(height: 6),
        Expanded(
            child: Card(
                child: _loading
                    ? const Center(child: CircularProgressIndicator())
                    : _buildTable(canCancel, canEvidence))),
      ]),
    );
  }

  Widget _buildForm(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('MAKE PAYMENT',
              style: TextStyle(
                  fontWeight: FontWeight.w800,
                  color: Theme.of(context).colorScheme.primary)),
          const SizedBox(height: 10),
          SegmentedButton<String>(
            segments: const [
              ButtonSegment(value: 'SINGLE', label: Text('Single Purchase')),
              ButtonSegment(value: 'DATE_RANGE', label: Text('Date Range (All Farmers)')),
              ButtonSegment(
                  value: 'FARMER', label: Text('Farmer-wise (All Pending)')),
            ],
            selected: {_mode},
            onSelectionChanged: (s) => setState(() {
              _mode = s.first;
              _invalidatePreview();
            }),
          ),
          const SizedBox(height: 12),
          Wrap(
              spacing: 12,
              runSpacing: 12,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                if (_mode == 'SINGLE')
                  SizedBox(
                    width: 200,
                    child: TextField(
                      controller: _purchaseId,
                      onChanged: (_) => setState(_invalidatePreview),
                      keyboardType: TextInputType.number,
                      decoration: const InputDecoration(
                          labelText: 'Purchase ID', hintText: 'Example: 105'),
                      onSubmitted: (_) => _previewing ? null : _loadPreview(),
                    ),
                  ),
                if (_mode == 'FARMER')
                  SizedBox(
                    width: 200,
                    child: TextField(
                      controller: _growerCode,
                      decoration: const InputDecoration(
                          labelText: 'Grower Code or Name', hintText: 'Example: 101/1 or Ramesh'),
                      onChanged: (v) {
                        setState(_invalidatePreview);
                        if (v.trim().isNotEmpty) {
                          _searchTimer = Timer(const Duration(milliseconds: 350), _loadPreview);
                        }
                      },
                      onSubmitted: (_) => _previewing ? null : _loadPreview(),
                    ),
                  ),
                if (_mode == 'DATE_RANGE' || _mode == 'FARMER') ...[
                  OutlinedButton(
                      onPressed: () => _pickDate(from: true),
                      child: Text(_fromDate == null
                          ? 'From Date'
                          : DateFormat('dd-MM-yyyy').format(_fromDate!))),
                  OutlinedButton(
                      onPressed: () => _pickDate(from: false),
                      child: Text(_toDate == null
                          ? 'To Date'
                          : DateFormat('dd-MM-yyyy').format(_toDate!))),
                ],
                FilledButton.tonal(
                    onPressed: _previewing ? null : _loadPreview,
                    child: Text(_previewing ? 'Loading...' : 'Preview')),
              ]),
          if (_previewError != null)
            Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(_previewError!,
                    style:
                        TextStyle(color: Theme.of(context).colorScheme.error))),
          if (_preview != null) _buildPreview(context),
          if (_preview != null) ...[
            const SizedBox(height: 14),
            Wrap(
                spacing: 12,
                runSpacing: 12,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  SizedBox(
                    width: 220,
                    child: DropdownButtonFormField<int>(
                      isExpanded: true,
                      value: _paymentModeId,
                      decoration:
                          const InputDecoration(labelText: 'Payment Mode'),
                      items: [
                        for (final m in _paymentModes)
                          DropdownMenuItem(
                              value: m['id'] as int, child: Text(m['modeName'], overflow: TextOverflow.ellipsis))
                      ],
                      onChanged: (v) => setState(() => _paymentModeId = v),
                    ),
                  ),
                  SizedBox(
                    width: 220,
                    child: TextField(
                      controller: _txnRef,
                      decoration: const InputDecoration(
                          labelText: 'Transaction Ref (optional)',
                          hintText: 'UPI/Bank Ref No.'),
                    ),
                  ),
                ]),
            const SizedBox(height: 14),
            FilledButton.icon(
              onPressed: _paying ? null : _pay,
              icon: const Icon(Icons.payments_outlined),
              label: Text(_paying ? 'Processing...' : 'COMPLETE PAYMENT'),
              style: FilledButton.styleFrom(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 30, vertical: 16)),
            ),
          ],
        ]),
      ),
    );
  }

  Widget _buildPreview(BuildContext context) {
    final eligible = _preview!['eligiblePurchases'] as List;
    final loans = _preview!['outstandingLoans'] as List;
    final batch = _preview!['isBatch'] == true;
    final batchGrowers = _preview!['batchGrowers'] as List? ?? [];
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Container(
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
            color:
                Theme.of(context).colorScheme.primary.withValues(alpha: 0.06),
            borderRadius: BorderRadius.circular(8)),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          if (_preview!['statusMessage'] != null)
            Text(_preview!['statusMessage'].toString(),
                style: TextStyle(
                    color: Theme.of(context).colorScheme.error,
                    fontWeight: FontWeight.w700)),
          Text(
              '${batch ? 'Farmers: ${_preview!['growerCount'] ?? batchGrowers.length}  •  ' : ''}Purchase Count: ${_preview!['purchaseCount'] ?? eligible.length}  •  Final Weight: ${((_preview!['totalFinalWeight'] ?? 0) as num).toStringAsFixed(2)} Qtl  •  Total: Rs ${(_preview!['totalPurchaseAmount'] as num).toStringAsFixed(2)}',
              style: const TextStyle(fontWeight: FontWeight.w700)),
          if (batch) ...[
            const SizedBox(height: 8),
            const Text('Each row will create a separate Payment ID, Advice Number, PDF slip and cash-evidence capture.',
                style: TextStyle(fontSize: 12, fontStyle: FontStyle.italic)),
            const SizedBox(height: 5),
            for (final raw in batchGrowers)
              Builder(builder: (_) {
                final g = Map<String, dynamic>.from(raw as Map);
                return Text('${g['growerCode']} • ${g['growerName']} — ${g['purchaseCount']} purchase(s), '
                    'Rs ${(g['estimatedNetPayable'] as num).toStringAsFixed(2)} net',
                    style: const TextStyle(fontSize: 12));
              }),
          ],
          if (loans.isNotEmpty) ...[
            const SizedBox(height: 6),
            Text(
                'Outstanding Loans: Rs ${(_preview!['totalOutstandingLoan'] as num).toStringAsFixed(2)}  '
                '(auto-deducting Rs ${(_preview!['estimatedLoanDeduction'] as num).toStringAsFixed(2)})',
                style: const TextStyle(
                    color: Colors.orange, fontWeight: FontWeight.w600)),
          ],
          const SizedBox(height: 6),
          Text(
              'Estimated Net Payable: Rs ${(_preview!['estimatedNetPayable'] as num).toStringAsFixed(2)}',
              style: TextStyle(
                  fontWeight: FontWeight.w800,
                  color: Theme.of(context).colorScheme.primary,
                  fontSize: 15)),
        ]),
      ),
    );
  }

  Widget _buildTable(bool canCancel, bool canEvidence) {
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
          if (canEvidence) const DataColumn(label: Text('Cash Evidence')),
          if (canCancel) const DataColumn(label: Text('Actions')),
        ], rows: [
          for (final p in _items)
            DataRow(cells: [
              DataCell(Text('${p['paymentId']}',
                  style: const TextStyle(fontWeight: FontWeight.w700))),
              DataCell(Text('${p['adviceNumber']}')),
              DataCell(Text('${p['growerCode']} ${p['growerName']}')),
              DataCell(
                  Text((p['totalPurchaseAmount'] as num).toStringAsFixed(2))),
              DataCell(
                  Text((p['loanDeductedAmount'] as num).toStringAsFixed(2))),
              DataCell(Text((p['netPayableAmount'] as num).toStringAsFixed(2))),
              DataCell(Text('${p['paymentModeName']}')),
              DataCell(Chip(
                  label: Text(p['paymentStatus'] ?? '-',
                      style:
                          const TextStyle(fontSize: 10, color: Colors.white)),
                  backgroundColor: p['paymentStatus'] == 'COMPLETED'
                      ? const Color(0xFF2E7D32)
                      : Colors.red,
                  visualDensity: VisualDensity.compact)),
              if (canEvidence)
                DataCell(p['paymentModeName']?.toString().toUpperCase() == 'CASH'
                    ? Row(mainAxisSize: MainAxisSize.min, children: [
                        IconButton(
                            tooltip: 'Open live camera / Capture or Retake',
                            icon: const Icon(Icons.camera_alt_outlined, size: 18),
                            onPressed: () => _openPaymentEvidence(p['paymentId'] as int)),
                        IconButton(
                            tooltip: 'View saved evidence / Retake if needed',
                            icon: const Icon(Icons.photo_library_outlined, size: 18),
                            onPressed: () => _viewEvidence(p['paymentId'] as int)),
                        IconButton(
                            tooltip: 'Upload evidence image',
                            icon: const Icon(Icons.upload_file_outlined, size: 18),
                            onPressed: () => _uploadEvidence(p['paymentId'] as int)),
                      ])
                    : const Text('-')),
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

/// Cash payment proof is intentionally captured only after the operator can see a real live frame.
/// Existing evidence changes are not deleted: a retake creates a new active JPG and retires the prior row.
class _PaymentEvidenceDialog extends StatefulWidget {
  const _PaymentEvidenceDialog(
      {required this.paymentId, required this.initialContext});

  final int paymentId;
  final Map<String, dynamic> initialContext;

  @override
  State<_PaymentEvidenceDialog> createState() => _PaymentEvidenceDialogState();
}

class _PaymentEvidenceDialogState extends State<_PaymentEvidenceDialog> {
  late Map<String, dynamic> _contextData;
  int? _purchaseId;
  Uint8List? _preview;
  String? _previewError;
  bool _previewLoading = false;
  bool _capturing = false;
  Timer? _previewTimer;

  @override
  void initState() {
    super.initState();
    _contextData = Map<String, dynamic>.from(widget.initialContext);
    _selectFirstPurchase();
    _refreshPreview();
    _previewTimer = Timer.periodic(
        const Duration(seconds: 2), (_) {
      if (mounted && !_capturing) _refreshPreview(silent: true);
    });
  }

  @override
  void dispose() {
    _previewTimer?.cancel();
    super.dispose();
  }

  List<dynamic> get _purchases =>
      List<dynamic>.from(_contextData['purchases'] as List? ?? const []);

  void _selectFirstPurchase() {
    final purchases = _purchases;
    if (purchases.isEmpty) return;
    final ids = purchases
        .map((purchase) => (purchase as Map)['purchaseId'] as num)
        .map((id) => id.toInt())
        .toSet();
    if (_purchaseId == null || !ids.contains(_purchaseId)) {
      _purchaseId = ids.first;
    }
  }

  Map<dynamic, dynamic>? get _selectedPurchase {
    for (final purchase in _purchases) {
      final item = purchase as Map;
      if ((item['purchaseId'] as num).toInt() == _purchaseId) return item;
    }
    return null;
  }

  Future<void> _refreshPreview({bool silent = false}) async {
    if (_previewLoading || _capturing) return;
    if (!silent && mounted) setState(() => _previewLoading = true);
    _previewLoading = true;
    try {
      final res = await ApiClient.instance.dio.get(
          '/api/payments/${widget.paymentId}/evidence/preview',
          options: Options(
              responseType: ResponseType.bytes,
              validateStatus: (status) => status != null && status < 500));
      if (!mounted) return;
      if (res.statusCode == 200 && res.data != null) {
        setState(() {
          _preview = Uint8List.fromList(List<int>.from(res.data as List));
          _previewError = null;
        });
      } else {
        setState(() => _previewError = ApiClient.errorMessage(res));
      }
    } catch (error) {
      if (mounted) setState(() => _previewError = ApiClient.exceptionMessage(error));
    } finally {
      _previewLoading = false;
      if (!silent && mounted) setState(() {});
    }
  }

  Future<void> _reloadContext() async {
    final res = await ApiClient.instance.dio
        .get('/api/payments/${widget.paymentId}/evidence-context');
    if (res.statusCode != 200) {
      if (mounted) setState(() => _previewError = ApiClient.errorMessage(res));
      return;
    }
    if (!mounted) return;
    setState(() {
      _contextData = Map<String, dynamic>.from(res.data);
      _selectFirstPurchase();
    });
  }

  Future<void> _capture() async {
    final selected = _selectedPurchase;
    if (selected == null || _capturing) return;
    final evidence = selected['evidence'];
    final retake = evidence != null;
    setState(() => _capturing = true);
    try {
      final res = await ApiClient.instance.dio.post(
          '/api/payments/${widget.paymentId}/images/capture',
          data: {
            'purchaseId': _purchaseId,
            'replaceExisting': retake,
          });
      if (!mounted) return;
      if (res.statusCode == 200) {
        await _reloadContext();
        await _refreshPreview();
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(SnackBar(
              content: Text(res.data['message']?.toString() ??
                  (retake ? 'Evidence retaken.' : 'Evidence captured.'))));
        }
      } else {
        setState(() => _previewError = ApiClient.errorMessage(res));
      }
    } catch (error) {
      if (mounted) setState(() => _previewError = ApiClient.exceptionMessage(error));
    } finally {
      if (mounted) setState(() => _capturing = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final camera = _contextData['camera'] as Map?;
    final selected = _selectedPurchase;
    final hasEvidence = selected?['evidence'] != null;
    final purchases = _purchases;
    return AlertDialog(
      insetPadding: const EdgeInsets.all(24),
      title: Text('Cash Payment Evidence • Advice ' +
          (_contextData['adviceNumber']?.toString() ?? '-')),
      content: SizedBox(
          width: 800,
          height: 570,
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Text(
                'Grower: ' +
                    (_contextData['growerCode']?.toString() ?? '-') +
                    ' • Net payable: Rs ' +
                    ((_contextData['netPayableAmount'] as num?)?.toStringAsFixed(2) ?? '-'),
                style: const TextStyle(fontWeight: FontWeight.w700)),
            const SizedBox(height: 8),
            if (camera == null)
              const Text(
                  'Payment Evidence Camera is not configured. Developer: choose an active camera in Configuration > Cameras.',
                  style: TextStyle(color: Colors.red))
            else
              Text(
                  'Live camera: Camera ' +
                      ((camera['cameraNumber'] as num?)?.toInt().toString().padLeft(2, '0') ?? '-') +
                      ' • ' +
                      (camera['vendor']?.toString() ?? '')),
            const SizedBox(height: 10),
            Expanded(
                child: Container(
                    color: Colors.black,
                    alignment: Alignment.center,
                    child: _preview != null
                        ? Image.memory(_preview!, fit: BoxFit.contain,
                            gaplessPlayback: true)
                        : Column(mainAxisAlignment: MainAxisAlignment.center, children: [
                            if (_previewLoading) const CircularProgressIndicator(),
                            const SizedBox(height: 10),
                            Text(_previewError ?? 'Connecting to live camera...',
                                textAlign: TextAlign.center,
                                style: const TextStyle(color: Colors.white)),
                          ]))),
            const SizedBox(height: 12),
            Row(children: [
              const Text('Purchase ID: '),
              const SizedBox(width: 8),
              SizedBox(
                  width: 240,
                  child: DropdownButtonFormField<int>(
                      value: _purchaseId,
                      isExpanded: true,
                      items: [
                        for (final purchase in purchases)
                          DropdownMenuItem<int>(
                              value: ((purchase as Map)['purchaseId'] as num).toInt(),
                              child: Text('Purchase ' +
                                  purchase['purchaseId'].toString() +
                                  ' • Vehicle ' +
                                  (purchase['vehicleNumber']?.toString() ?? '-')))
                      ],
                      onChanged: (value) => setState(() => _purchaseId = value))),
              const SizedBox(width: 18),
              Expanded(
                  child: Text(
                      hasEvidence
                          ? 'Existing evidence found. Capture will safely RETAKE it.'
                          : 'No active evidence for this purchase.',
                      style: TextStyle(
                          color: hasEvidence ? Colors.orange : Colors.grey.shade700,
                          fontWeight: FontWeight.w600))),
            ]),
            const SizedBox(height: 8),
            Text(
                'Save path: ' +
                    (_contextData['configuredPath']?.toString() ?? r'C:\WeighmentImage\Payment') +
                    r'\yyyy-MM-dd\GrowerCode-AdviceNo-PurchaseId-PaymentId.jpg',
                style: const TextStyle(fontSize: 12)),
          ])),
      actions: [
        TextButton(
            onPressed: _capturing ? null : () => _refreshPreview(),
            child: const Text('Refresh Live View')),
        TextButton(
            onPressed: _capturing ? null : () => Navigator.pop(context),
            child: const Text('Close')),
        FilledButton.icon(
            onPressed: camera == null || selected == null || _capturing
                ? null
                : _capture,
            icon: _capturing
                ? const SizedBox(
                    width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
                : Icon(hasEvidence ? Icons.refresh : Icons.camera_alt_outlined),
            label: Text(_capturing
                ? 'Saving...'
                : hasEvidence
                    ? 'Retake Image'
                    : 'Capture Image')),
      ],
    );
  }
}
