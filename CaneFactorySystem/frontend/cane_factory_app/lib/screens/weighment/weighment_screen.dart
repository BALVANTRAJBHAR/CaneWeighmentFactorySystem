import 'dart:typed_data';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:dio/dio.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/print_service.dart';
import '../../core/sound_controller.dart';
import '../../providers/auth_provider.dart';
import '../../providers/live_weight_provider.dart';

/// UNIFIED CANE WEIGHMENT MAIN FORM.
/// One form with (O) GROSS / (O) TARE radio modes - the visible sections, grid and
/// buttons change dynamically without opening another window. Live weight and the
/// camera panel are common sections. Keyboard-first: ENTER lookups and quick save.
class WeighmentScreen extends StatefulWidget {
  const WeighmentScreen({super.key});
  @override
  State<WeighmentScreen> createState() => _WeighmentScreenState();
}

class _WeighmentScreenState extends State<WeighmentScreen> {
  bool _grossMode = true;
  final _sound = SoundController();

  // GROSS state
  final _growerCode = TextEditingController();
  Map<String, dynamic>? _grower;
  String? _growerError;
  int? _vehicleTypeId;
  final _vehicleNumber = TextEditingController();
  int? _varietyTypeId;
  int? _varietyId;
  final _cutting = TextEditingController(text: '0.00');
  final _tax = TextEditingController(text: '0.00');

  // TARE state
  final _purchaseIdCtl = TextEditingController();
  Map<String, dynamic>? _selectedPurchase;
  String? _tareError;

  List _pending = [];
  List _vehicleTypes = [];
  List _varietyTypes = [];
  List _varieties = [];
  List _cameras = [];
  bool _saving = false;

  // Phase 6: evidence images captured (async, in the background) for the last saved purchase
  int? _lastCapturedPurchaseId;
  List _capturedImages = [];
  String? _lastPrintStage; // Phase 7: GROSS|TARE for the manual Reprint button

  @override
  void initState() {
    super.initState();
    context.read<LiveWeightProvider>().start();
    _sound.loadConfig();
    _loadRefs();
    _loadPending();
  }

  @override
  void dispose() {
    _sound.dispose();
    super.dispose();
  }

  Future<void> _loadRefs() async {
    try {
      final vt = await ApiClient.instance.dio.get('/api/vehicle-types');
      final vart = await ApiClient.instance.dio.get('/api/variety-types');
      final cams = await ApiClient.instance.dio.get('/api/config/cameras');
      if (!mounted) return;
      setState(() {
        if (vt.statusCode == 200) _vehicleTypes = vt.data['items'];
        if (vart.statusCode == 200) _varietyTypes = vart.data['items'];
        if (cams.statusCode == 200 && cams.data is List) {
          _cameras = (cams.data as List).where((c) => c['liveViewEnabled'] == true && c['status'] == true).toList();
        }
      });
    } catch (_) {}
  }

  Future<void> _loadPending() async {
    try {
      final res = await ApiClient.instance.dio.get('/api/weighment/pending-tare');
      if (res.statusCode == 200 && mounted) setState(() => _pending = res.data);
    } catch (_) {}
  }

  /// Auto Print (Phase 7): server tells us whether to print and the ready-made document URL;
  /// failures never affect the already-completed save - just a clear toast + manual Reprint option.
  Future<void> _autoPrint(dynamic autoPrint) async {
    if (autoPrint == null) return;
    final outcome = await PrintService.printDocument(
      documentUrl: autoPrint['documentUrl'],
      printerType: autoPrint['printerType'] ?? 'DotMatrix',
      printerName: autoPrint['printerName'] ?? '',
      copies: autoPrint['copies'] ?? 1,
    );
    if (mounted) _toast(outcome.message, error: !outcome.success);
  }

  /// Manual (re)print for the last saved purchase - authorized users can reprint if Auto Print
  /// failed or a physical copy was damaged; every call is audited server-side (Print/Reprint).
  Future<void> _manualPrint(String stage) async {
    if (_lastCapturedPurchaseId == null) return;
    final cfgRes = await ApiClient.instance.dio.get('/api/config/print');
    if (cfgRes.statusCode != 200) {
      _toast('Could not load print configuration.', error: true);
      return;
    }
    final cfg = cfgRes.data;
    final outcome = await PrintService.printDocument(
      documentUrl: '/api/print/purchase/$_lastCapturedPurchaseId?stage=$stage&format=final',
      printerType: cfg['printerType'] ?? 'DotMatrix',
      printerName: cfg['printerName'] ?? '',
    );
    if (mounted) _toast(outcome.message, error: !outcome.success);
  }

  Future<void> _loadVarieties(int typeId) async {
    final res = await ApiClient.instance.dio.get('/api/varieties/by-type/$typeId');
    if (res.statusCode == 200 && mounted) setState(() => _varieties = res.data);
  }

  /// Camera capture runs in the background on the server after Gross/Tare save;
  /// wait briefly then fetch the metadata list so thumbnails appear automatically.
  Future<void> _loadCapturedImages(int purchaseId) async {
    _lastCapturedPurchaseId = purchaseId;
    await Future.delayed(const Duration(seconds: 2));
    try {
      final res = await ApiClient.instance.dio.get('/api/purchases/$purchaseId/images');
      if (res.statusCode == 200 && mounted && _lastCapturedPurchaseId == purchaseId) {
        setState(() => _capturedImages = res.data);
      }
    } catch (_) {}
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

  Future<void> _selectPurchase(int id) async {
    setState(() {
      _selectedPurchase = null;
      _tareError = null;
    });
    final res = await ApiClient.instance.dio.get('/api/weighment/purchase/$id/for-tare');
    setState(() {
      if (res.statusCode == 200) {
        _selectedPurchase = Map<String, dynamic>.from(res.data);
        _purchaseIdCtl.text = '$id';
      } else {
        _tareError = ApiClient.errorMessage(res);
      }
    });
  }

  void _toast(String msg, {bool error = false}) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(msg, style: const TextStyle(fontSize: 15)),
        duration: Duration(seconds: error ? 5 : 4),
        backgroundColor: error ? Theme.of(context).colorScheme.error : const Color(0xFF2E7D32)));
  }

  Future<void> _saveGross() async {
    final live = context.read<LiveWeightProvider>().current;
    if (_grower == null) return _toast('Lookup a valid Grower Code first (press ENTER).', error: true);
    if (_vehicleTypeId == null) return _toast('Select Vehicle Type.', error: true);
    if (_vehicleNumber.text.trim().length < 4) return _toast('Enter a valid Vehicle Number. Example: UP32AB1234', error: true);
    if (_varietyTypeId == null || _varietyId == null) return _toast('Select Variety Type and Variety.', error: true);
    setState(() => _saving = true);
    final res = await ApiClient.instance.dio.post('/api/weighment/gross', data: {
      'growerCode': _grower!['growerCode'],
      'vehicleTypeId': _vehicleTypeId,
      'vehicleNumber': _vehicleNumber.text.trim().toUpperCase(),
      'varietyTypeId': _varietyTypeId,
      'varietyId': _varietyId,
      'cuttingPercent': double.tryParse(_cutting.text) ?? 0,
      'taxPercent': double.tryParse(_tax.text) ?? 0,
      'scaleReadingKg': live.weightKg,
      'idempotencyKey': 'gross-${_grower!['growerCode']}-${DateTime.now().millisecondsSinceEpoch ~/ 30000}',
    });
    setState(() => _saving = false);
    if (res.statusCode == 200) {
      _toast(res.data['message']);
      _sound.onWeighmentSaved();
      final purchaseId = res.data['purchaseId'] as int;
      // reset new-entry fields; keep configuration selections for fast operation
      setState(() {
        _grower = null;
        _growerCode.clear();
        _vehicleNumber.clear();
        _capturedImages = [];
        _lastPrintStage = 'GROSS';
      });
      _loadPending();
      _loadCapturedImages(purchaseId);
      _autoPrint(res.data['autoPrint']);
    } else {
      _toast(ApiClient.errorMessage(res), error: true);
    }
  }

  Future<void> _saveTare() async {
    final live = context.read<LiveWeightProvider>().current;
    if (_selectedPurchase == null) return _toast('Select a pending purchase (double-click a row or enter Purchase ID).', error: true);
    setState(() => _saving = true);
    final res = await ApiClient.instance.dio.post('/api/weighment/tare', data: {
      'purchaseId': _selectedPurchase!['purchaseId'],
      'scaleReadingKg': live.weightKg,
      'idempotencyKey': 'tare-${_selectedPurchase!['purchaseId']}',
    });
    setState(() => _saving = false);
    if (res.statusCode == 200) {
      _toast('${res.data['message']} Purchase ID: ${res.data['purchaseId']}');
      _sound.onWeighmentSaved();
      final purchaseId = _selectedPurchase!['purchaseId'] as int;
      setState(() {
        _selectedPurchase = null;
        _purchaseIdCtl.clear();
        _capturedImages = [];
        _lastPrintStage = 'TARE';
      });
      _loadPending();
      _loadCapturedImages(purchaseId);
      _autoPrint(res.data['autoPrint']);
    } else {
      _toast(ApiClient.errorMessage(res), error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final live = context.watch<LiveWeightProvider>().current;
    final auth = context.watch<AuthProvider>();
    _sound.onWeight(live.weightQuintal);
    final scheme = Theme.of(context).colorScheme;

    return Padding(
      padding: const EdgeInsets.all(10),
      child: Column(children: [
        // ---------- TOP: mode radio + live weight + status ----------
        Card(
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
            child: Row(children: [
              for (final m in [(true, 'GROSS'), (false, 'TARE')])
                Row(mainAxisSize: MainAxisSize.min, children: [
                  Radio<bool>(
                      value: m.$1,
                      groupValue: _grossMode,
                      onChanged: (v) {
                        _sound.reset(); // mode change stops any repeating message
                        setState(() => _grossMode = v!);
                        _loadPending();
                      }),
                  Text(m.$2,
                      style: TextStyle(
                          fontWeight: FontWeight.w800,
                          fontSize: 16,
                          color: _grossMode == m.$1 ? scheme.primary : null)),
                  const SizedBox(width: 10),
                ]),
              const SizedBox(width: 20),
              // LIVE WEIGHT display
              Expanded(
                child: Container(
                  padding: const EdgeInsets.symmetric(vertical: 6, horizontal: 18),
                  decoration: BoxDecoration(
                    color: const Color(0xFF0D1B0F),
                    borderRadius: BorderRadius.circular(10),
                    border: Border.all(color: live.stable ? const Color(0xFF2E7D32) : Colors.orange, width: 2),
                  ),
                  child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                    Text(live.weightQuintal.toStringAsFixed(2),
                        style: const TextStyle(
                            fontFamily: 'monospace',
                            fontSize: 34,
                            fontWeight: FontWeight.w900,
                            color: Color(0xFF7CFC8F))),
                    const SizedBox(width: 8),
                    const Text('Qtl', style: TextStyle(color: Color(0xFF7CFC8F), fontSize: 16)),
                    const SizedBox(width: 16),
                    Text('(${live.weightKg.toStringAsFixed(0)} KG)',
                        style: const TextStyle(color: Colors.white54, fontSize: 13)),
                  ]),
                ),
              ),
              const SizedBox(width: 14),
              Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
                Row(children: [
                  Icon(live.deviceConnected ? Icons.usb : Icons.usb_off,
                      size: 15, color: live.deviceConnected ? const Color(0xFF2E7D32) : Colors.red),
                  const SizedBox(width: 4),
                  Text(live.deviceConnected ? 'Digitizer: ${live.deviceName ?? "Connected"}' : 'Digitizer: Disconnected',
                      style: const TextStyle(fontSize: 11)),
                ]),
                Text(live.stable ? 'STABLE' : 'UNSTABLE',
                    style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w700,
                        color: live.stable ? const Color(0xFF2E7D32) : Colors.orange)),
                Text(
                    live.lastReceivedAt == null
                        ? 'No data yet'
                        : 'Last: ${DateFormat('HH:mm:ss').format(live.lastReceivedAt!.toLocal())}',
                    style: const TextStyle(fontSize: 10)),
              ]),
              const SizedBox(width: 12),
              Chip(
                avatar: const Icon(Icons.person, size: 14),
                visualDensity: VisualDensity.compact,
                label: Text('${auth.user?['username']} • ${auth.roles.join(",")}', style: const TextStyle(fontSize: 11)),
              ),
            ]),
          ),
        ),
        const SizedBox(height: 6),
        // ---------- CENTER: entry panel + cameras ----------
        Expanded(
          child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Expanded(flex: 3, child: _grossMode ? _grossPanel() : _tarePanel()),
            if (_cameras.isNotEmpty) ...[
              const SizedBox(width: 6),
              SizedBox(width: 230, child: _cameraPanel()),
            ],
          ]),
        ),
        const SizedBox(height: 6),
        // ---------- BOTTOM: pending grid ----------
        SizedBox(height: 220, child: _pendingGrid()),
      ]),
    );
  }

  Widget _grossPanel() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: SingleChildScrollView(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('GROSS ENTRY', style: TextStyle(fontWeight: FontWeight.w800, color: Theme.of(context).colorScheme.primary)),
            const SizedBox(height: 10),
            Row(children: [
              SizedBox(
                width: 200,
                child: TextField(
                  controller: _growerCode,
                  autofocus: true,
                  decoration: const InputDecoration(
                      labelText: 'Grower Code', hintText: 'Example: 101/1', prefixIcon: Icon(Icons.badge_outlined, size: 18)),
                  onSubmitted: (_) => _lookupGrower(),
                ),
              ),
              const SizedBox(width: 8),
              FilledButton.tonal(onPressed: _lookupGrower, child: const Text('Lookup (Enter)')),
              const SizedBox(width: 14),
              if (_grower != null)
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                        color: const Color(0xFF2E7D32).withOpacity(0.10), borderRadius: BorderRadius.circular(8)),
                    child: Text(
                      '${_grower!['growerName']}  S/o ${_grower!['fatherName']}  •  Village: ${_grower!['villageName']}  •  '
                      'Bank: ${_grower!['bankName'] ?? '-'} ${_grower!['accountMasked'] ?? ''}',
                      style: const TextStyle(fontWeight: FontWeight.w600),
                    ),
                  ),
                ),
              if (_growerError != null)
                Expanded(child: Text(_growerError!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
            ]),
            const SizedBox(height: 12),
            Wrap(spacing: 12, runSpacing: 12, children: [
              SizedBox(
                width: 190,
                child: DropdownButtonFormField<int>(
                  value: _vehicleTypeId,
                  decoration: const InputDecoration(labelText: 'Vehicle Type', hintText: 'Select Vehicle Type'),
                  items: [for (final v in _vehicleTypes) DropdownMenuItem(value: v['id'] as int, child: Text(v['vehicleTypeName']))],
                  onChanged: (v) => setState(() => _vehicleTypeId = v),
                ),
              ),
              SizedBox(
                width: 190,
                child: TextField(
                  controller: _vehicleNumber,
                  textCapitalization: TextCapitalization.characters,
                  decoration: const InputDecoration(labelText: 'Vehicle Number', hintText: 'Example: UP32AB1234'),
                ),
              ),
              SizedBox(
                width: 190,
                child: DropdownButtonFormField<int>(
                  value: _varietyTypeId,
                  decoration: const InputDecoration(labelText: 'Variety Type', hintText: 'Select Variety Type'),
                  items: [for (final v in _varietyTypes) DropdownMenuItem(value: v['id'] as int, child: Text(v['varietyTypeName']))],
                  onChanged: (v) {
                    setState(() {
                      _varietyTypeId = v;
                      _varietyId = null;
                      _varieties = [];
                    });
                    if (v != null) _loadVarieties(v);
                  },
                ),
              ),
              SizedBox(
                width: 190,
                child: DropdownButtonFormField<int>(
                  value: _varietyId,
                  decoration: const InputDecoration(labelText: 'Variety', hintText: 'Select Variety'),
                  items: [for (final v in _varieties) DropdownMenuItem(value: v['id'] as int, child: Text(v['varietyName']))],
                  onChanged: (v) => setState(() => _varietyId = v),
                ),
              ),
              SizedBox(
                width: 120,
                child: TextField(
                  controller: _cutting,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'^\d*\.?\d{0,2}'))],
                  decoration: const InputDecoration(labelText: 'Cutting %', hintText: 'Example: 2.00'),
                ),
              ),
              SizedBox(
                width: 120,
                child: TextField(
                  controller: _tax,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'^\d*\.?\d{0,2}'))],
                  decoration: const InputDecoration(labelText: 'Tax %', hintText: 'Example: 0.00'),
                ),
              ),
            ]),
            const SizedBox(height: 16),
            FilledButton.icon(
              onPressed: _saving ? null : _saveGross,
              icon: const Icon(Icons.save_outlined),
              label: Text(_saving ? 'Saving...' : 'SAVE GROSS'),
              style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(horizontal: 34, vertical: 18)),
            ),
          ]),
        ),
      ),
    );
  }

  Widget _tarePanel() {
    final p = _selectedPurchase;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: SingleChildScrollView(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('TARE ENTRY', style: TextStyle(fontWeight: FontWeight.w800, color: Theme.of(context).colorScheme.primary)),
            const SizedBox(height: 10),
            Row(children: [
              SizedBox(
                width: 210,
                child: TextField(
                  controller: _purchaseIdCtl,
                  keyboardType: TextInputType.number,
                  inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                  decoration: const InputDecoration(
                      labelText: 'Purchase ID', hintText: 'Example: 15482', prefixIcon: Icon(Icons.receipt_long, size: 18)),
                  onSubmitted: (v) {
                    final id = int.tryParse(v);
                    if (id != null) _selectPurchase(id);
                  },
                ),
              ),
              const SizedBox(width: 8),
              FilledButton.tonal(
                  onPressed: () {
                    final id = int.tryParse(_purchaseIdCtl.text);
                    if (id != null) _selectPurchase(id);
                  },
                  child: const Text('Fetch (Enter)')),
              const SizedBox(width: 10),
              const Text('or double-click a row in the pending grid below', style: TextStyle(fontSize: 12)),
            ]),
            if (_tareError != null)
              Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(_tareError!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
            if (p != null) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                    color: Theme.of(context).colorScheme.primary.withOpacity(0.07),
                    borderRadius: BorderRadius.circular(10)),
                child: Wrap(spacing: 26, runSpacing: 8, children: [
                  _ro('Purchase ID', '${p['purchaseId']}'),
                  _ro('Grower', '${p['growerCode']} ${p['growerName']}'),
                  _ro('Father', '${p['fatherName']}'),
                  _ro('Village', '${p['villageName']}'),
                  _ro('Vehicle', '${p['vehicleNumber']} (${p['vehicleTypeName']})'),
                  _ro('Variety', '${p['varietyName']}'),
                  _ro('Rate', (p['rate'] as num).toStringAsFixed(2)),
                  _ro('Gross Weight', '${(p['grossWeightQuintal'] as num).toStringAsFixed(2)} Qtl'),
                  _ro('Gross Time', '${p['grossDateTime']}'.replaceFirst('T', ' ').split('.').first),
                  _ro('Gross Operator', '${p['grossByUserName']}'),
                  _ro('Cutting %', (p['cuttingPercent'] as num).toStringAsFixed(2)),
                  _ro('Tax %', (p['taxPercent'] as num).toStringAsFixed(2)),
                ]),
              ),
              const SizedBox(height: 16),
              FilledButton.icon(
                onPressed: _saving ? null : _saveTare,
                icon: const Icon(Icons.save_outlined),
                label: Text(_saving ? 'Saving...' : 'SAVE TARE'),
                style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(horizontal: 34, vertical: 18)),
              ),
            ],
          ]),
        ),
      ),
    );
  }

  Widget _ro(String label, String value) => Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
        Text(label, style: const TextStyle(fontSize: 10, color: Colors.grey)),
        Text(value, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 13)),
      ]);

  Widget _cameraPanel() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(8),
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          const Text('Live Cameras', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 12)),
          const SizedBox(height: 6),
          Expanded(
            flex: 3,
            child: ListView(children: [
              for (final c in _cameras)
                Container(
                  height: 110,
                  margin: const EdgeInsets.only(bottom: 6),
                  decoration: BoxDecoration(
                      color: Colors.black87, borderRadius: BorderRadius.circular(8)),
                  child: Stack(children: [
                    const Center(child: Icon(Icons.videocam_outlined, color: Colors.white38, size: 34)),
                    Positioned(
                        left: 6,
                        top: 4,
                        child: Text('Camera ${c['cameraNumber']} • ${c['vendor']}',
                            style: const TextStyle(color: Colors.white70, fontSize: 10))),
                    Positioned(
                        left: 6,
                        bottom: 4,
                        child: Text('${c['ipAddress']} (${c['protocol']})',
                            style: const TextStyle(color: Colors.white38, fontSize: 9))),
                  ]),
                ),
            ]),
          ),
          if (_capturedImages.isNotEmpty) ...[
            const Divider(height: 12),
            Row(children: [
              const Expanded(child: Text('Captured Evidence', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 12))),
              TextButton.icon(
                onPressed: () => _manualPrint(_lastPrintStage ?? 'GROSS'),
                icon: const Icon(Icons.print, size: 16),
                label: const Text('Reprint', style: TextStyle(fontSize: 12)),
              ),
            ]),
            const SizedBox(height: 4),
            Expanded(
              flex: 2,
              child: GridView.builder(
                gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, crossAxisSpacing: 4, mainAxisSpacing: 4),
                itemCount: _capturedImages.length,
                itemBuilder: (ctx, i) => _evidenceThumb(_capturedImages[i]),
              ),
            ),
          ],
        ]),
      ),
    );
  }

  Widget _evidenceThumb(Map img) {
    return FutureBuilder<List<int>?>(
      future: _fetchImageBytes(img['id']),
      builder: (ctx, snap) {
        return Container(
          decoration: BoxDecoration(borderRadius: BorderRadius.circular(6), color: Colors.black87),
          clipBehavior: Clip.antiAlias,
          child: Stack(fit: StackFit.expand, children: [
            if (snap.hasData && snap.data != null)
              Image.memory(Uint8List.fromList(snap.data!), fit: BoxFit.cover)
            else
              const Center(child: SizedBox(width: 14, height: 14, child: CircularProgressIndicator(strokeWidth: 2))),
            Positioned(
              left: 3,
              bottom: 2,
              child: Text('${img['captureStage']}', style: const TextStyle(color: Colors.white, fontSize: 9, fontWeight: FontWeight.w700)),
            ),
          ]),
        );
      },
    );
  }

  Future<List<int>?> _fetchImageBytes(int imageId) async {
    try {
      final res = await ApiClient.instance.dio
          .get('/api/images/$imageId/file', options: Options(responseType: ResponseType.bytes));
      return res.statusCode == 200 ? (res.data as List<int>) : null;
    } catch (_) {
      return null;
    }
  }

  Widget _pendingGrid() {
    return Card(
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
          child: Row(children: [
            Text(_grossMode ? 'Today\'s Gross (Pending Tare)' : 'Pending Gross — double-click to select for Tare',
                style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 13)),
            const Spacer(),
            Text('${_pending.length} pending', style: const TextStyle(fontSize: 12)),
            IconButton(onPressed: _loadPending, icon: const Icon(Icons.refresh, size: 18)),
          ]),
        ),
        Expanded(
          child: _pending.isEmpty
              ? const Center(child: Text('No pending gross transactions.', style: TextStyle(fontSize: 12)))
              : SingleChildScrollView(
                  scrollDirection: Axis.vertical,
                  child: SingleChildScrollView(
                    scrollDirection: Axis.horizontal,
                    child: DataTable(
                      showCheckboxColumn: false,
                      headingRowHeight: 34,
                      dataRowMinHeight: 30,
                      dataRowMaxHeight: 36,
                      columns: const [
                        DataColumn(label: Text('Purchase ID')),
                        DataColumn(label: Text('Grower Code')),
                        DataColumn(label: Text('Name')),
                        DataColumn(label: Text('Father Name')),
                        DataColumn(label: Text('Village')),
                        DataColumn(label: Text('Vehicle No')),
                        DataColumn(label: Text('Gross Qtl')),
                        DataColumn(label: Text('Gross Time')),
                        DataColumn(label: Text('By')),
                      ],
                      rows: [
                        for (final p in _pending)
                          DataRow(
                            onSelectChanged: !_grossMode
                                ? (_) => _selectPurchase(p['purchaseId'])
                                : null,
                            cells: [
                              DataCell(Text('${p['purchaseId']}', style: const TextStyle(fontWeight: FontWeight.w700))),
                              DataCell(Text('${p['growerCode']}')),
                              DataCell(Text('${p['growerName']}')),
                              DataCell(Text('${p['fatherName']}')),
                              DataCell(Text('${p['villageName']}')),
                              DataCell(Text('${p['vehicleNumber']}')),
                              DataCell(Text((p['grossWeightQuintal'] as num).toStringAsFixed(2))),
                              DataCell(Text('${p['grossDateTime']}'.replaceFirst('T', ' ').split('.').first)),
                              DataCell(Text('${p['grossByUserName']}')),
                            ],
                          ),
                      ],
                    ),
                  ),
                ),
        ),
      ]),
    );
  }
}
