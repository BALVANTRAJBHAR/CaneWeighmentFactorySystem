import 'dart:convert';
import 'dart:typed_data';
import 'package:flutter/material.dart';
import 'package:dio/dio.dart';
import '../../core/api_client.dart';

/// Developer Configuration hub: Weight Rules, Sound/TTS, Cameras, Print, SMS, Razorpay, Company.
class DeveloperSettingsScreen extends StatelessWidget {
  const DeveloperSettingsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 7,
      child: Column(children: [
        Material(
          color: Theme.of(context).colorScheme.surface,
          child: const TabBar(isScrollable: true, tabs: [
            Tab(text: 'Weight Rules'),
            Tab(text: 'Sound / TTS'),
            Tab(text: 'Cameras'),
            Tab(text: 'Print'),
            Tab(text: 'SMS'),
            Tab(text: 'Razorpay'),
            Tab(text: 'Company'),
          ]),
        ),
        const Expanded(
          child: TabBarView(children: [
            _WeightRulesTab(),
            _SoundTab(),
            _CamerasTab(),
            _PrintTab(),
            _SmsTab(),
            _RazorpayTab(),
            _CompanyTab(),
          ]),
        ),
      ]),
    );
  }
}

void showResult(BuildContext context, dynamic res) {
  final ok = res.statusCode == 200;
  ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(ok ? res.data['message'] ?? 'Saved.' : ApiClient.errorMessage(res)),
      backgroundColor: ok ? const Color(0xFF2E7D32) : Theme.of(context).colorScheme.error));
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
    final res = await ApiClient.instance.dio.put('/api/config/weight-rules', data: {
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
        SizedBox(width: 220, child: TextField(controller: _min, decoration: const InputDecoration(labelText: 'Minimum Weight (Quintal)', hintText: 'Example: 10.00'))),
        SizedBox(width: 220, child: TextField(controller: _cut, decoration: const InputDecoration(labelText: 'Default Cutting %', hintText: 'Example: 2.00'))),
        SizedBox(width: 220, child: TextField(controller: _tax, decoration: const InputDecoration(labelText: 'Default Tax %', hintText: 'Example: 0.00'))),
      ]),
      for (final e in [('enabled', 'Minimum Weight Rule Enabled'), ('applyToCanePurchase', 'Apply to Cane Purchase'), ('applyToSalePurchase', 'Apply to SalePurchase'), ('applyToGross', 'Apply to Gross'), ('applyToTare', 'Apply to Tare')])
        SwitchListTile(title: Text(e.$2), value: v[e.$1] == true, onChanged: (x) => setState(() => v[e.$1] = x)),
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
    final res = await ApiClient.instance.dio.put('/api/config/sound', data: cfg);
    if (mounted) showResult(context, res);
  }

  @override
  Widget build(BuildContext context) {
    if (cfg == null) return const Center(child: CircularProgressIndicator());
    return ListView(padding: const EdgeInsets.all(16), children: [
      SwitchListTile(title: const Text('Sound Enabled'), value: cfg!['soundEnabled'] == true, onChanged: (v) => setState(() => cfg!['soundEnabled'] = v)),
      Wrap(spacing: 14, runSpacing: 14, children: [
        SizedBox(width: 200, child: DropdownButtonFormField<String>(value: cfg!['language'],
            decoration: const InputDecoration(labelText: 'Language'),
            items: const [DropdownMenuItem(value: 'hi', child: Text('Hindi')), DropdownMenuItem(value: 'en', child: Text('English'))],
            onChanged: (v) => setState(() => cfg!['language'] = v))),
        SizedBox(width: 200, child: DropdownButtonFormField<String>(value: cfg!['repeatMode'],
            decoration: const InputDecoration(labelText: 'Repeat Mode'),
            items: [for (final m in ['OFF', 'ONCE', 'TWICE', 'CONTINUOUS']) DropdownMenuItem(value: m, child: Text(m))],
            onChanged: (v) => setState(() => cfg!['repeatMode'] = v))),
        SizedBox(width: 200, child: DropdownButtonFormField<int>(value: cfg!['repeatIntervalSeconds'],
            decoration: const InputDecoration(labelText: 'Repeat Interval (seconds)'),
            items: [for (final s in [5, 10, 30, 60]) DropdownMenuItem(value: s, child: Text('$s seconds'))],
            onChanged: (v) => setState(() => cfg!['repeatIntervalSeconds'] = v))),
        SizedBox(width: 200, child: TextField(
            controller: TextEditingController(text: '${cfg!['voiceVolume']}'),
            decoration: const InputDecoration(labelText: 'Voice Volume (0-100)'),
            onChanged: (v) => cfg!['voiceVolume'] = int.tryParse(v) ?? 100)),
      ]),
      const SizedBox(height: 10),
      FilledButton(onPressed: _save, child: const Text('Save Sound Configuration')),
      const Divider(height: 30),
      const Text('Announcement Messages (editable per event & language)', style: TextStyle(fontWeight: FontWeight.w700)),
      for (final m in messages)
        ListTile(
          dense: true,
          title: Text('${m['eventCode']} (${m['languageCode']})', style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
          subtitle: Text('${m['messageText']}'),
          trailing: IconButton(
            icon: const Icon(Icons.edit_outlined, size: 18),
            onPressed: () async {
              final ctl = TextEditingController(text: m['messageText']);
              final ok = await showDialog<bool>(context: context, builder: (ctx) => AlertDialog(
                title: Text('Edit ${m['eventCode']} (${m['languageCode']})'),
                content: TextField(controller: ctl, maxLines: 3),
                actions: [
                  TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
                  FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Save')),
                ]));
              if (ok == true) {
                final res = await ApiClient.instance.dio.put('/api/config/sound/messages/${m['id']}',
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
    final c = {
      for (final k in ['ipAddress', 'username', 'password', 'model', 'rtspUrl'])
        k: TextEditingController(text: '${cam?[k] ?? ''}'.replaceAll('null', ''))
    };
    int number = cam?['cameraNumber'] ?? (cams.length + 1);
    String vendor = cam?['vendor'] ?? 'Hikvision';
    String protocol = cam?['protocol'] ?? 'RTSP';
    int port = cam?['port'] ?? 554;
    bool capture = cam?['captureEnabled'] ?? true;
    bool liveView = cam?['liveViewEnabled'] ?? true;
    final ok = await showDialog<bool>(context: context, builder: (ctx) => StatefulBuilder(builder: (ctx, setD) => AlertDialog(
      title: Text('Camera ${number.toString().padLeft(2, '0')}'),
      content: SizedBox(width: 480, child: SingleChildScrollView(child: Wrap(spacing: 10, runSpacing: 10, children: [
        SizedBox(width: 140, child: DropdownButtonFormField<int>(value: number,
            decoration: const InputDecoration(labelText: 'Camera No (1-6)'),
            items: [for (var i = 1; i <= 6; i++) DropdownMenuItem(value: i, child: Text('Camera $i'))],
            onChanged: (v) => number = v!)),
        SizedBox(width: 160, child: DropdownButtonFormField<String>(value: vendor,
            decoration: const InputDecoration(labelText: 'Vendor'),
            items: [for (final v in ['Hikvision', 'CPPlus', 'Dahua', 'Uniview', 'GenericONVIF', 'GenericRTSP']) DropdownMenuItem(value: v, child: Text(v))],
            onChanged: (v) => vendor = v!)),
        SizedBox(width: 140, child: DropdownButtonFormField<String>(value: protocol,
            decoration: const InputDecoration(labelText: 'Protocol'),
            items: [for (final v in ['RTSP', 'ONVIF', 'ISAPI']) DropdownMenuItem(value: v, child: Text(v))],
            onChanged: (v) => protocol = v!)),
        SizedBox(width: 220, child: TextField(controller: c['model'], decoration: const InputDecoration(labelText: 'Model', hintText: 'Example: DS-2CD2046G2-IU'))),
        SizedBox(width: 220, child: TextField(controller: c['ipAddress'], decoration: const InputDecoration(labelText: 'IP Address', hintText: 'Example: 192.168.1.64'))),
        SizedBox(width: 120, child: TextField(controller: TextEditingController(text: '$port'), decoration: const InputDecoration(labelText: 'Port', hintText: '554'), onChanged: (v) => port = int.tryParse(v) ?? 554)),
        SizedBox(width: 220, child: TextField(controller: c['username'], decoration: const InputDecoration(labelText: 'Username', hintText: 'Example: admin'))),
        SizedBox(width: 220, child: TextField(controller: c['password'], obscureText: true, decoration: const InputDecoration(labelText: 'Password (stored encrypted)', hintText: 'Leave blank to keep existing'))),
        SizedBox(width: 460, child: TextField(controller: c['rtspUrl'], decoration: const InputDecoration(labelText: 'RTSP URL (optional override)', hintText: 'rtsp://user:pass@ip:554/Streaming/Channels/101'))),
        SwitchListTile(title: const Text('Capture Enabled'), value: capture, onChanged: (v) => setD(() => capture = v)),
        SwitchListTile(title: const Text('Live View Enabled'), value: liveView, onChanged: (v) => setD(() => liveView = v)),
      ]))),
      actions: [
        TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
        FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Save')),
      ])));
    if (ok != true) return;
    final res = await ApiClient.instance.dio.post('/api/config/cameras', data: {
      'cameraNumber': number, 'vendor': vendor, 'protocol': protocol,
      'model': c['model']!.text, 'ipAddress': c['ipAddress']!.text, 'port': port,
      'username': c['username']!.text, 'password': c['password']!.text.isEmpty ? null : c['password']!.text,
      'channel': cam?['channel'] ?? 1, 'streamType': cam?['streamType'] ?? 'Main',
      'rtspUrl': c['rtspUrl']!.text.isEmpty ? null : c['rtspUrl']!.text,
      'captureEnabled': capture, 'liveViewEnabled': liveView,
      'retentionDays': cam?['retentionDays'] ?? 365, 'status': true,
    });
    if (mounted) showResult(context, res);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return ListView(padding: const EdgeInsets.all(16), children: [
      Row(children: [
        const Text('IP Cameras (1-6, vendor-abstracted)', style: TextStyle(fontWeight: FontWeight.w700)),
        const Spacer(),
        FilledButton.icon(onPressed: () => _edit(), icon: const Icon(Icons.add), label: const Text('Add Camera')),
      ]),
      for (final cam in cams)
        Card(child: ListTile(
          leading: Icon(Icons.videocam_outlined, color: cam['status'] == true ? const Color(0xFF2E7D32) : Colors.grey),
          title: Text('Camera ${cam['cameraNumber'].toString().padLeft(2, '0')} • ${cam['vendor']} ${cam['model'] ?? ''}'),
          subtitle: Text('${cam['ipAddress']}:${cam['port']} (${cam['protocol']}) • Capture: ${cam['captureEnabled'] == true ? 'ON' : 'OFF'} • Live: ${cam['liveViewEnabled'] == true ? 'ON' : 'OFF'} • Password: ${cam['hasPassword'] == true ? 'set (encrypted)' : 'not set'}'),
          trailing: Wrap(children: [
            TextButton(onPressed: () async {
              final res = await ApiClient.instance.dio.post('/api/config/cameras/${cam['id']}/test');
              if (context.mounted) showResult(context, res);
            }, child: const Text('Test')),
            TextButton(onPressed: () => _snapshot(cam['id']), child: const Text('Snapshot')),
            TextButton(onPressed: () => _edit(Map<String, dynamic>.from(cam)), child: const Text('Edit')),
          ]),
        )),
    ]);
  }

  /// Live capture preview (Phase 6) - one real snapshot right now via RTSP/ONVIF/ISAPI (or the
  /// SIMULATOR provider when Camera:SimulatorMode=true), never saved as purchase evidence.
  Future<void> _snapshot(int cameraId) async {
    showDialog(context: context, barrierDismissible: false, builder: (_) => const Center(child: CircularProgressIndicator()));
    try {
      final res = await ApiClient.instance.dio.get('/api/config/cameras/$cameraId/snapshot',
          options: Options(responseType: ResponseType.bytes, validateStatus: (s) => s != null && s < 500));
      if (mounted) Navigator.of(context, rootNavigator: true).pop();
      if (!mounted) return;
      if (res.statusCode == 200) {
        await showDialog(context: context, builder: (ctx) => Dialog(
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              Image.memory(Uint8List.fromList(res.data as List<int>), width: 420),
              const SizedBox(height: 8),
              TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Close')),
            ]),
          ),
        ));
      } else {
        var message = 'Snapshot capture failed.';
        try {
          final decoded = jsonDecode(utf8.decode(res.data as List<int>)) as Map;
          if (decoded['message'] != null) message = decoded['message'].toString();
        } catch (_) {}
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
        }
      }
    } catch (e) {
      if (mounted) Navigator.of(context, rootNavigator: true).pop();
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Snapshot failed: $e')));
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

  @override
  void initState() {
    super.initState();
    ApiClient.instance.dio.get('/api/config/print').then((res) {
      if (res.statusCode == 200 && mounted) setState(() => v = Map<String, dynamic>.from(res.data));
    });
  }

  @override
  Widget build(BuildContext context) {
    if (v == null) return const Center(child: CircularProgressIndicator());
    return ListView(padding: const EdgeInsets.all(16), children: [
      Wrap(spacing: 14, runSpacing: 14, children: [
        SizedBox(width: 220, child: DropdownButtonFormField<String>(value: v!['printerType'],
            decoration: const InputDecoration(labelText: 'Printer Type'),
            items: const [DropdownMenuItem(value: 'DotMatrix', child: Text('Dot Matrix')), DropdownMenuItem(value: 'A4', child: Text('A4 Laser/Inkjet'))],
            onChanged: (x) => setState(() => v!['printerType'] = x))),
        SizedBox(width: 280, child: TextField(controller: TextEditingController(text: v!['printerName']),
            decoration: const InputDecoration(labelText: 'Printer Name', hintText: 'Example: TVS MSP 270 Classic Plus'),
            onChanged: (x) => v!['printerName'] = x)),
        SizedBox(width: 180, child: DropdownButtonFormField<String>(value: v!['language'],
            decoration: const InputDecoration(labelText: 'Slip Language'),
            items: const [DropdownMenuItem(value: 'hi', child: Text('Hindi (default)')), DropdownMenuItem(value: 'en', child: Text('English'))],
            onChanged: (x) => setState(() => v!['language'] = x))),
      ]),
      SwitchListTile(title: const Text('Auto Print after Save'), value: v!['autoPrint'] == true, onChanged: (x) => setState(() => v!['autoPrint'] = x)),
      Wrap(spacing: 14, runSpacing: 14, children: [
        for (final e in [('grossCopies', 'Gross Copies'), ('tareCopies', 'Tare Copies'), ('paymentCopies', 'Payment Copies'), ('loanCopies', 'Loan Copies'), ('salePurchaseCopies', 'SalePurchase Copies')])
          SizedBox(width: 170, child: DropdownButtonFormField<int>(value: v![e.$1],
              decoration: InputDecoration(labelText: e.$2),
              items: [for (var i = 0; i <= 5; i++) DropdownMenuItem(value: i, child: Text('$i'))],
              onChanged: (x) => setState(() => v![e.$1] = x))),
      ]),
      const SizedBox(height: 12),
      FilledButton(onPressed: () async {
        final res = await ApiClient.instance.dio.put('/api/config/print', data: v);
        if (context.mounted) showResult(context, res);
      }, child: const Text('Save Print Configuration')),
    ]);
  }
}

class _SmsTab extends StatefulWidget {
  const _SmsTab();
  @override
  State<_SmsTab> createState() => _SmsTabState();
}

class _SmsTabState extends State<_SmsTab> {
  final c = {for (final k in ['providerName', 'apiBaseUrl', 'apiKey', 'apiSecret', 'authorizationHeader', 'senderId', 'entityId']) k: TextEditingController()};
  String method = 'POST';
  bool enabled = false;
  String? info;

  @override
  void initState() {
    super.initState();
    ApiClient.instance.dio.get('/api/config/sms').then((res) {
      if (res.statusCode == 200 && res.data['config'] != null && mounted) {
        final v = res.data['config'];
        setState(() {
          c['providerName']!.text = v['providerName'] ?? '';
          c['apiBaseUrl']!.text = v['apiBaseUrl'] ?? '';
          c['authorizationHeader']!.text = v['authorizationHeader'] ?? '';
          c['senderId']!.text = v['senderId'] ?? '';
          c['entityId']!.text = v['entityId'] ?? '';
          method = v['httpMethod'] ?? 'POST';
          enabled = v['enabled'] == true;
          info = 'API Key: ${v['hasApiKey'] == true ? 'set (encrypted)' : 'not set'} • API Secret: ${v['hasApiSecret'] == true ? 'set (encrypted)' : 'not set'}';
        });
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return ListView(padding: const EdgeInsets.all(16), children: [
      const Text('Generic DLT-compatible HTTP SMS provider. Secrets are encrypted server-side and never sent to any client.',
          style: TextStyle(fontSize: 12, fontStyle: FontStyle.italic)),
      if (info != null) Padding(padding: const EdgeInsets.only(top: 4), child: Text(info!, style: const TextStyle(fontSize: 12))),
      const SizedBox(height: 10),
      Wrap(spacing: 14, runSpacing: 14, children: [
        SizedBox(width: 240, child: TextField(controller: c['providerName'], decoration: const InputDecoration(labelText: 'Provider Name', hintText: 'Example: MSG91 / Fast2SMS / custom'))),
        SizedBox(width: 340, child: TextField(controller: c['apiBaseUrl'], decoration: const InputDecoration(labelText: 'API Base URL', hintText: 'https://api.provider.com/send'))),
        SizedBox(width: 140, child: DropdownButtonFormField<String>(value: method,
            decoration: const InputDecoration(labelText: 'HTTP Method'),
            items: const [DropdownMenuItem(value: 'POST', child: Text('POST')), DropdownMenuItem(value: 'GET', child: Text('GET'))],
            onChanged: (v) => setState(() => method = v!))),
        SizedBox(width: 240, child: TextField(controller: c['apiKey'], obscureText: true, decoration: const InputDecoration(labelText: 'API Key', hintText: 'Leave blank to keep existing'))),
        SizedBox(width: 240, child: TextField(controller: c['apiSecret'], obscureText: true, decoration: const InputDecoration(labelText: 'API Secret', hintText: 'Leave blank to keep existing'))),
        SizedBox(width: 240, child: TextField(controller: c['authorizationHeader'], decoration: const InputDecoration(labelText: 'Authorization Header', hintText: 'Example: Bearer / authkey'))),
        SizedBox(width: 180, child: TextField(controller: c['senderId'], decoration: const InputDecoration(labelText: 'Sender ID', hintText: 'Example: FCTORY'))),
        SizedBox(width: 240, child: TextField(controller: c['entityId'], decoration: const InputDecoration(labelText: 'DLT Entity ID', hintText: 'Where required'))),
      ]),
      SwitchListTile(title: const Text('SMS Enabled'), value: enabled, onChanged: (v) => setState(() => enabled = v)),
      FilledButton(onPressed: () async {
        final res = await ApiClient.instance.dio.put('/api/config/sms', data: {
          'providerName': c['providerName']!.text, 'apiBaseUrl': c['apiBaseUrl']!.text, 'httpMethod': method,
          'apiKey': c['apiKey']!.text.isEmpty ? null : c['apiKey']!.text,
          'apiSecret': c['apiSecret']!.text.isEmpty ? null : c['apiSecret']!.text,
          'authorizationHeader': c['authorizationHeader']!.text, 'senderId': c['senderId']!.text,
          'entityId': c['entityId']!.text, 'enabled': enabled,
        });
        if (context.mounted) showResult(context, res);
      }, child: const Text('Save SMS Configuration')),
    ]);
  }
}

class _RazorpayTab extends StatefulWidget {
  const _RazorpayTab();
  @override
  State<_RazorpayTab> createState() => _RazorpayTabState();
}

class _RazorpayTabState extends State<_RazorpayTab> {
  final c = {for (final k in ['keyId', 'keySecret', 'webhookSecret', 'accountNumber']) k: TextEditingController()};
  String mode = 'Test';
  bool enabled = false;
  String? info;

  @override
  void initState() {
    super.initState();
    ApiClient.instance.dio.get('/api/config/razorpay').then((res) {
      if (res.statusCode == 200 && res.data != null && mounted) {
        final v = res.data;
        setState(() {
          mode = v['mode'] ?? 'Test';
          enabled = v['enabled'] == true;
          c['accountNumber']!.text = v['accountNumber'] ?? '';
          info = 'Key ID: ${v['hasKeyId'] == true ? 'set' : 'not set'} • Secret: ${v['hasKeySecret'] == true ? 'set' : 'not set'} • Webhook: ${v['hasWebhookSecret'] == true ? 'set' : 'not set'} (all encrypted)';
        });
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return ListView(padding: const EdgeInsets.all(16), children: [
      const Text('RazorpayX payout configuration. All calls are made server-side only; secrets never reach Flutter/Web/Mobile.',
          style: TextStyle(fontSize: 12, fontStyle: FontStyle.italic)),
      if (info != null) Padding(padding: const EdgeInsets.only(top: 4), child: Text(info!, style: const TextStyle(fontSize: 12))),
      const SizedBox(height: 10),
      Wrap(spacing: 14, runSpacing: 14, children: [
        SizedBox(width: 160, child: DropdownButtonFormField<String>(value: mode,
            decoration: const InputDecoration(labelText: 'Mode'),
            items: const [DropdownMenuItem(value: 'Test', child: Text('Test')), DropdownMenuItem(value: 'Live', child: Text('Live'))],
            onChanged: (v) => setState(() => mode = v!))),
        SizedBox(width: 260, child: TextField(controller: c['keyId'], obscureText: true, decoration: const InputDecoration(labelText: 'Key ID', hintText: 'rzp_test_... (blank keeps existing)'))),
        SizedBox(width: 260, child: TextField(controller: c['keySecret'], obscureText: true, decoration: const InputDecoration(labelText: 'Key Secret', hintText: 'blank keeps existing'))),
        SizedBox(width: 260, child: TextField(controller: c['webhookSecret'], obscureText: true, decoration: const InputDecoration(labelText: 'Webhook Secret', hintText: 'blank keeps existing'))),
        SizedBox(width: 260, child: TextField(controller: c['accountNumber'], decoration: const InputDecoration(labelText: 'RazorpayX Account Number', hintText: 'For payouts'))),
      ]),
      SwitchListTile(title: const Text('Razorpay Enabled'), value: enabled, onChanged: (v) => setState(() => enabled = v)),
      FilledButton(onPressed: () async {
        final res = await ApiClient.instance.dio.put('/api/config/razorpay', data: {
          'enabled': enabled, 'mode': mode,
          'keyId': c['keyId']!.text.isEmpty ? null : c['keyId']!.text,
          'keySecret': c['keySecret']!.text.isEmpty ? null : c['keySecret']!.text,
          'webhookSecret': c['webhookSecret']!.text.isEmpty ? null : c['webhookSecret']!.text,
          'accountNumber': c['accountNumber']!.text,
        });
        if (context.mounted) showResult(context, res);
      }, child: const Text('Save Razorpay Configuration')),
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

  @override
  void initState() {
    super.initState();
    ApiClient.instance.dio.get('/api/config/company').then((res) {
      if (res.statusCode == 200 && mounted) setState(() => v = Map<String, dynamic>.from(res.data));
    });
  }

  @override
  Widget build(BuildContext context) {
    if (v == null) return const Center(child: CircularProgressIndicator());
    return ListView(padding: const EdgeInsets.all(16), children: [
      TextField(controller: TextEditingController(text: v!['companyName']),
          decoration: const InputDecoration(labelText: 'Company Name', hintText: 'Printed on all slips/reports'),
          onChanged: (x) => v!['companyName'] = x),
      const SizedBox(height: 12),
      TextField(controller: TextEditingController(text: v!['address']),
          decoration: const InputDecoration(labelText: 'Address'), maxLines: 2,
          onChanged: (x) => v!['address'] = x),
      const SizedBox(height: 12),
      DropdownButtonFormField<String>(value: v!['defaultLanguage'],
          decoration: const InputDecoration(labelText: 'Default Language'),
          items: const [DropdownMenuItem(value: 'en', child: Text('English')), DropdownMenuItem(value: 'hi', child: Text('Hindi'))],
          onChanged: (x) => setState(() => v!['defaultLanguage'] = x)),
      const SizedBox(height: 12),
      FilledButton(onPressed: () async {
        final res = await ApiClient.instance.dio.put('/api/config/company', data: v);
        if (context.mounted) showResult(context, res);
      }, child: const Text('Save Company Configuration')),
    ]);
  }
}
