import 'dart:convert';
import 'dart:typed_data';
import 'package:flutter/material.dart';
import 'package:dio/dio.dart';
import 'package:file_picker/file_picker.dart';
import '../../core/api_client.dart';
import '../../core/print_service.dart';
import '../../core/file_download.dart';
import '../../core/hindi_transliteration.dart';

/// Developer Configuration hub: Weight Rules, Sound/TTS, Cameras, Print, SMS, Backup, Company.
class DeveloperSettingsScreen extends StatelessWidget {
  const DeveloperSettingsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 9,
      child: Column(children: [
        Material(
          color: Theme.of(context).colorScheme.surface,
          child: const TabBar(isScrollable: true, tabs: [
            Tab(text: 'Weight Rules'),
            Tab(text: 'Sound / TTS'),
            Tab(text: 'Cameras'),
            Tab(text: 'Print'),
            Tab(text: 'SMS'),
            Tab(text: 'SMS Logs'),
            Tab(text: 'Backup'),
            Tab(text: 'Company'),
            Tab(text: 'License'),
          ]),
        ),
        const Expanded(
          child: TabBarView(children: [
            _WeightRulesTab(),
            _SoundTab(),
            _CamerasTab(),
            _PrintTab(),
            _SmsTab(),
            _SmsLogsTab(),
            _BackupTab(),
            _CompanyTab(),
            _LicenseTab(),
          ]),
        ),
      ]),
    );
  }
}

void showResult(BuildContext context, dynamic res) {
  final ok = res.statusCode == 200;
  ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(
          ok ? res.data['message'] ?? 'Saved.' : ApiClient.errorMessage(res)),
      backgroundColor:
          ok ? const Color(0xFF2E7D32) : Theme.of(context).colorScheme.error));
}

class _WeightRulesTab extends StatefulWidget {
  const _WeightRulesTab();
  @override
  State<_WeightRulesTab> createState() => _WeightRulesTabState();
}

class _WeightRulesTabState extends State<_WeightRulesTab> {
  Map<String, dynamic> v = {};
  final _min = TextEditingController();
  final _cut = TextEditingController();
  final _tax = TextEditingController();

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final res = await ApiClient.instance.dio.get('/api/config/weight-rules');
    if (res.statusCode == 200 && mounted) {
      setState(() {
        v = Map<String, dynamic>.from(res.data);
        _min.text = (v['minimumWeightQuintal'] as num).toStringAsFixed(2);
        _cut.text = (v['defaultCuttingPercent'] as num).toStringAsFixed(2);
        _tax.text = (v['defaultTaxPercent'] as num).toStringAsFixed(2);
      });
    }
  }

  Future<void> _save() async {
    final res =
        await ApiClient.instance.dio.put('/api/config/weight-rules', data: {
      ...v,
      'minimumWeightQuintal': double.tryParse(_min.text) ?? 10,
      'defaultCuttingPercent': double.tryParse(_cut.text) ?? 0,
      'defaultTaxPercent': double.tryParse(_tax.text) ?? 0,
    });
    if (mounted) showResult(context, res);
  }

  @override
  Widget build(BuildContext context) {
    if (v.isEmpty) return const Center(child: CircularProgressIndicator());
    return ListView(padding: const EdgeInsets.all(16), children: [
      Wrap(spacing: 14, runSpacing: 14, children: [
        SizedBox(
            width: 220,
            child: TextField(
                controller: _min,
                decoration: const InputDecoration(
                    labelText: 'Minimum Weight (Quintal)',
                    hintText: 'Example: 10.00'))),
        SizedBox(
            width: 220,
            child: TextField(
                controller: _cut,
                decoration: const InputDecoration(
                    labelText: 'Default Cutting %',
                    hintText: 'Example: 2.00'))),
        SizedBox(
            width: 220,
            child: TextField(
                controller: _tax,
                decoration: const InputDecoration(
                    labelText: 'Default Tax %', hintText: 'Example: 0.00'))),
      ]),
      for (final e in [
        ('enabled', 'Minimum Weight Rule Enabled'),
        ('applyToCanePurchase', 'Apply to Cane Purchase'),
        ('applyToSalePurchase', 'Apply to SalePurchase'),
        ('applyToGross', 'Apply to Gross'),
        ('applyToTare', 'Apply to Tare')
      ])
        SwitchListTile(
            title: Text(e.$2),
            value: v[e.$1] == true,
            onChanged: (x) => setState(() => v[e.$1] = x)),
      const SizedBox(height: 10),
      FilledButton(onPressed: _save, child: const Text('Save Weight Rules')),
    ]);
  }
}

class _SoundTab extends StatefulWidget {
  const _SoundTab();
  @override
  State<_SoundTab> createState() => _SoundTabState();
}

class _SoundTabState extends State<_SoundTab> {
  Map<String, dynamic>? cfg;
  List messages = [];

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final res = await ApiClient.instance.dio.get('/api/config/sound');
    if (res.statusCode == 200 && mounted) {
      setState(() {
        cfg = Map<String, dynamic>.from(res.data['config']);
        messages = res.data['messages'];
      });
    }
  }

  Future<void> _save() async {
    final res =
        await ApiClient.instance.dio.put('/api/config/sound', data: cfg);
    if (mounted) showResult(context, res);
  }

  @override
  Widget build(BuildContext context) {
    if (cfg == null) return const Center(child: CircularProgressIndicator());
    return ListView(padding: const EdgeInsets.all(16), children: [
      SwitchListTile(
          title: const Text('Sound Enabled'),
          value: cfg!['soundEnabled'] == true,
          onChanged: (v) => setState(() => cfg!['soundEnabled'] = v)),
      Wrap(spacing: 14, runSpacing: 14, children: [
        SizedBox(
            width: 200,
            child: DropdownButtonFormField<String>(
                value: cfg!['language'],
                decoration: const InputDecoration(labelText: 'Language'),
                items: const [
                  DropdownMenuItem(value: 'hi', child: Text('Hindi')),
                  DropdownMenuItem(value: 'en', child: Text('English'))
                ],
                onChanged: (v) => setState(() => cfg!['language'] = v))),
        SizedBox(
            width: 200,
            child: DropdownButtonFormField<String>(
                value: cfg!['repeatMode'],
                decoration: const InputDecoration(labelText: 'Repeat Mode'),
                items: [
                  for (final m in ['OFF', 'ONCE', 'TWICE', 'CONTINUOUS'])
                    DropdownMenuItem(value: m, child: Text(m))
                ],
                onChanged: (v) => setState(() => cfg!['repeatMode'] = v))),
        SizedBox(
            width: 200,
            child: DropdownButtonFormField<int>(
                value: cfg!['repeatIntervalSeconds'],
                decoration: const InputDecoration(
                    labelText: 'Repeat Interval (seconds)'),
                items: [
                  for (final s in [5, 10, 30, 60])
                    DropdownMenuItem(value: s, child: Text('$s seconds'))
                ],
                onChanged: (v) =>
                    setState(() => cfg!['repeatIntervalSeconds'] = v))),
        SizedBox(
            width: 200,
            child: TextField(
                controller:
                    TextEditingController(text: '${cfg!['voiceVolume']}'),
                decoration:
                    const InputDecoration(labelText: 'Voice Volume (0-100)'),
                onChanged: (v) =>
                    cfg!['voiceVolume'] = int.tryParse(v) ?? 100)),
      ]),
      const SizedBox(height: 10),
      FilledButton(
          onPressed: _save, child: const Text('Save Sound Configuration')),
      const Divider(height: 30),
      const Text('Announcement Messages (editable per event & language)',
          style: TextStyle(fontWeight: FontWeight.w700)),
      for (final m in messages)
        ListTile(
          dense: true,
          title: Text('${m['eventCode']} (${m['languageCode']})',
              style:
                  const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
          subtitle: Text('${m['messageText']}'),
          trailing: IconButton(
            icon: const Icon(Icons.edit_outlined, size: 18),
            onPressed: () async {
              final ctl = TextEditingController(text: m['messageText']);
              final ok = await showDialog<bool>(
                  context: context,
                  builder: (ctx) => AlertDialog(
                          title: Text(
                              'Edit ${m['eventCode']} (${m['languageCode']})'),
                          content: TextField(controller: ctl, maxLines: 3),
                          actions: [
                            TextButton(
                                onPressed: () => Navigator.pop(ctx, false),
                                child: const Text('Cancel')),
                            FilledButton(
                                onPressed: () => Navigator.pop(ctx, true),
                                child: const Text('Save')),
                          ]));
              if (ok == true) {
                final res = await ApiClient.instance.dio.put(
                    '/api/config/sound/messages/${m['id']}',
                    data: {...m, 'messageText': ctl.text});
                if (context.mounted) showResult(context, res);
                _load();
              }
            },
          ),
        ),
    ]);
  }
}

class _CamerasTab extends StatefulWidget {
  const _CamerasTab();
  @override
  State<_CamerasTab> createState() => _CamerasTabState();
}

class _CamerasTabState extends State<_CamerasTab> {
  List cams = [];

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final res = await ApiClient.instance.dio.get('/api/config/cameras');
    if (res.statusCode == 200 && mounted) setState(() => cams = res.data);
  }

  Future<void> _edit([Map<String, dynamic>? cam]) async {
    if (cam == null && cams.length >= 6) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text(
              'Only six cameras can be configured. Edit or remove an existing camera first.')));
      return;
    }
    if (cam != null) {
      final detail = await ApiClient.instance.dio
          .get('/api/config/cameras/${cam['id']}/configuration');
      if (!mounted) return;
      if (detail.statusCode != 200) {
        showResult(context, detail);
        return;
      }
      cam = Map<String, dynamic>.from(detail.data);
    }
    final c = {
      for (final k in ['ipAddress', 'username', 'password', 'model', 'rtspUrl'])
        k: TextEditingController(
            text: '${cam?[k] ?? ''}'.replaceAll('null', ''))
    };
    final usedNumbers = cams.map((x) => x['cameraNumber'] as int).toSet();
    int number = cam?['cameraNumber'] ??
        List<int>.generate(6, (i) => i + 1)
            .firstWhere((i) => !usedNumbers.contains(i));
    String vendor = cam?['vendor'] ?? 'Hikvision';
    String protocol = cam?['protocol'] ?? 'RTSP';
    int port = cam?['port'] ?? 554;
    final portController = TextEditingController(text: '$port');
    bool capture = cam?['captureEnabled'] ?? true;
    bool liveView = cam?['liveViewEnabled'] ?? true;
    final ok = await showDialog<bool>(
        context: context,
        builder: (ctx) => StatefulBuilder(
            builder: (ctx, setD) => AlertDialog(
                    insetPadding: const EdgeInsets.all(24),
                    constraints: BoxConstraints(
                        maxWidth: 560,
                        maxHeight: MediaQuery.sizeOf(ctx).height - 48),
                    title: Text('Camera ${number.toString().padLeft(2, '0')}'),
                    content: SizedBox(
                        width: 500,
                        child: SingleChildScrollView(child:
                            LayoutBuilder(builder: (context, constraints) {
                          final halfWidth = (constraints.maxWidth - 10) / 2;
                          return Wrap(spacing: 10, runSpacing: 10, children: [
                            SizedBox(
                                width: halfWidth,
                                child: DropdownButtonFormField<int>(
                                    value: number,
                                    isExpanded: true,
                                    decoration: const InputDecoration(
                                        labelText: 'Camera No (1-6)'),
                                    items: [
                                      for (var i = 1; i <= 6; i++)
                                        DropdownMenuItem(
                                            value: i,
                                            child: Text('Camera $i',
                                                overflow:
                                                    TextOverflow.ellipsis))
                                    ],
                                    onChanged: (v) => setD(() => number = v!))),
                            SizedBox(
                                width: halfWidth,
                                child: DropdownButtonFormField<String>(
                                    value: vendor,
                                    isExpanded: true,
                                    decoration: const InputDecoration(
                                        labelText: 'Vendor'),
                                    items: [
                                      for (final v in [
                                        'Hikvision',
                                        'CPPlus',
                                        'Dahua',
                                        'Uniview',
                                        'GenericONVIF',
                                        'GenericRTSP'
                                      ])
                                        DropdownMenuItem(
                                            value: v,
                                            child: Text(v,
                                                overflow:
                                                    TextOverflow.ellipsis))
                                    ],
                                    onChanged: (v) => setD(() => vendor = v!))),
                            SizedBox(
                                width: halfWidth,
                                child: DropdownButtonFormField<String>(
                                    value: protocol,
                                    isExpanded: true,
                                    decoration: const InputDecoration(
                                        labelText: 'Protocol'),
                                    items: [
                                      for (final v in [
                                        'RTSP',
                                        'ONVIF',
                                        'ISAPI'
                                      ])
                                        DropdownMenuItem(
                                            value: v,
                                            child: Text(v,
                                                overflow:
                                                    TextOverflow.ellipsis))
                                    ],
                                    onChanged: (v) =>
                                        setD(() => protocol = v!))),
                            SizedBox(
                                width: halfWidth,
                                child: TextField(
                                    controller: c['model'],
                                    decoration: const InputDecoration(
                                        labelText: 'Model',
                                        hintText: 'Example: DS-2CD2046G2-IU'))),
                            SizedBox(
                                width: halfWidth,
                                child: TextField(
                                    controller: c['ipAddress'],
                                    decoration: const InputDecoration(
                                        labelText: 'IP Address',
                                        hintText: 'Example: 192.168.1.64'))),
                            SizedBox(
                                width: halfWidth,
                                child: TextField(
                                    controller: portController,
                                    decoration: const InputDecoration(
                                        labelText: 'Port', hintText: '554'),
                                    onChanged: (v) =>
                                        port = int.tryParse(v) ?? 554)),
                            SizedBox(
                                width: halfWidth,
                                child: TextField(
                                    controller: c['username'],
                                    decoration: const InputDecoration(
                                        labelText: 'Username',
                                        hintText:
                                            'Leave blank to keep existing'))),
                            SizedBox(
                                width: halfWidth,
                                child: TextField(
                                    controller: c['password'],
                                    obscureText: true,
                                    decoration: const InputDecoration(
                                        labelText:
                                            'Password (stored encrypted)',
                                        hintText:
                                            'Leave blank to keep existing'))),
                            SizedBox(
                                width: constraints.maxWidth,
                                child: TextField(
                                    controller: c['rtspUrl'],
                                    decoration: const InputDecoration(
                                        labelText:
                                            'RTSP URL (optional override)',
                                        hintText:
                                            'Leave blank to keep existing override'))),
                            SwitchListTile(
                                title: const Text('Capture Enabled'),
                                value: capture,
                                onChanged: (v) => setD(() => capture = v)),
                            SwitchListTile(
                                title: const Text('Live View Enabled'),
                                value: liveView,
                                onChanged: (v) => setD(() => liveView = v)),
                          ]);
                        }))),
                    actions: [
                      TextButton(
                          onPressed: () => Navigator.pop(ctx, false),
                          child: const Text('Cancel')),
                      FilledButton(
                          onPressed: () => Navigator.pop(ctx, true),
                          child: const Text('Save')),
                    ])));
    if (ok != true) return;
    final res = await ApiClient.instance.dio.post('/api/config/cameras', data: {
      'cameraNumber': number,
      'vendor': vendor,
      'protocol': protocol,
      'model': c['model']!.text,
      'ipAddress': c['ipAddress']!.text,
      'port': port,
      'username': c['username']!.text,
      'password': c['password']!.text.isEmpty ? null : c['password']!.text,
      'channel': cam?['channel'] ?? 1,
      'streamType': cam?['streamType'] ?? 'Main',
      'rtspUrl': c['rtspUrl']!.text.isEmpty ? null : c['rtspUrl']!.text,
      'captureEnabled': capture,
      'liveViewEnabled': liveView,
      'retentionDays': cam?['retentionDays'] ?? 365,
      'status': true,
    });
    if (mounted) showResult(context, res);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return ListView(padding: const EdgeInsets.all(16), children: [
      Row(children: [
        const Text('IP Cameras (1-6, vendor-abstracted)',
            style: TextStyle(fontWeight: FontWeight.w700)),
        const Spacer(),
        FilledButton.icon(
            onPressed: () => _edit(),
            icon: const Icon(Icons.add),
            label: const Text('Add Camera')),
      ]),
      for (final cam in cams)
        Card(
            child: ListTile(
          leading: Icon(Icons.videocam_outlined,
              color: cam['status'] == true
                  ? const Color(0xFF2E7D32)
                  : Colors.grey),
          title: Text(
              'Camera ${cam['cameraNumber'].toString().padLeft(2, '0')} • ${cam['vendor']} ${cam['model'] ?? ''}'),
          subtitle: Text(
              '${cam['protocol']} • Capture: ${cam['captureEnabled'] == true ? 'ON' : 'OFF'} • Live: ${cam['liveViewEnabled'] == true ? 'ON' : 'OFF'}'),
          trailing: Wrap(children: [
            TextButton(
                onPressed: () async {
                  final res = await ApiClient.instance.dio
                      .post('/api/config/cameras/${cam['id']}/test');
                  if (context.mounted) showResult(context, res);
                },
                child: const Text('Test')),
            TextButton(
                onPressed: () => _snapshot(cam['id']),
                child: const Text('Snapshot')),
            TextButton(
                onPressed: () => _edit(Map<String, dynamic>.from(cam)),
                child: const Text('Edit')),
          ]),
        )),
    ]);
  }

  /// Live capture preview (Phase 6) - one real snapshot right now via RTSP/ONVIF/ISAPI (or the
  /// SIMULATOR provider when Camera:SimulatorMode=true), never saved as purchase evidence.
  Future<void> _snapshot(int cameraId) async {
    showDialog(
        context: context,
        barrierDismissible: false,
        builder: (_) => const Center(child: CircularProgressIndicator()));
    try {
      final res = await ApiClient.instance.dio.get(
          '/api/config/cameras/$cameraId/snapshot',
          options: Options(
              responseType: ResponseType.bytes,
              validateStatus: (s) => s != null && s < 500));
      if (mounted) Navigator.of(context, rootNavigator: true).pop();
      if (!mounted) return;
      if (res.statusCode == 200) {
        await showDialog(
            context: context,
            builder: (ctx) => Dialog(
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: Column(mainAxisSize: MainAxisSize.min, children: [
                      Image.memory(Uint8List.fromList(res.data as List<int>),
                          width: 420),
                      const SizedBox(height: 8),
                      TextButton(
                          onPressed: () => Navigator.pop(ctx),
                          child: const Text('Close')),
                    ]),
                  ),
                ));
      } else {
        var message = 'Snapshot capture failed.';
        try {
          final decoded = jsonDecode(utf8.decode(res.data as List<int>)) as Map;
          if (decoded['message'] != null)
            message = decoded['message'].toString();
        } catch (_) {}
        if (mounted) {
          ScaffoldMessenger.of(context)
              .showSnackBar(SnackBar(content: Text(message)));
        }
      }
    } catch (e) {
      if (mounted) Navigator.of(context, rootNavigator: true).pop();
      if (mounted)
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('Snapshot failed: $e')));
    }
  }
}

class _PrintTab extends StatefulWidget {
  const _PrintTab();
  @override
  State<_PrintTab> createState() => _PrintTabState();
}

class _PrintTabState extends State<_PrintTab> {
  Map<String, dynamic>? v;
  List<String> _printers = const [];
  String _testTarget = 'DotMatrix';
  String _testLanguage = 'hi';

  @override
  void initState() {
    super.initState();
    ApiClient.instance.dio.get('/api/config/print').then((res) {
      if (res.statusCode == 200 && mounted) {
        final config = Map<String, dynamic>.from(res.data);
        if ((config['dotMatrixPrinterName'] ?? '').toString().isEmpty &&
            config['printerType'] == 'DotMatrix') {
          config['dotMatrixPrinterName'] = config['printerName'] ?? '';
        }
        if ((config['a4PrinterName'] ?? '').toString().isEmpty &&
            config['printerType'] == 'A4') {
          config['a4PrinterName'] = config['printerName'] ?? '';
        }
        setState(() {
          v = config;
          _printers =
              PrintService.installedPrinters().map((p) => p.name).toList();
        });
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    if (v == null) return const Center(child: CircularProgressIndicator());
    return ListView(padding: const EdgeInsets.all(16), children: [
      Wrap(spacing: 14, runSpacing: 14, children: [
        SizedBox(
            width: 220,
            child: DropdownButtonFormField<String>(
                value: v!['printerType'],
                decoration: const InputDecoration(labelText: 'Printer Type'),
                items: const [
                  DropdownMenuItem(
                      value: 'DotMatrix', child: Text('Dot Matrix')),
                  DropdownMenuItem(value: 'A4', child: Text('A4 Laser/Inkjet'))
                ],
                onChanged: (x) => setState(() => v!['printerType'] = x))),
        SizedBox(
            width: 280,
            child: DropdownButtonFormField<String>(
                value: _printers.contains(v!['dotMatrixPrinterName'])
                    ? v!['dotMatrixPrinterName']
                    : null,
                decoration: const InputDecoration(
                    labelText: 'Dot Matrix Printer (installed Windows queue)'),
                hint: const Text('Select installed printer'),
                items: [
                  for (final name in _printers)
                    DropdownMenuItem(
                        value: name,
                        child: Text(name, overflow: TextOverflow.ellipsis))
                ],
                onChanged: (x) =>
                    setState(() => v!['dotMatrixPrinterName'] = x ?? ''))),
        SizedBox(
            width: 280,
            child: DropdownButtonFormField<String>(
                value: _printers.contains(v!['a4PrinterName'])
                    ? v!['a4PrinterName']
                    : null,
                decoration: const InputDecoration(
                    labelText: 'A4 Printer (installed Windows queue)'),
                hint: const Text('Select installed printer'),
                items: [
                  for (final name in _printers)
                    DropdownMenuItem(
                        value: name,
                        child: Text(name, overflow: TextOverflow.ellipsis))
                ],
                onChanged: (x) =>
                    setState(() => v!['a4PrinterName'] = x ?? ''))),
        SizedBox(
            width: 180,
            child: DropdownButtonFormField<String>(
                value: v!['language'],
                decoration: const InputDecoration(labelText: 'Slip Language'),
                items: const [
                  DropdownMenuItem(value: 'hi', child: Text('Hindi (default)')),
                  DropdownMenuItem(value: 'en', child: Text('English'))
                ],
                onChanged: (x) => setState(() => v!['language'] = x))),
      ]),
      SwitchListTile(
          title: const Text('Auto Print after Save'),
          value: v!['autoPrint'] == true,
          onChanged: (x) => setState(() => v!['autoPrint'] = x)),
      Wrap(spacing: 14, runSpacing: 14, children: [
        for (final e in [
          ('grossCopies', 'Gross Copies'),
          ('tareCopies', 'Tare Copies'),
          ('paymentCopies', 'Payment Copies'),
          ('loanCopies', 'Loan Copies'),
          ('salePurchaseCopies', 'SalePurchase Copies')
        ])
          SizedBox(
              width: 170,
              child: DropdownButtonFormField<int>(
                  value: v![e.$1],
                  decoration: InputDecoration(labelText: e.$2),
                  items: [
                    for (var i = 0; i <= 5; i++)
                      DropdownMenuItem(value: i, child: Text('$i'))
                  ],
                  onChanged: (x) => setState(() => v![e.$1] = x))),
      ]),
      const SizedBox(height: 12),
      FilledButton(
          onPressed: () async {
            final res =
                await ApiClient.instance.dio.put('/api/config/print', data: v);
            if (context.mounted) showResult(context, res);
          },
          child: const Text('Save Print Configuration')),
      const Divider(height: 32),
      const Text('Print Test',
          style: TextStyle(fontWeight: FontWeight.w700, fontSize: 14)),
      const Text(
          'Verify printer, paper, Hindi glyph rendering, QR readability and alignment before enabling Auto Print.',
          style: TextStyle(fontSize: 11, color: Colors.grey)),
      const SizedBox(height: 10),
      Wrap(spacing: 14, runSpacing: 14, children: [
        SizedBox(
            width: 200,
            child: DropdownButtonFormField<String>(
                value: _testTarget,
                decoration: const InputDecoration(labelText: 'Test Target'),
                items: const [
                  DropdownMenuItem(
                      value: 'DotMatrix', child: Text('Dot Matrix')),
                  DropdownMenuItem(value: 'A4', child: Text('A4'))
                ],
                onChanged: (x) => setState(() => _testTarget = x!))),
        SizedBox(
            width: 180,
            child: DropdownButtonFormField<String>(
                value: _testLanguage,
                decoration: const InputDecoration(labelText: 'Test Language'),
                items: const [
                  DropdownMenuItem(value: 'hi', child: Text('Hindi')),
                  DropdownMenuItem(value: 'en', child: Text('English'))
                ],
                onChanged: (x) => setState(() => _testLanguage = x!))),
      ]),
      const SizedBox(height: 10),
      Wrap(spacing: 10, children: [
        OutlinedButton.icon(
            onPressed: _previewTest,
            icon: const Icon(Icons.visibility),
            label: const Text('Preview')),
        FilledButton.icon(
            onPressed: _printTest,
            icon: const Icon(Icons.print),
            label: const Text('Print Test Page')),
      ]),
    ]);
  }

  Future<void> _previewTest() async {
    showDialog(
        context: context,
        barrierDismissible: false,
        builder: (_) => const Center(child: CircularProgressIndicator()));
    try {
      final res = await ApiClient.instance.dio.get(
          '/api/print/test?target=$_testTarget&language=$_testLanguage&format=preview',
          options: Options(
              responseType: ResponseType.bytes,
              validateStatus: (s) => s != null && s < 500));
      if (mounted) Navigator.of(context, rootNavigator: true).pop();
      if (!mounted) return;
      if (res.statusCode != 200) {
        _showError(res.data);
        return;
      }
      final bytes = Uint8List.fromList(res.data as List<int>);
      if (_testTarget == 'DotMatrix') {
        await showDialog(
            context: context,
            builder: (ctx) => Dialog(
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: Column(mainAxisSize: MainAxisSize.min, children: [
                      Image.memory(bytes, width: 480),
                      const SizedBox(height: 8),
                      TextButton(
                          onPressed: () => Navigator.pop(ctx),
                          child: const Text('Close')),
                    ]),
                  ),
                ));
      } else {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
            content: Text(
                'A4 preview generated (PDF). Use "Print Test Page" to send it to the printer.')));
      }
    } catch (e) {
      if (mounted) Navigator.of(context, rootNavigator: true).pop();
      if (mounted)
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('Preview failed: $e')));
    }
  }

  Future<void> _printTest() async {
    if (v == null) return;
    final printerName = _testTarget == 'A4'
        ? (v!['a4PrinterName'] ?? '')
        : (v!['dotMatrixPrinterName'] ?? '');
    final outcome = await PrintService.printDocument(
      documentUrl:
          '/api/print/test?target=$_testTarget&language=$_testLanguage&format=final',
      printerType: _testTarget,
      printerName: printerName,
    );
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(outcome.message),
          backgroundColor: outcome.success ? Colors.green : Colors.red));
    }
  }

  void _showError(List<int> bytes) {
    var message = 'Preview failed.';
    try {
      final decoded = jsonDecode(utf8.decode(bytes)) as Map;
      if (decoded['message'] != null) message = decoded['message'].toString();
    } catch (_) {}
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }
}

class _SmsTab extends StatefulWidget {
  const _SmsTab();
  @override
  State<_SmsTab> createState() => _SmsTabState();
}

class _SmsTabState extends State<_SmsTab> {
  final c = {
    for (final k in [
      'providerName',
      'apiBaseUrl',
      'apiKey',
      'apiSecret',
      'authorizationHeader',
      'senderId',
      'entityId',
      'requestBodyTemplate',
      'responseSuccessPath',
      'responseSuccessValue',
      'salePurchaseRecipients',
      'testMobile',
      'testMessage'
    ])
      k: TextEditingController()
  };
  String method = 'POST';
  String requestContentType = 'application/json';
  String language = 'hi';
  bool enabled = false;
  String? info;
  List templates = [];
  bool testing = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final res = await ApiClient.instance.dio.get('/api/config/sms');
    if (res.statusCode != 200 || !mounted) return;
    final v = res.data['config'];
    setState(() {
      templates = res.data['templates'] ?? [];
      if (v != null) {
        c['providerName']!.text = v['providerName'] ?? '';
        c['apiBaseUrl']!.text = v['apiBaseUrl'] ?? '';
        c['authorizationHeader']!.text = v['authorizationHeader'] ?? '';
        c['senderId']!.text = v['senderId'] ?? '';
        c['entityId']!.text = v['entityId'] ?? '';
        c['requestBodyTemplate']!.text = v['requestBodyTemplate'] ?? '';
        c['responseSuccessPath']!.text = v['responseSuccessPath'] ?? '';
        c['responseSuccessValue']!.text = v['responseSuccessValue'] ?? '';
        c['salePurchaseRecipients']!.text = v['salePurchaseRecipients'] ?? '';
        method = v['httpMethod'] ?? 'POST';
        requestContentType = v['requestContentType'] ?? 'application/json';
        language = v['language'] ?? 'hi';
        enabled = v['enabled'] == true;
        info =
            'API Key: ${v['hasApiKey'] == true ? 'set (encrypted)' : 'not set'} • API Secret: ${v['hasApiSecret'] == true ? 'set (encrypted)' : 'not set'}';
      }
    });
  }

  Future<void> _save() async {
    final res = await ApiClient.instance.dio.put('/api/config/sms', data: {
      'providerName': c['providerName']!.text,
      'apiBaseUrl': c['apiBaseUrl']!.text,
      'httpMethod': method,
      'apiKey': c['apiKey']!.text.isEmpty ? null : c['apiKey']!.text,
      'apiSecret': c['apiSecret']!.text.isEmpty ? null : c['apiSecret']!.text,
      'authorizationHeader': c['authorizationHeader']!.text,
      'senderId': c['senderId']!.text,
      'entityId': c['entityId']!.text,
      'enabled': enabled,
      'language': language,
      'requestContentType': requestContentType,
      'requestBodyTemplate': c['requestBodyTemplate']!.text,
      'responseSuccessPath': c['responseSuccessPath']!.text,
      'responseSuccessValue': c['responseSuccessValue']!.text,
      'salePurchaseRecipients': c['salePurchaseRecipients']!.text,
    });
    if (context.mounted) showResult(context, res);
    _load();
  }

  Future<void> _testConnection() async {
    setState(() => testing = true);
    final res =
        await ApiClient.instance.dio.post('/api/config/sms/test-connection');
    setState(() => testing = false);
    if (context.mounted) showResult(context, res);
  }

  Future<void> _testSend() async {
    if (c['testMobile']!.text.trim().isEmpty) return;
    setState(() => testing = true);
    final res =
        await ApiClient.instance.dio.post('/api/config/sms/test-send', data: {
      'mobileNumber': c['testMobile']!.text.trim(),
      'message': c['testMessage']!.text.trim().isEmpty
          ? null
          : c['testMessage']!.text.trim(),
    });
    setState(() => testing = false);
    if (!context.mounted) return;
    final ok = res.statusCode == 200 && res.data['success'] == true;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(res.statusCode == 200
            ? res.data['message']
            : ApiClient.errorMessage(res)),
        backgroundColor: ok
            ? const Color(0xFF2E7D32)
            : Theme.of(context).colorScheme.error));
  }

  Future<void> _editTemplate(Map t) async {
    final ctl = TextEditingController(text: t['messageTemplate'] ?? '');
    final dltCtl = TextEditingController(text: t['dltTemplateId'] ?? '');
    bool enabledT = t['enabled'] == true;
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => StatefulBuilder(
          builder: (ctx, setD) => AlertDialog(
                title: Text('${t['eventCode']} (${t['language']})'),
                content: SizedBox(
                  width: 480,
                  child: Column(
                      mainAxisSize: MainAxisSize.min,
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Text(
                            'Placeholders: {GrowerName} {GrowerCode} {VehicleNumber} {FinalWeight} {PurchaseAmount} '
                            '{AdviceNumber} {TotalPurchaseAmount} {LoanDeducted} {NetPayable} {PaymentMode}',
                            style: TextStyle(
                                fontSize: 11, fontStyle: FontStyle.italic)),
                        const SizedBox(height: 8),
                        TextField(
                            controller: ctl,
                            maxLines: 4,
                            decoration: const InputDecoration(
                                labelText: 'Message Template')),
                        const SizedBox(height: 8),
                        TextField(
                            controller: dltCtl,
                            decoration: const InputDecoration(
                                labelText: 'DLT Template ID (optional)')),
                        SwitchListTile(
                            title: const Text('Enabled'),
                            value: enabledT,
                            onChanged: (v) => setD(() => enabledT = v)),
                      ]),
                ),
                actions: [
                  TextButton(
                      onPressed: () => Navigator.pop(ctx, false),
                      child: const Text('Cancel')),
                  FilledButton(
                      onPressed: () => Navigator.pop(ctx, true),
                      child: const Text('Save')),
                ],
              )),
    );
    if (ok != true) return;
    final res =
        await ApiClient.instance.dio.post('/api/config/sms/templates', data: {
      'eventCode': t['eventCode'],
      'language': t['language'],
      'messageTemplate': ctl.text,
      'dltTemplateId': dltCtl.text.trim().isEmpty ? null : dltCtl.text.trim(),
      'enabled': enabledT,
    });
    if (context.mounted) showResult(context, res);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return ListView(padding: const EdgeInsets.all(16), children: [
      const Text(
          'Generic HTTP SMS provider - works with ANY vendor by configuring the request/response '
          'templates below. No provider is hard-coded. Secrets are encrypted server-side and never sent to any client.',
          style: TextStyle(fontSize: 12, fontStyle: FontStyle.italic)),
      if (info != null)
        Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Text(info!, style: const TextStyle(fontSize: 12))),
      const SizedBox(height: 10),
      LayoutBuilder(builder: (context, constraints) {
        final width = constraints.maxWidth;
        return Wrap(spacing: 14, runSpacing: 14, children: [
          SizedBox(
              width: width < 240 ? width : 240,
              child: TextField(
                  controller: c['providerName'],
                  decoration: const InputDecoration(
                      labelText: 'Provider Name',
                      hintText: 'Example: your local SMS panel name'))),
          SizedBox(
              width: width < 340 ? width : 340,
              child: TextField(
                  controller: c['apiBaseUrl'],
                  decoration: const InputDecoration(
                      labelText: 'API Base URL',
                      hintText:
                          'https://api.provider.com/send?key={ApiKey}&to={Mobile}&msg={Message}'))),
          SizedBox(
              width: width < 140 ? width : 140,
              child: DropdownButtonFormField<String>(
                  value: method,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'HTTP Method'),
                  items: const [
                    DropdownMenuItem(
                        value: 'POST',
                        child: Text('POST', overflow: TextOverflow.ellipsis)),
                    DropdownMenuItem(
                        value: 'GET',
                        child: Text('GET', overflow: TextOverflow.ellipsis))
                  ],
                  onChanged: (v) => setState(() => method = v!))),
          SizedBox(
              width: width < 140 ? width : 140,
              child: DropdownButtonFormField<String>(
                  value: language,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Language'),
                  items: const [
                    DropdownMenuItem(
                        value: 'hi',
                        child: Text('Hindi (default)',
                            overflow: TextOverflow.ellipsis)),
                    DropdownMenuItem(
                        value: 'en',
                        child: Text('English', overflow: TextOverflow.ellipsis))
                  ],
                  onChanged: (v) => setState(() => language = v!))),
          SizedBox(
              width: width < 240 ? width : 240,
              child: TextField(
                  controller: c['apiKey'],
                  obscureText: true,
                  decoration: const InputDecoration(
                      labelText: 'API Key',
                      hintText: 'Leave blank to keep existing'))),
          SizedBox(
              width: width < 240 ? width : 240,
              child: TextField(
                  controller: c['apiSecret'],
                  obscureText: true,
                  decoration: const InputDecoration(
                      labelText: 'API Secret',
                      hintText: 'Leave blank to keep existing'))),
          SizedBox(
              width: width < 240 ? width : 240,
              child: TextField(
                  controller: c['authorizationHeader'],
                  decoration: const InputDecoration(
                      labelText: 'Authorization Header',
                      hintText: 'Example: Bearer {ApiKey}'))),
          SizedBox(
              width: width < 180 ? width : 180,
              child: TextField(
                  controller: c['senderId'],
                  decoration: const InputDecoration(
                      labelText: 'Sender ID', hintText: 'Example: FCTORY'))),
          SizedBox(
              width: width < 240 ? width : 240,
              child: TextField(
                  controller: c['entityId'],
                  decoration: const InputDecoration(
                      labelText: 'DLT Entity ID', hintText: 'Where required'))),
          SizedBox(
              width: width < 200 ? width : 200,
              child: TextField(
                  controller: TextEditingController(text: requestContentType),
                  decoration:
                      const InputDecoration(labelText: 'Request Content-Type'),
                  onChanged: (v) => requestContentType = v)),
        ]);
      }),
      const SizedBox(height: 10),
      TextField(
          controller: c['requestBodyTemplate'],
          maxLines: 3,
          decoration: const InputDecoration(
              labelText: 'Request Body Template (POST only)',
              hintText:
                  '{"to":"{Mobile}","text":"{Message}","sender":"{SenderId}","key":"{ApiKey}"}')),
      const SizedBox(height: 10),
      LayoutBuilder(builder: (context, constraints) {
        final width = constraints.maxWidth;
        return Wrap(spacing: 14, runSpacing: 14, children: [
          SizedBox(
              width: width < 240 ? width : 240,
              child: TextField(
                  controller: c['responseSuccessPath'],
                  decoration: const InputDecoration(
                      labelText: 'Response Success JSON Field',
                      hintText: 'Example: status'))),
          SizedBox(
              width: width < 240 ? width : 240,
              child: TextField(
                  controller: c['responseSuccessValue'],
                  decoration: const InputDecoration(
                      labelText: 'Expected Success Value',
                      hintText: 'Example: success'))),
          SizedBox(
              width: width < 300 ? width : 300,
              child: TextField(
                  controller: c['salePurchaseRecipients'],
                  decoration: const InputDecoration(
                      labelText: 'SalePurchase SMS Recipients',
                      hintText: 'Comma-separated 10-digit mobile numbers'))),
        ]);
      }),
      SwitchListTile(
          title: const Text('SMS Enabled'),
          value: enabled,
          onChanged: (v) => setState(() => enabled = v)),
      Wrap(spacing: 10, children: [
        FilledButton(
            onPressed: _save, child: const Text('Save SMS Configuration')),
        OutlinedButton(
            onPressed: testing ? null : _testConnection,
            child: const Text('Test Connection')),
      ]),
      const Divider(height: 32),
      Text('Test Send SMS',
          style: Theme.of(context)
              .textTheme
              .titleSmall
              ?.copyWith(fontWeight: FontWeight.w700)),
      const SizedBox(height: 8),
      LayoutBuilder(builder: (context, constraints) {
        final width = constraints.maxWidth;
        return Wrap(spacing: 14, runSpacing: 14, children: [
          SizedBox(
              width: width < 200 ? width : 200,
              child: TextField(
                  controller: c['testMobile'],
                  decoration:
                      const InputDecoration(labelText: 'Mobile Number'))),
          SizedBox(
              width: width < 300 ? width : 300,
              child: TextField(
                  controller: c['testMessage'],
                  decoration:
                      const InputDecoration(labelText: 'Message (optional)'))),
          FilledButton.tonal(
              onPressed: testing ? null : _testSend,
              child: Text(testing ? 'Sending...' : 'Send Test SMS')),
        ]);
      }),
      const Divider(height: 32),
      Text('Message Templates',
          style: Theme.of(context)
              .textTheme
              .titleSmall
              ?.copyWith(fontWeight: FontWeight.w700)),
      const SizedBox(height: 8),
      for (final t in templates)
        Card(
          child: ListTile(
            title: Text('${t['eventCode']} (${t['language']})'),
            subtitle: Text(t['messageTemplate'] ?? '',
                maxLines: 2, overflow: TextOverflow.ellipsis),
            trailing: Wrap(spacing: 6, children: [
              Chip(
                  label: Text(t['enabled'] == true ? 'ON' : 'OFF',
                      style:
                          const TextStyle(fontSize: 10, color: Colors.white)),
                  backgroundColor: t['enabled'] == true
                      ? const Color(0xFF2E7D32)
                      : Colors.grey,
                  visualDensity: VisualDensity.compact),
              IconButton(
                  icon: const Icon(Icons.edit_outlined, size: 18),
                  onPressed: () => _editTemplate(t)),
            ]),
          ),
        ),
    ]);
  }
}

class _SmsLogsTab extends StatefulWidget {
  const _SmsLogsTab();
  @override
  State<_SmsLogsTab> createState() => _SmsLogsTabState();
}

class _SmsLogsTabState extends State<_SmsLogsTab> {
  List items = [];
  bool loading = true;
  String? statusFilter;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => loading = true);
    final res = await ApiClient.instance.dio.get('/api/sms-logs',
        queryParameters: statusFilter == null ? {} : {'status': statusFilter});
    if (res.statusCode == 200 && mounted)
      setState(() => items = res.data['items']);
    if (mounted) setState(() => loading = false);
  }

  Future<void> _retry(int id) async {
    final res = await ApiClient.instance.dio.post('/api/sms-logs/$id/retry');
    if (context.mounted) showResult(context, res);
    _load();
  }

  Color _statusColor(String s) => switch (s) {
        'SENT' => const Color(0xFF2E7D32),
        'FAILED' => Colors.red,
        'RETRY_PENDING' => Colors.orange,
        _ => Colors.blueGrey,
      };

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Wrap(
            spacing: 10,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              const Text('Filter:'),
              for (final s in [
                null,
                'QUEUED',
                'PROCESSING',
                'SENT',
                'RETRY_PENDING',
                'FAILED'
              ])
                ChoiceChip(
                  label: Text(s ?? 'ALL'),
                  selected: statusFilter == s,
                  onSelected: (_) {
                    setState(() => statusFilter = s);
                    _load();
                  },
                ),
              IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
            ]),
        const SizedBox(height: 8),
        Expanded(
          child: loading
              ? const Center(child: CircularProgressIndicator())
              : ListView.builder(
                  itemCount: items.length,
                  itemBuilder: (ctx, i) {
                    final l = items[i];
                    return Card(
                      child: ListTile(
                        title: Text(
                            '#${l['id']} ${l['eventCode']} → ${l['mobileMasked']}'),
                        subtitle: Text(
                            '${l['messageText']}\nRef: ${l['referenceId']}  •  Attempts: ${l['attemptCount']}'
                            '${l['failureReason'] != null ? '  •  Error: ${l['failureReason']}' : ''}'),
                        isThreeLine: true,
                        trailing: Wrap(
                            spacing: 6,
                            crossAxisAlignment: WrapCrossAlignment.center,
                            children: [
                              Chip(
                                  label: Text(l['status'],
                                      style: const TextStyle(
                                          fontSize: 10, color: Colors.white)),
                                  backgroundColor: _statusColor(l['status']),
                                  visualDensity: VisualDensity.compact),
                              if (l['status'] == 'FAILED' ||
                                  l['status'] == 'RETRY_PENDING')
                                IconButton(
                                    icon: const Icon(Icons.refresh, size: 18),
                                    tooltip: 'Retry now',
                                    onPressed: () => _retry(l['id'])),
                            ]),
                      ),
                    );
                  },
                ),
        ),
      ]),
    );
  }
}

/// Phase 13: SQL Server backup policy - schedule/retention config + one-click .sql / Task
/// Scheduler XML download (no SQL Agent needed on Express edition).
class _BackupTab extends StatefulWidget {
  const _BackupTab();
  @override
  State<_BackupTab> createState() => _BackupTabState();
}

class _BackupTabState extends State<_BackupTab> {
  final folderCtrl = TextEditingController(text: r'E:\Backup');
  final retentionCtrl = TextEditingController(text: '14');
  final timeCtrl = TextEditingController(text: '02:00');
  String frequency = 'Daily';
  bool enabled = true;
  bool differentialEnabled = true;
  bool transactionLogEnabled = true;

  @override
  void initState() {
    super.initState();
    ApiClient.instance.dio.get('/api/backup/config').then((res) {
      if (res.statusCode == 200 && res.data != null && mounted) {
        final v = res.data;
        setState(() {
          enabled = v['enabled'] == true;
          frequency = v['frequency'] ?? 'Daily';
          timeCtrl.text = v['timeOfDay'] ?? '02:00';
          retentionCtrl.text = '${v['retentionDays'] ?? 14}';
          folderCtrl.text = v['backupFolderPath'] ?? r'E:\Backup';
          differentialEnabled = v['differentialEnabled'] == true;
          transactionLogEnabled = v['transactionLogEnabled'] == true;
        });
      }
    });
  }

  Future<void> _download(String path, String filename) =>
      downloadAndNotify(context, path, filename);

  @override
  Widget build(BuildContext context) {
    return ListView(padding: const EdgeInsets.all(16), children: [
      const Text(
          'SQL Server 2019 Express has no SQL Agent - schedule the generated .sql script via Windows Task Scheduler + sqlcmd.',
          style: TextStyle(fontSize: 12, fontStyle: FontStyle.italic)),
      const SizedBox(height: 10),
      Wrap(spacing: 14, runSpacing: 14, children: [
        SizedBox(
            width: 160,
            child: DropdownButtonFormField<String>(
                value: frequency,
                decoration: const InputDecoration(labelText: 'Frequency'),
                items: const [
                  DropdownMenuItem(value: 'Daily', child: Text('Daily')),
                  DropdownMenuItem(value: 'Hourly', child: Text('Hourly')),
                  DropdownMenuItem(value: 'Weekly', child: Text('Weekly'))
                ],
                onChanged: (v) => setState(() => frequency = v!))),
        SizedBox(
            width: 140,
            child: TextField(
                controller: timeCtrl,
                decoration: const InputDecoration(
                    labelText: 'Time (HH:mm)', hintText: '02:00'))),
        SizedBox(
            width: 160,
            child: TextField(
                controller: retentionCtrl,
                keyboardType: TextInputType.number,
                decoration:
                    const InputDecoration(labelText: 'Retention (days)'))),
        SizedBox(
            width: 260,
            child: TextField(
                controller: folderCtrl,
                decoration: const InputDecoration(
                    labelText: 'Backup Folder Path', hintText: r'E:\Backup'))),
      ]),
      SwitchListTile(
          title: const Text('Backup Enabled'),
          value: enabled,
          onChanged: (v) => setState(() => enabled = v)),
      SwitchListTile(
          title: const Text('Differential backups (every few hours)'),
          value: differentialEnabled,
          onChanged: (v) => setState(() => differentialEnabled = v)),
      SwitchListTile(
          title: const Text('Transaction log backups (every few minutes)'),
          value: transactionLogEnabled,
          onChanged: (v) => setState(() => transactionLogEnabled = v)),
      Wrap(spacing: 10, runSpacing: 10, children: [
        FilledButton(
            onPressed: () async {
              final res =
                  await ApiClient.instance.dio.put('/api/backup/config', data: {
                'enabled': enabled,
                'frequency': frequency,
                'timeOfDay': timeCtrl.text,
                'retentionDays': int.tryParse(retentionCtrl.text) ?? 14,
                'backupFolderPath': folderCtrl.text,
                'differentialEnabled': differentialEnabled,
                'differentialIntervalHours': 4,
                'transactionLogEnabled': transactionLogEnabled,
                'transactionLogIntervalMinutes': 30,
              });
              if (context.mounted) showResult(context, res);
            },
            child: const Text('Save Backup Policy')),
        OutlinedButton(
            onPressed: () =>
                _download('/api/backup/script', 'CaneFactoryBackup.sql'),
            child: const Text('Download Backup Script (.sql)')),
        OutlinedButton(
            onPressed: () => _download(
                '/api/backup/task-scheduler-xml', 'CaneFactoryBackup-Task.xml'),
            child: const Text('Download Task Scheduler XML')),
      ]),
    ]);
  }
}

class _CompanyTab extends StatefulWidget {
  const _CompanyTab();
  @override
  State<_CompanyTab> createState() => _CompanyTabState();
}

class _CompanyTabState extends State<_CompanyTab> {
  Map<String, dynamic>? v;
  final _logoPath = TextEditingController();
  final _companyNameHi = TextEditingController();
  final _addressHi = TextEditingController();
  bool _companyNameHiManual = false;
  bool _addressHiManual = false;

  @override
  void initState() {
    super.initState();
    ApiClient.instance.dio.get('/api/config/company').then((res) {
      if (res.statusCode == 200 && mounted)
        setState(() {
          v = Map<String, dynamic>.from(res.data);
          _logoPath.text = v!['logoPath']?.toString() ?? '';
          _companyNameHi.text = v!['companyNameHi']?.toString() ?? '';
          _addressHi.text = v!['addressHi']?.toString() ?? '';
        });
    });
  }

  @override
  void dispose() {
    _logoPath.dispose();
    _companyNameHi.dispose();
    _addressHi.dispose();
    super.dispose();
  }

  Future<void> _chooseLogo() async {
    final picked = await FilePicker.platform.pickFiles(
        type: FileType.custom, allowedExtensions: const ['png', 'jpg', 'jpeg']);
    final path = picked?.files.single.path;
    if (path != null && mounted) {
      setState(() {
        _logoPath.text = path;
        v!['logoPath'] = path;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    if (v == null) return const Center(child: CircularProgressIndicator());
    return ListView(padding: const EdgeInsets.all(16), children: [
      TextField(
          controller: TextEditingController(text: v!['companyName']),
          decoration: const InputDecoration(
              labelText: 'Company Name',
              hintText: 'Printed on all slips/reports'),
          onChanged: (x) {
            v!['companyName'] = x;
            if (!_companyNameHiManual) _companyNameHi.text = HindiTransliterator.transliterate(x);
          }),
      const SizedBox(height: 12),
      TextField(
          controller: _companyNameHi,
          decoration: const InputDecoration(labelText: 'Company Name (Hindi)'),
          onChanged: (x) { _companyNameHiManual = true; v!['companyNameHi'] = x; }),
      const SizedBox(height: 12),
      TextField(
          controller: TextEditingController(text: v!['address']),
          decoration: const InputDecoration(labelText: 'Address'),
          maxLines: 2,
          onChanged: (x) {
            v!['address'] = x;
            if (!_addressHiManual) _addressHi.text = HindiTransliterator.transliterate(x);
          }),
      const SizedBox(height: 12),
      TextField(
          controller: _addressHi,
          decoration: const InputDecoration(labelText: 'Address (Hindi)'),
          maxLines: 2,
          onChanged: (x) { _addressHiManual = true; v!['addressHi'] = x; }),
      const SizedBox(height: 12),
      TextField(
          controller: _logoPath,
          decoration: InputDecoration(
              labelText: 'Print Logo File',
              hintText: 'PNG/JPG logo used on all slips and reports',
              suffixIcon: IconButton(
                  tooltip: 'Choose logo image',
                  icon: const Icon(Icons.folder_open_outlined),
                  onPressed: _chooseLogo)),
          onChanged: (x) =>
              v!['logoPath'] = x.trim().isEmpty ? null : x.trim()),
      const Padding(
          padding: EdgeInsets.only(top: 4),
          child: Text(
              'Select a PNG/JPG stored on this factory PC. It is used by A4/PDF and Dot Matrix slips.',
              style: TextStyle(fontSize: 12, color: Colors.grey))),
      const SizedBox(height: 12),
      DropdownButtonFormField<String>(
          value: v!['defaultLanguage'],
          decoration: const InputDecoration(labelText: 'Default Language'),
          items: const [
            DropdownMenuItem(value: 'en', child: Text('English')),
            DropdownMenuItem(value: 'hi', child: Text('Hindi'))
          ],
          onChanged: (x) => setState(() => v!['defaultLanguage'] = x)),
      const SizedBox(height: 12),
      FilledButton(
          onPressed: () async {
            final res = await ApiClient.instance.dio
                .put('/api/config/company', data: v);
            if (context.mounted) showResult(context, res);
          },
          child: const Text('Save Company Configuration')),
    ]);
  }
}

class _LicenseTab extends StatefulWidget {
  const _LicenseTab();
  @override
  State<_LicenseTab> createState() => _LicenseTabState();
}

class _LicenseTabState extends State<_LicenseTab> {
  final _endDate = TextEditingController();
  String? _message;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final results = await Future.wait([
      ApiClient.instance.dio.get('/api/config/settings'),
      ApiClient.instance.dio.get('/api/license/status'),
    ]);
    if (!mounted) return;
    final settings = results[0];
    if (settings.statusCode == 200) {
      final item = (settings.data as List)
          .cast<dynamic>()
          .where((s) => s['key'] == 'LicenseEndDate')
          .firstOrNull;
      _endDate.text = item?['value']?.toString() ?? '';
    }
    final status = results[1];
    if (status.statusCode == 200)
      setState(() => _message = status.data['message']?.toString());
  }

  @override
  Widget build(BuildContext context) =>
      ListView(padding: const EdgeInsets.all(16), children: [
        const Text('License control',
            style: TextStyle(fontWeight: FontWeight.w700)),
        const SizedBox(height: 8),
        const Text(
            'Set an expiry date in YYYY-MM-DD. Leave it blank only when the license is intentionally not date-limited.',
            style: TextStyle(fontSize: 12)),
        const SizedBox(height: 14),
        SizedBox(
            width: 260,
            child: TextField(
                controller: _endDate,
                decoration: const InputDecoration(
                    labelText: 'License End Date', hintText: 'YYYY-MM-DD'))),
        const SizedBox(height: 12),
        Wrap(spacing: 10, children: [
          FilledButton(
              onPressed: () async {
                final res = await ApiClient.instance.dio.put(
                    '/api/config/settings/LicenseEndDate',
                    data: {'value': _endDate.text.trim()});
                if (context.mounted) showResult(context, res);
                await _load();
              },
              child: const Text('Save License Date')),
          OutlinedButton(onPressed: _load, child: const Text('Refresh Status')),
        ]),
        if (_message != null)
          Padding(
              padding: const EdgeInsets.only(top: 16), child: Text(_message!)),
      ]);
}
