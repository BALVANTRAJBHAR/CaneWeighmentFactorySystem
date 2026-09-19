import 'dart:async';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/file_download.dart';
import '../../core/print_service.dart';
import '../../core/sound_controller.dart';
import '../../providers/live_weight_provider.dart';
import '../../widgets/camera_live_preview_panel.dart';

/// Separate non-cane SalePurchase workflow. It never uses the Payment screen/API.
class SalePurchaseWeighmentScreen extends StatefulWidget {
  const SalePurchaseWeighmentScreen({super.key});
  @override
  State<SalePurchaseWeighmentScreen> createState() =>
      _SalePurchaseWeighmentScreenState();
}

class _SalePurchaseWeighmentScreenState
    extends State<SalePurchaseWeighmentScreen> {
  final _sound = SoundController.instance;
  final _vehicle = TextEditingController();
  final _driver = TextEditingController();
  final _remark = TextEditingController();
  final _id = TextEditingController();
  final _rate = TextEditingController();
  List _items = [], _parties = [], _vehicles = [], _pending = [], _cameras = [];
  int? _itemId, _partyId, _vehicleTypeId;
  Map<String, dynamic>? _selected;
  bool _gross = false, _saving = false;
  late final LiveWeightProvider _liveWeightProvider;

  @override
  void initState() {
    super.initState();
    _liveWeightProvider = context.read<LiveWeightProvider>();
    _liveWeightProvider.addListener(_onLiveWeightChanged);
    unawaited(_liveWeightProvider.start());
    unawaited(_sound.loadConfig().then((_) => _onLiveWeightChanged()));
    _load();
  }

  @override
  void dispose() {
    _liveWeightProvider.removeListener(_onLiveWeightChanged);
    _sound.dispose();
    _vehicle.dispose();
    _driver.dispose();
    _remark.dispose();
    _id.dispose();
    _rate.dispose();
    super.dispose();
  }

  void _onLiveWeightChanged() {
    if (!mounted) return;
    unawaited(_sound.onWeight(_liveWeightProvider.current.weightQuintal));
  }

  Future<void> _load() async {
    try {
      final results = await Future.wait([
        ApiClient.instance.dio.get('/api/items'),
        ApiClient.instance.dio.get('/api/parties'),
        ApiClient.instance.dio.get('/api/vehicle-types'),
        ApiClient.instance.dio.get('/api/sale-purchase-weighment/pending'),
        ApiClient.instance.dio.get('/api/config/cameras')
      ]);
      if (!mounted) return;
      setState(() {
        _items = results[0].data['items'] ?? [];
        _parties = results[1].data['items'] ?? [];
        _vehicles = results[2].data['items'] ?? [];
        _pending = results[3].data ?? [];
        if (results[4].statusCode == 200 && results[4].data is List) {
          _cameras = (results[4].data as List)
              .where((c) =>
                  c['cameraSystemEnabled'] == true &&
                  c['liveViewEnabled'] == true &&
                  c['status'] == true)
              .toList();
        }
      });
    } catch (_) {
      _toast('Could not load SalePurchase reference data.', error: true);
    }
  }

  void _toast(String message, {bool error = false}) =>
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(message),
          backgroundColor: error
              ? Theme.of(context).colorScheme.error
              : const Color(0xff2e7d32)));

  Future<void> _select(int id) async {
    final res =
        await ApiClient.instance.dio.get('/api/sale-purchase-weighment/$id');
    if (!mounted) return;
    if (res.statusCode != 200)
      return _toast(ApiClient.errorMessage(res), error: true);
    final data = Map<String, dynamic>.from(res.data);
    setState(() {
      _selected = data;
      _gross = true;
      _id.text = '$id';
      _itemId = data['itemId'];
      _partyId = data['partyId'];
      _vehicleTypeId = data['vehicleTypeId'];
      _vehicle.text = data['vehicleNumber'] ?? '';
      _driver.text = data['driverName'] ?? '';
      _remark.text = data['remark'] ?? '';
      _rate.text = data['rate']?.toString() ?? '';
    });
  }

  Future<void> _print(dynamic print) async {
    if (print == null) return;
    if (print['shouldAutoPrint'] != true) {
      final stage = print['stage']?.toString().toLowerCase() ?? 'weighment';
      await openPdfAfterSave(context, print['documentUrl'],
          'sale-purchase_$stage-${DateTime.now().millisecondsSinceEpoch}.pdf');
      return;
    }
    final outcome = await PrintService.printDocument(
        documentUrl: print['documentUrl'],
        printerType: print['printerType'] ?? 'DotMatrix',
        printerName: print['printerName'] ?? '',
        copies: print['copies'] ?? 2);
    if (mounted && !outcome.success) _toast(outcome.message, error: true);
  }

  Future<bool> _waitForCapturedImages(int salePurchaseId, String stage) async {
    for (var attempt = 0; attempt < 24; attempt++) {
      if (attempt > 0) await Future.delayed(const Duration(milliseconds: 250));
      try {
        final res = await ApiClient.instance.dio
            .get('/api/sale-purchase-weighment/$salePurchaseId/images');
        if (res.statusCode == 200 && res.data is List) {
          final hasStage =
              (res.data as List).any((image) => image['captureStage'] == stage);
          if (hasStage) {
            await _sound.playConfiguredEvent('IMAGE_CAPTURED');
            return true;
          }
        }
      } catch (_) {}
    }
    return false;
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

  bool _validLive() {
    final l = context.read<LiveWeightProvider>().current;
    if (!l.deviceConnected || !l.readerRunning || !l.isLive) {
      _toast(
          'Device is disconnected, reading is stopped, or live weight is stale.',
          error: true);
      return false;
    }
    return true;
  }

  Future<void> _saveTare() async {
    if (!_validLive()) return;
    if (_itemId == null ||
        _partyId == null ||
        _vehicleTypeId == null ||
        _vehicle.text.trim().isEmpty ||
        _driver.text.trim().isEmpty) {
      return _toast(
          'Item, Party, Vehicle Type, Vehicle Number and Driver Name are required.',
          error: true);
    }
    setState(() => _saving = true);
    try {
      final res = await ApiClient.instance.dio
          .post('/api/sale-purchase-weighment/tare', data: {
        'itemId': _itemId,
        'partyId': _partyId,
        'vehicleTypeId': _vehicleTypeId,
        'vehicleNumber': _vehicle.text.trim(),
        'driverName': _driver.text.trim(),
        'remark': _remark.text.trim(),
        'idempotencyKey': 'sp-tare-${DateTime.now().microsecondsSinceEpoch}'
      });
      if (!mounted) return;
      if (res.statusCode != 200)
        return _toast(ApiClient.errorMessage(res), error: true);
      _toast(res.data['message']);
      await _sound.onWeighmentSaved();
      final salePurchaseId = res.data['salePurchaseId'] as int;
      final captured = await _waitForCapturedImages(salePurchaseId, 'TARE');
      if (!captured || !_hasConnectedCameraEvidence(res.data)) {
        _toast(
            'Tare saved, but camera evidence was not captured: ${_captureFailure(res.data) ?? 'Check Camera Configuration.'}',
            error: true);
      } else {
        await _print(res.data['autoPrint']);
      }
      setState(() {
        _itemId = null;
        _partyId = null;
        _vehicleTypeId = null;
        _vehicle.clear();
        _driver.clear();
        _remark.clear();
      });
      _load();
    } catch (e) {
      if (mounted) _toast(ApiClient.exceptionMessage(e), error: true);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _saveGross() async {
    if (!_validLive()) return;
    if (_selected == null)
      return _toast(
          'Double-click a pending row or enter a SalePurchase ID and press ENTER.',
          error: true);
    final rate =
        _rate.text.trim().isEmpty ? null : double.tryParse(_rate.text.trim());
    if (_rate.text.trim().isNotEmpty && rate == null)
      return _toast('Rate must be a valid number.', error: true);
    setState(() => _saving = true);
    try {
      final res = await ApiClient.instance.dio
          .post('/api/sale-purchase-weighment/gross', data: {
        'salePurchaseId': _selected!['salePurchaseId'],
        'rate': rate,
        'idempotencyKey':
            'sp-gross-${_selected!['salePurchaseId']}-${DateTime.now().microsecondsSinceEpoch}'
      });
      if (!mounted) return;
      if (res.statusCode != 200)
        return _toast(ApiClient.errorMessage(res), error: true);
      _toast(res.data['message']);
      await _sound.onWeighmentSaved();
      final salePurchaseId = res.data['salePurchaseId'] as int;
      final captured = await _waitForCapturedImages(salePurchaseId, 'GROSS');
      if (!captured || !_hasConnectedCameraEvidence(res.data)) {
        _toast(
            'Gross saved, but camera evidence was not captured: ${_captureFailure(res.data) ?? 'Check Camera Configuration.'}',
            error: true);
      } else {
        await _print(res.data['autoPrint']);
      }
      setState(() {
        _selected = null;
        _id.clear();
        _rate.clear();
        _gross = false;
      });
      _load();
    } catch (e) {
      if (mounted) _toast(ApiClient.exceptionMessage(e), error: true);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Widget _dropdown(String label, int? value, List data,
          ValueChanged<int?> onChanged, String name) =>
      SizedBox(
          width: 230,
          child: DropdownButtonFormField<int>(
              isExpanded: true,
              value: value,
              decoration: InputDecoration(labelText: label),
              items: [
                for (final x in data)
                  DropdownMenuItem(
                      value: x['id'] as int,
                      child:
                          Text('${x[name]}', overflow: TextOverflow.ellipsis))
              ],
              onChanged: onChanged));

  @override
  Widget build(BuildContext context) {
    final live = context.watch<LiveWeightProvider>().current;
    final finalWeight = _selected == null
        ? null
        : live.weightQuintal -
            ((_selected!['tareWeightQuintal'] ?? 0) as num).toDouble();
    return SafeArea(
        child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(children: [
              _weighmentHeader(live),
              const SizedBox(height: 8),
              Expanded(
                  child: SingleChildScrollView(
                      child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                    Expanded(
                        flex: 3,
                        child: Card(
                            child: Padding(
                                padding: const EdgeInsets.all(16),
                                child: _gross
                                    ? _grossForm(finalWeight)
                                    : _tareForm()))),
                    if (_cameras.isNotEmpty) ...[
                      const SizedBox(width: 8),
                      Expanded(
                          flex: 1,
                          child: SizedBox(
                              height: 235,
                              child:
                                  CameraLivePreviewPanel(cameras: _cameras))),
                    ],
                  ]))),
              const SizedBox(height: 8),
              SizedBox(height: 210, child: Card(child: _pendingGrid()))
            ])));
  }

  /// Uses the same three-part hierarchy as Cane Weighment: mode on the left,
  /// weight centred in its own box, and digitizer health in a separate box.
  /// This deliberately keeps status details out of the live-weight display.
  Widget _weighmentHeader(dynamic live) => LayoutBuilder(
        builder: (context, constraints) {
          final compact = constraints.maxWidth < 1000;
          final modeBox = Card(
            child: Padding(
              padding: const EdgeInsets.all(12),
              child: SegmentedButton<bool>(
                segments: const [
                  ButtonSegment(value: false, label: Text('TARE FIRST')),
                  ButtonSegment(value: true, label: Text('GROSS')),
                ],
                selected: {_gross},
                onSelectionChanged: (v) => setState(() => _gross = v.first),
              ),
            ),
          );

          final weightBox = Card(
            child: Container(
              constraints: const BoxConstraints(minHeight: 72),
              alignment: Alignment.center,
              padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 8),
              child: Text(
                '${live.weightQuintal.toStringAsFixed(2)} Qtl',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                    fontWeight: FontWeight.bold,
                    color: live.isLive ? Colors.green : Colors.red),
              ),
            ),
          );

          final statusBox = Card(
            child: SizedBox(
              width: compact ? double.infinity : 270,
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 9),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      live.deviceConnected
                          ? 'Digitizer: ${live.deviceName ?? 'Main Weighbridge Indicator'}'
                          : 'Digitizer: Disconnected',
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontSize: 11),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      live.deviceConnected ? 'CONNECTED' : 'DISCONNECTED',
                      style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w800,
                        color: live.deviceConnected
                            ? const Color(0xFF2E7D32)
                            : Colors.red,
                      ),
                    ),
                    Text(
                      '${live.readerState.replaceAll('_', ' ')} • ${live.stable ? 'STABLE' : 'UNSTABLE'}',
                      style: const TextStyle(fontSize: 11),
                    ),
                    Text(
                      live.lastReceivedAt == null
                          ? 'Last: No data yet'
                          : 'Last: ${DateFormat('HH:mm:ss').format(live.lastReceivedAt!.toLocal())}',
                      style: const TextStyle(fontSize: 10),
                    ),
                  ],
                ),
              ),
            ),
          );

          if (compact) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [modeBox, weightBox, statusBox],
            );
          }
          return Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              modeBox,
              const SizedBox(width: 8),
              Expanded(child: weightBox),
              const SizedBox(width: 8),
              statusBox,
            ],
          );
        },
      );

  Widget _tareForm() => Wrap(
          spacing: 14,
          runSpacing: 14,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            _dropdown('Item', _itemId, _items,
                (v) => setState(() => _itemId = v), 'itemName'),
            _dropdown('Party', _partyId, _parties,
                (v) => setState(() => _partyId = v), 'partyName'),
            _dropdown('Vehicle Type', _vehicleTypeId, _vehicles,
                (v) => setState(() => _vehicleTypeId = v), 'vehicleTypeName'),
            SizedBox(
                width: 200,
                child: TextField(
                    controller: _vehicle,
                    textCapitalization: TextCapitalization.characters,
                    decoration:
                        const InputDecoration(labelText: 'Vehicle Number'))),
            SizedBox(
                width: 200,
                child: TextField(
                    controller: _driver,
                    decoration:
                        const InputDecoration(labelText: 'Driver Name'))),
            SizedBox(
                width: 260,
                child: TextField(
                    controller: _remark,
                    decoration:
                        const InputDecoration(labelText: 'Remark (optional)'))),
            FilledButton.icon(
                onPressed: _saving ? null : _saveTare,
                icon: const Icon(Icons.save),
                label: const Text('Save Tare'))
          ]);

  Widget _grossForm(double? finalWeight) =>
      Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Wrap(
            spacing: 12,
            runSpacing: 12,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              SizedBox(
                  width: 230,
                  child: TextField(
                      controller: _id,
                      keyboardType: TextInputType.number,
                      onSubmitted: (v) {
                        final id = int.tryParse(v);
                        if (id != null) _select(id);
                      },
                      decoration: const InputDecoration(
                          labelText: 'SalePurchase ID',
                          hintText: 'Enter ID + ENTER'))),
              OutlinedButton.icon(
                  onPressed: () {
                    final id = int.tryParse(_id.text);
                    if (id != null) _select(id);
                  },
                  icon: const Icon(Icons.search),
                  label: const Text('Lookup')),
              SizedBox(
                  width: 180,
                  child: TextField(
                      controller: _rate,
                      keyboardType:
                          const TextInputType.numberWithOptions(decimal: true),
                      decoration:
                          const InputDecoration(labelText: 'Rate (optional)'))),
              FilledButton.icon(
                  onPressed: _saving ? null : _saveGross,
                  icon: const Icon(Icons.save),
                  label: const Text('Save Gross')),
            ]),
        const SizedBox(height: 16),
        if (_selected == null)
          const Text(
              'Select a pending tare transaction to autofill its details.')
        else
          Wrap(spacing: 22, runSpacing: 8, children: [
            Text('Item: ${_selected!['item']}'),
            Text('Party: ${_selected!['party']}'),
            Text('Vehicle: ${_selected!['vehicleNumber']}'),
            Text('Driver: ${_selected!['driverName']}'),
            Text('Tare: ${_selected!['tareWeightQuintal']} Qtl'),
            Text('Tare Time: ${_selected!['tareDateTime']}'),
            Text('Tare Operator: ${_selected!['tareOperator']}'),
            Text(
                'Live Gross: ${context.watch<LiveWeightProvider>().current.weightQuintal.toStringAsFixed(2)} Qtl'),
            Text('Final: ${(finalWeight ?? 0).toStringAsFixed(2)} Qtl',
                style: const TextStyle(fontWeight: FontWeight.bold))
          ])
      ]);

  Widget _pendingGrid() => _pending.isEmpty
      ? const Center(
          child: Text('No SalePurchase tare transactions pending gross.'))
      : ListView.builder(
          itemCount: _pending.length,
          itemBuilder: (_, i) {
            final row = _pending[i];
            return GestureDetector(
                onDoubleTap: () => _select(row['salePurchaseId'] as int),
                child: ListTile(
                    dense: true,
                    leading: Text('${row['salePurchaseId']}',
                        style: const TextStyle(fontWeight: FontWeight.bold)),
                    title: Text(
                        '${row['item']} • ${row['party']} • ${row['vehicleNumber']}'),
                    subtitle: Text(
                        'Tare ${row['tareWeightQuintal']} Qtl • ${row['tareOperator']}'),
                    trailing: Chip(label: Text(row['status'] ?? ''))));
          });
}
