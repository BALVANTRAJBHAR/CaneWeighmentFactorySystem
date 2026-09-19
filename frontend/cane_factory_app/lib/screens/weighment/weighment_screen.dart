import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:dio/dio.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/file_download.dart';
import '../../core/print_service.dart';
import '../../core/sound_controller.dart';
import '../../providers/auth_provider.dart';
import '../../providers/live_weight_provider.dart';
import '../../widgets/camera_live_preview_panel.dart';

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
  final _sound = SoundController.instance;
  // The desktop layout moves this panel between compact and wide branches when
  // the window is restored/maximized.  A stable GlobalKey preserves the live
  // MJPEG connection instead of disposing it and reconnecting on every resize.
  final _cameraPreviewKey = GlobalKey();

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
  Timer? _evidenceFlashTimer;
  late final LiveWeightProvider _liveWeightProvider;

  @override
  void initState() {
    super.initState();
    _liveWeightProvider = context.read<LiveWeightProvider>();
    _liveWeightProvider.addListener(_onLiveWeightChanged);
    unawaited(_liveWeightProvider.start());
    unawaited(_sound.loadConfig().then((_) => _onLiveWeightChanged()));
    _loadRefs();
    _loadPending();
  }

  @override
  void dispose() {
    _evidenceFlashTimer?.cancel();
    _liveWeightProvider.removeListener(_onLiveWeightChanged);
    _sound.dispose();
    super.dispose();
  }

  void _onLiveWeightChanged() {
    if (!mounted) return;
    unawaited(_sound.onWeight(_liveWeightProvider.current.weightQuintal));
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
          _cameras = (cams.data as List)
              .where((c) =>
                  c['cameraSystemEnabled'] == true &&
                  c['liveViewEnabled'] == true &&
                  c['status'] == true)
              .toList();
        }
      });
    } catch (_) {}
  }

  Future<void> _loadPending() async {
    try {
      final res =
          await ApiClient.instance.dio.get('/api/weighment/pending-tare');
      if (res.statusCode == 200 && mounted) setState(() => _pending = res.data);
    } catch (_) {}
  }

  /// Auto Print (Phase 7): server tells us whether to print and the ready-made document URL;
  /// failures never affect the already-completed save - just a clear toast + manual Reprint option.
  Future<void> _autoPrint(dynamic autoPrint) async {
    if (autoPrint == null) return;
    if (autoPrint['shouldAutoPrint'] != true) {
      final stage = autoPrint['stage']?.toString().toLowerCase() ?? 'weighment';
      await openPdfAfterSave(context, autoPrint['documentUrl'],
          'cane_$stage-${DateTime.now().millisecondsSinceEpoch}.pdf');
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

  Future<void> _loadVarieties(int typeId) async {
    final res =
        await ApiClient.instance.dio.get('/api/varieties/by-type/$typeId');
    if (res.statusCode == 200 && mounted) setState(() => _varieties = res.data);
  }

  /// The server saves evidence before it returns. This confirms that the persisted
  /// image metadata is available before the A4 PDF is requested for printing.
  Future<bool> _loadCapturedImages(int purchaseId, String stage) async {
    _lastCapturedPurchaseId = purchaseId;
    for (var attempt = 0; attempt < 24; attempt++) {
      if (attempt > 0) await Future.delayed(const Duration(milliseconds: 250));
      try {
        final res = await ApiClient.instance.dio
            .get('/api/purchases/$purchaseId/images');
        if (res.statusCode == 200 && res.data is List) {
          final images = (res.data as List).toList();
          final stageImages =
              images.where((image) => image['captureStage'] == stage).toList();
          if (stageImages.isNotEmpty) {
            _flashCapturedEvidence(stageImages, purchaseId);
            await _sound.playConfiguredEvent('IMAGE_CAPTURED');
            return true;
          }
        }
      } catch (_) {}
    }
    return false;
  }

  /// Evidence is persisted and printed by the server. In the live weighment
  /// screen it is only a one-time visual confirmation, not a permanent panel
  /// that repeatedly refetches/rebuilds thumbnails on every camera frame.
  void _flashCapturedEvidence(List images, int purchaseId) {
    _evidenceFlashTimer?.cancel();
    if (!mounted || _lastCapturedPurchaseId != purchaseId) return;
    setState(() => _capturedImages = images);
    _evidenceFlashTimer = Timer(const Duration(milliseconds: 1200), () {
      if (mounted && _lastCapturedPurchaseId == purchaseId) {
        setState(() => _capturedImages = []);
      }
    });
  }

  String? _captureFailure(dynamic response) {
    final capture = response is Map ? response['capture'] : null;
    if (capture is! Map || (capture['saved'] as num? ?? 0) > 0) return null;
    final results = capture['results'];
    if (results is List) {
      final errors = results
          .map((result) => result is Map ? result['error']?.toString() : null)
          .whereType<String>()
          .where((error) => error.trim().isNotEmpty)
          .toList();
      if (errors.isNotEmpty) return errors.join(' | ');
    }
    return 'No enabled camera was available for evidence capture.';
  }

  bool _hasConnectedCameraEvidence(dynamic response) {
    final capture = response is Map ? response['capture'] : null;
    if (capture is! Map) return false;
    final saved = (capture['saved'] as num?)?.toInt() ?? 0;
    return saved > 0;
  }

  Future<void> _lookupGrower() async {
    setState(() {
      _grower = null;
      _growerError = null;
    });
    final res = await ApiClient.instance.dio.get('/api/growers/by-code',
        queryParameters: {'code': _growerCode.text.trim()});
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
    final res = await ApiClient.instance.dio
        .get('/api/weighment/purchase/$id/for-tare');
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
        backgroundColor: error
            ? Theme.of(context).colorScheme.error
            : const Color(0xFF2E7D32)));
  }

  Future<void> _saveGross() async {
    final live = context.read<LiveWeightProvider>().current;
    if (_grower == null)
      return _toast('Lookup a valid Grower Code first (press ENTER).',
          error: true);
    if (_vehicleTypeId == null)
      return _toast('Select Vehicle Type.', error: true);
    if (_vehicleNumber.text.trim().length < 4)
      return _toast('Enter a valid Vehicle Number. Example: UP32AB1234',
          error: true);
    if (_varietyTypeId == null || _varietyId == null)
      return _toast('Select Variety Type and Variety.', error: true);
    setState(() => _saving = true);
    try {
      final res =
          await ApiClient.instance.dio.post('/api/weighment/gross', data: {
        'growerCode': _grower!['growerCode'],
        'vehicleTypeId': _vehicleTypeId,
        'vehicleNumber': _vehicleNumber.text.trim().toUpperCase(),
        'varietyTypeId': _varietyTypeId,
        'varietyId': _varietyId,
        'cuttingPercent': double.tryParse(_cutting.text) ?? 0,
        'taxPercent': double.tryParse(_tax.text) ?? 0,
        'scaleReadingKg': live.weightKg,
        'idempotencyKey':
            'gross-${_grower!['growerCode']}-${DateTime.now().microsecondsSinceEpoch}',
      });
      if (!mounted) return;
      if (res.statusCode == 200) {
        _toast(res.data['message']);
        await _sound.onWeighmentSaved();
        final purchaseId = res.data['purchaseId'] as int;
        // reset new-entry fields; keep configuration selections for fast operation
        setState(() {
          _grower = null;
          _growerCode.clear();
          _vehicleNumber.clear();
          _capturedImages = [];
        });
        _loadPending();
        final captured = await _loadCapturedImages(purchaseId, 'GROSS');
        if (!captured || !_hasConnectedCameraEvidence(res.data)) {
          _toast(
              'Gross saved, but camera evidence was not captured: ${_captureFailure(res.data) ?? 'Check Camera Configuration.'}',
              error: true);
        } else {
          await _autoPrint(res.data['autoPrint']);
        }
      } else {
        _toast(ApiClient.errorMessage(res), error: true);
      }
    } catch (_) {
      if (mounted)
        _toast(
            'Could not save Gross. Please check the server connection and try again.',
            error: true);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _saveTare() async {
    final live = context.read<LiveWeightProvider>().current;
    if (_selectedPurchase == null)
      return _toast(
          'Select a pending purchase (double-click a row or enter Purchase ID).',
          error: true);
    setState(() => _saving = true);
    try {
      final res =
          await ApiClient.instance.dio.post('/api/weighment/tare', data: {
        'purchaseId': _selectedPurchase!['purchaseId'],
        'scaleReadingKg': live.weightKg,
        // A failed validation must be retryable; each distinct user click gets a new request key.
        'idempotencyKey':
            'tare-${_selectedPurchase!['purchaseId']}-${DateTime.now().microsecondsSinceEpoch}',
      });
      if (!mounted) return;
      if (res.statusCode == 200) {
        _toast('${res.data['message']} Purchase ID: ${res.data['purchaseId']}');
        await _sound.onWeighmentSaved();
        final purchaseId = _selectedPurchase!['purchaseId'] as int;
        setState(() {
          _selectedPurchase = null;
          _purchaseIdCtl.clear();
          _capturedImages = [];
        });
        _loadPending();
        final captured = await _loadCapturedImages(purchaseId, 'TARE');
        if (!captured || !_hasConnectedCameraEvidence(res.data)) {
          _toast(
              'Tare saved, but camera evidence was not captured: ${_captureFailure(res.data) ?? 'Check Camera Configuration.'}',
              error: true);
        } else {
          await _autoPrint(res.data['autoPrint']);
        }
      } else {
        _toast(ApiClient.errorMessage(res), error: true);
      }
    } catch (_) {
      if (mounted)
        _toast(
            'Could not save Tare. Please check the server connection and try again.',
            error: true);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final live = context.watch<LiveWeightProvider>().current;
    final auth = context.watch<AuthProvider>();
    final scheme = Theme.of(context).colorScheme;

    return SafeArea(
      child: LayoutBuilder(builder: (context, constraints) {
        final compactHeader = constraints.maxWidth < 1100;
        final showCameraBesideForm = constraints.maxWidth >= 1200;
        final gridHeight = constraints.maxHeight < 700 ? 170.0 : 210.0;
        return Padding(
          padding: const EdgeInsets.all(10),
          child: Column(children: [
            // ---------- TOP: mode radio + live weight + status ----------
            Card(
              child: Padding(
                padding:
                    const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
                child: compactHeader
                    ? Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                            _modeSelector(scheme),
                            const SizedBox(height: 8),
                            _liveWeightDisplay(live),
                            const SizedBox(height: 8),
                            _deviceStatus(live, auth),
                          ])
                    : Row(children: [
                        _modeSelector(scheme),
                        const SizedBox(width: 20),
                        // LIVE WEIGHT display
                        Expanded(
                          child: _liveWeightDisplay(live),
                        ),
                        const SizedBox(width: 14),
                        _deviceStatus(live, auth),
                      ]),
              ),
            ),
            const SizedBox(height: 6),
            // ---------- CENTER: entry panel + cameras ----------
            Expanded(
              child: _cameras.isEmpty
                  ? (_grossMode ? _grossPanel() : _tarePanel())
                  : showCameraBesideForm
                      ? Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                              Expanded(
                                  flex: 3,
                                  child: _grossMode
                                      ? _grossPanel()
                                      : _tarePanel()),
                              const SizedBox(width: 6),
                              SizedBox(
                                  width: 480,
                                  height: 370,
                                  child: _cameraPanel()),
                            ])
                      : Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                              // In a restored/non-maximized window reserve
                              // three parts for the form and one part for the
                              // cameras. This keeps the camera area at 25% of
                              // the available width instead of full width.
                              Expanded(
                                  flex: 3,
                                  child: _grossMode
                                      ? _grossPanel()
                                      : _tarePanel()),
                              const SizedBox(width: 6),
                              Expanded(
                                  flex: 1,
                                  child: SizedBox(
                                      height: 320, child: _cameraPanel())),
                            ]),
            ),
            const SizedBox(height: 6),
            // ---------- BOTTOM: pending grid ----------
            SizedBox(height: gridHeight, child: _pendingGrid()),
          ]),
        );
      }),
    );
  }

  Widget _modeSelector(ColorScheme scheme) => RadioGroup<bool>(
        groupValue: _grossMode,
        onChanged: (v) {
          _sound.reset();
          setState(() => _grossMode = v!);
          _loadPending();
        },
        child: Wrap(children: [
          for (final m in [(true, 'GROSS'), (false, 'TARE')])
            Row(mainAxisSize: MainAxisSize.min, children: [
              Radio<bool>(value: m.$1),
              Text(m.$2,
                  style: TextStyle(
                      fontWeight: FontWeight.w800,
                      fontSize: 16,
                      color: _grossMode == m.$1 ? scheme.primary : null)),
              const SizedBox(width: 10),
            ]),
        ]),
      );

  Widget _liveWeightDisplay(dynamic live) => Container(
        padding: const EdgeInsets.symmetric(vertical: 6, horizontal: 18),
        decoration: BoxDecoration(
          color: const Color(0xFF0D1B0F),
          borderRadius: BorderRadius.circular(10),
          border: Border.all(
              color: live.stable ? const Color(0xFF2E7D32) : Colors.orange,
              width: 2),
        ),
        child: Wrap(
            alignment: WrapAlignment.center,
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 8,
            children: [
              Text(live.isLive ? live.weightQuintal.toStringAsFixed(2) : '--',
                  style: const TextStyle(
                      fontFamily: 'monospace',
                      fontSize: 34,
                      fontWeight: FontWeight.w900,
                      color: Color(0xFF7CFC8F))),
              const Text('Qtl',
                  style: TextStyle(color: Color(0xFF7CFC8F), fontSize: 16)),
              Text(
                  live.isLive
                      ? '(${live.weightKg.toStringAsFixed(0)} KG)'
                      : 'Reading Stopped',
                  style: const TextStyle(color: Colors.white54, fontSize: 13)),
            ]),
      );

  Widget _deviceStatus(dynamic live, AuthProvider auth) => Wrap(
        spacing: 12,
        runSpacing: 4,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Row(mainAxisSize: MainAxisSize.min, children: [
                  Icon(live.deviceConnected ? Icons.usb : Icons.usb_off,
                      size: 15,
                      color: live.deviceConnected
                          ? const Color(0xFF2E7D32)
                          : Colors.red),
                  const SizedBox(width: 4),
                  Text(
                      live.deviceConnected
                          ? 'Digitizer: ${live.deviceName ?? "Connected"}'
                          : 'Digitizer: Disconnected',
                      style: const TextStyle(fontSize: 11)),
                ]),
                Text(
                    live.isLive
                        ? (live.stable ? 'STABLE' : 'UNSTABLE')
                        : live.readerState.replaceAll('_', ' '),
                    style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w700,
                        color: live.stable && live.isLive
                            ? const Color(0xFF2E7D32)
                            : Colors.orange)),
                Text(
                    live.lastReceivedAt == null
                        ? 'No data yet'
                        : 'Last: ${DateFormat('HH:mm:ss').format(live.lastReceivedAt!.toLocal())}',
                    style: const TextStyle(fontSize: 10)),
              ]),
          Chip(
              avatar: const Icon(Icons.person, size: 14),
              visualDensity: VisualDensity.compact,
              label: Text('${auth.user?['username']} • ${auth.roles.join(",")}',
                  style: const TextStyle(fontSize: 11))),
        ],
      );

  Widget _grossPanel() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: SingleChildScrollView(
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('GROSS ENTRY',
                style: TextStyle(
                    fontWeight: FontWeight.w800,
                    color: Theme.of(context).colorScheme.primary)),
            const SizedBox(height: 10),
            Row(children: [
              SizedBox(
                width: 200,
                child: TextField(
                  controller: _growerCode,
                  autofocus: true,
                  decoration: const InputDecoration(
                      labelText: 'Grower Code',
                      hintText: 'Example: 101/1',
                      prefixIcon: Icon(Icons.badge_outlined, size: 18)),
                  onSubmitted: (_) => _lookupGrower(),
                ),
              ),
              const SizedBox(width: 8),
              FilledButton.tonal(
                  onPressed: _lookupGrower,
                  child: const Text('Lookup (Enter)')),
              const SizedBox(width: 14),
              if (_grower != null)
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                        color: const Color(0xFF2E7D32).withValues(alpha: 0.10),
                        borderRadius: BorderRadius.circular(8)),
                    child: Text(
                      '${_grower!['growerName']}  S/o ${_grower!['fatherName']}  •  Village: ${_grower!['villageName']}  •  '
                      'Bank: ${_grower!['bankName'] ?? '-'} ${_grower!['accountMasked'] ?? ''}',
                      style: const TextStyle(fontWeight: FontWeight.w600),
                    ),
                  ),
                ),
              if (_growerError != null)
                Expanded(
                    child: Text(_growerError!,
                        style: TextStyle(
                            color: Theme.of(context).colorScheme.error))),
            ]),
            const SizedBox(height: 12),
            Wrap(spacing: 12, runSpacing: 12, children: [
              SizedBox(
                width: 190,
                child: DropdownButtonFormField<int>(
                  value: _vehicleTypeId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                      labelText: 'Vehicle Type',
                      hintText: 'Select Vehicle Type'),
                  items: [
                    for (final v in _vehicleTypes)
                      DropdownMenuItem(
                          value: v['id'] as int,
                          child: Text(v['vehicleTypeName'],
                              overflow: TextOverflow.ellipsis))
                  ],
                  onChanged: (v) => setState(() => _vehicleTypeId = v),
                ),
              ),
              SizedBox(
                width: 190,
                child: TextField(
                  controller: _vehicleNumber,
                  textCapitalization: TextCapitalization.characters,
                  decoration: const InputDecoration(
                      labelText: 'Vehicle Number',
                      hintText: 'Example: UP32AB1234'),
                ),
              ),
              SizedBox(
                width: 190,
                child: DropdownButtonFormField<int>(
                  value: _varietyTypeId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                      labelText: 'Variety Type',
                      hintText: 'Select Variety Type'),
                  items: [
                    for (final v in _varietyTypes)
                      DropdownMenuItem(
                          value: v['id'] as int,
                          child: Text(v['varietyTypeName'],
                              overflow: TextOverflow.ellipsis))
                  ],
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
                  isExpanded: true,
                  decoration: const InputDecoration(
                      labelText: 'Variety', hintText: 'Select Variety'),
                  items: [
                    for (final v in _varieties)
                      DropdownMenuItem(
                          value: v['id'] as int,
                          child: Text(v['varietyName'],
                              overflow: TextOverflow.ellipsis))
                  ],
                  onChanged: (v) => setState(() => _varietyId = v),
                ),
              ),
              SizedBox(
                width: 252,
                child: Row(children: [
                  Expanded(
                    child: TextField(
                      controller: _tax,
                      keyboardType:
                          const TextInputType.numberWithOptions(decimal: true),
                      inputFormatters: [
                        FilteringTextInputFormatter.allow(
                            RegExp(r'^\d*\.?\d{0,2}'))
                      ],
                      decoration: const InputDecoration(
                          labelText: 'Tax %', hintText: '0.00'),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: TextField(
                      controller: _cutting,
                      keyboardType:
                          const TextInputType.numberWithOptions(decimal: true),
                      inputFormatters: [
                        FilteringTextInputFormatter.allow(
                            RegExp(r'^\d*\.?\d{0,2}'))
                      ],
                      decoration: const InputDecoration(
                          labelText: 'Cutting %', hintText: '2.00'),
                    ),
                  ),
                ]),
              ),
            ]),
            const SizedBox(height: 16),
            FilledButton.icon(
              onPressed: _saving ? null : _saveGross,
              icon: const Icon(Icons.save_outlined),
              label: Text(_saving ? 'Saving...' : 'SAVE GROSS'),
              style: FilledButton.styleFrom(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 34, vertical: 18)),
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
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('TARE ENTRY',
                style: TextStyle(
                    fontWeight: FontWeight.w800,
                    color: Theme.of(context).colorScheme.primary)),
            const SizedBox(height: 10),
            Row(children: [
              SizedBox(
                width: 210,
                child: TextField(
                  controller: _purchaseIdCtl,
                  keyboardType: TextInputType.number,
                  inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                  decoration: const InputDecoration(
                      labelText: 'Purchase ID',
                      hintText: 'Example: 15482',
                      prefixIcon: Icon(Icons.receipt_long, size: 18)),
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
              const Text('or double-click a row in the pending grid below',
                  style: TextStyle(fontSize: 12)),
            ]),
            if (_tareError != null)
              Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(_tareError!,
                      style: TextStyle(
                          color: Theme.of(context).colorScheme.error))),
            if (p != null) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                    color: Theme.of(context)
                        .colorScheme
                        .primary
                        .withValues(alpha: 0.07),
                    borderRadius: BorderRadius.circular(10)),
                child: Wrap(spacing: 26, runSpacing: 8, children: [
                  _ro('Purchase ID', '${p['purchaseId']}'),
                  _ro('Grower', '${p['growerCode']} ${p['growerName']}'),
                  _ro('Father', '${p['fatherName']}'),
                  _ro('Village', '${p['villageName']}'),
                  _ro('Vehicle',
                      '${p['vehicleNumber']} (${p['vehicleTypeName']})'),
                  _ro('Variety', '${p['varietyName']}'),
                  _ro('Rate', (p['rate'] as num).toStringAsFixed(2)),
                  _ro('Gross Weight',
                      '${(p['grossWeightQuintal'] as num).toStringAsFixed(2)} Qtl'),
                  _ro(
                      'Gross Time',
                      '${p['grossDateTime']}'
                          .replaceFirst('T', ' ')
                          .split('.')
                          .first),
                  _ro('Gross Operator', '${p['grossByUserName']}'),
                  _ro('Cutting %',
                      (p['cuttingPercent'] as num).toStringAsFixed(2)),
                  _ro('Tax %', (p['taxPercent'] as num).toStringAsFixed(2)),
                ]),
              ),
              const SizedBox(height: 16),
              FilledButton.icon(
                onPressed: _saving ? null : _saveTare,
                icon: const Icon(Icons.save_outlined),
                label: Text(_saving ? 'Saving...' : 'SAVE TARE'),
                style: FilledButton.styleFrom(
                    padding: const EdgeInsets.symmetric(
                        horizontal: 34, vertical: 18)),
              ),
            ],
          ]),
        ),
      ),
    );
  }

  Widget _ro(String label, String value) => Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(label,
                style: const TextStyle(fontSize: 10, color: Colors.grey)),
            Text(value,
                style:
                    const TextStyle(fontWeight: FontWeight.w700, fontSize: 13)),
          ]);

  Widget _cameraPanel() {
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Expanded(
          flex: _capturedImages.isEmpty ? 1 : 3,
          child: CameraLivePreviewPanel(
              key: _cameraPreviewKey,
              cameras: _cameras, compact: true, squareCards: true)),
      if (_capturedImages.isNotEmpty) ...[
        const Divider(height: 12),
        const Padding(
          padding: EdgeInsets.only(bottom: 4),
          child: Text('Evidence captured',
              style: TextStyle(fontWeight: FontWeight.w700, fontSize: 12)),
        ),
        Expanded(
          flex: 2,
          child: GridView.builder(
            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                crossAxisCount: 2, crossAxisSpacing: 4, mainAxisSpacing: 4),
            itemCount: _capturedImages.length,
            itemBuilder: (ctx, i) => _evidenceThumb(_capturedImages[i]),
          ),
        ),
      ],
    ]);
  }

  Widget _evidenceThumb(Map img) {
    return FutureBuilder<List<int>?>(
      future: _fetchImageBytes(img['id']),
      builder: (ctx, snap) {
        return Container(
          decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(6), color: Colors.black87),
          clipBehavior: Clip.antiAlias,
          child: Stack(fit: StackFit.expand, children: [
            if (snap.hasData && snap.data != null)
              Image.memory(Uint8List.fromList(snap.data!), fit: BoxFit.cover)
            else
              const Center(
                  child: SizedBox(
                      width: 14,
                      height: 14,
                      child: CircularProgressIndicator(strokeWidth: 2))),
            Positioned(
              left: 3,
              bottom: 2,
              child: Text('${img['captureStage']}',
                  style: const TextStyle(
                      color: Colors.white,
                      fontSize: 9,
                      fontWeight: FontWeight.w700)),
            ),
          ]),
        );
      },
    );
  }

  Future<List<int>?> _fetchImageBytes(int imageId) async {
    try {
      final res = await ApiClient.instance.dio.get('/api/images/$imageId/file',
          options: Options(responseType: ResponseType.bytes));
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
            Text(
                _grossMode
                    ? 'Today\'s Gross (Pending Tare)'
                    : 'Pending Gross — double-click to select for Tare',
                style:
                    const TextStyle(fontWeight: FontWeight.w700, fontSize: 13)),
            const Spacer(),
            Text('${_pending.length} pending',
                style: const TextStyle(fontSize: 12)),
            IconButton(
                onPressed: _loadPending,
                icon: const Icon(Icons.refresh, size: 18)),
          ]),
        ),
        Expanded(
          child: _pending.isEmpty
              ? const Center(
                  child: Text('No pending gross transactions.',
                      style: TextStyle(fontSize: 12)))
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
                              DataCell(Text('${p['purchaseId']}',
                                  style: const TextStyle(
                                      fontWeight: FontWeight.w700))),
                              DataCell(Text('${p['growerCode']}')),
                              DataCell(Text('${p['growerName']}')),
                              DataCell(Text('${p['fatherName']}')),
                              DataCell(Text('${p['villageName']}')),
                              DataCell(Text('${p['vehicleNumber']}')),
                              DataCell(Text((p['grossWeightQuintal'] as num)
                                  .toStringAsFixed(2))),
                              DataCell(Text('${p['grossDateTime']}'
                                  .replaceFirst('T', ' ')
                                  .split('.')
                                  .first)),
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
