import 'package:flutter/material.dart';
import '../../core/api_client.dart';

/// Developer: Weighing Device configuration + String Profiles + Communication/Parser Test.
class DeviceConfigScreen extends StatefulWidget {
  const DeviceConfigScreen({super.key});
  @override
  State<DeviceConfigScreen> createState() => _DeviceConfigScreenState();
}

class _DeviceConfigScreenState extends State<DeviceConfigScreen> {
  List _devices = [];
  List _profiles = [];
  Map<String, dynamic>? _live;
  final _rawHex =
      TextEditingController(text: '02 20 30 30 31 35 30 30 03 0D 0A');
  int? _testProfileId;
  Map<String, dynamic>? _parseResult;
  final _simWeight = TextEditingController(text: '25000');
  String? _status;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final d = await ApiClient.instance.dio.get('/api/devices');
      final p = await ApiClient.instance.dio.get('/api/string-profiles');
      final w = await ApiClient.instance.dio.get('/api/devices/live-weight');
      if (!mounted) return;
      setState(() {
        if (d.statusCode == 200) _devices = d.data;
        if (p.statusCode == 200) _profiles = p.data;
        if (w.statusCode == 200) _live = Map<String, dynamic>.from(w.data);
        _testProfileId ??= _profiles.isNotEmpty ? _profiles.first['id'] : null;
      });
    } catch (_) {}
  }

  Future<void> _post(String path, [Map<String, dynamic>? data]) async {
    final res = await ApiClient.instance.dio.post(path, data: data ?? {});
    setState(() => _status = res.statusCode == 200
        ? res.data['message']
        : ApiClient.errorMessage(res));
    _load();
  }

  Future<void> _testParser() async {
    final res = await ApiClient.instance.dio.post('/api/devices/test-parser',
        data: {'stringProfileId': _testProfileId, 'rawHex': _rawHex.text});
    setState(() => _parseResult = res.statusCode == 200
        ? Map<String, dynamic>.from(res.data)
        : {'error': ApiClient.errorMessage(res)});
  }

  Future<void> _editDevice(Map<String, dynamic> d) async {
    final c = {
      for (final k in ['deviceName', 'manufacturer', 'modelNumber', 'comPort'])
        k: TextEditingController(text: '${d[k] ?? ''}')
    };
    int baud = d['baudRate'] ?? 2400;
    String parity = d['parity'] ?? 'None';
    int dataBits = d['dataBits'] ?? 8;
    int stopBits = d['stopBits'] ?? 1;
    String flow = d['flowControl'] ?? 'None';
    String connType = d['connectionType'] ?? 'RS232';
    int? profileId = d['activeStringProfileId'];
    bool enabled = d['isEnabled'] ?? true;

    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setD) => AlertDialog(
          title: Text('Device: ${d['deviceName']}'),
          content: ConstrainedBox(
            constraints: BoxConstraints(
              maxWidth: 500,
              maxHeight: MediaQuery.sizeOf(ctx).height * .62,
            ),
            child: SingleChildScrollView(
              child: Wrap(spacing: 10, runSpacing: 10, children: [
                for (final e in [
                  ('deviceName', 'Device Name'),
                  ('manufacturer', 'Manufacturer'),
                  ('modelNumber', 'Model'),
                  ('comPort', 'COM Port (Example: COM1)')
                ])
                  SizedBox(
                      width: 230,
                      child: TextField(
                          controller: c[e.$1],
                          decoration: InputDecoration(labelText: e.$2))),
                SizedBox(
                    width: 230,
                    child: DropdownButtonFormField<String>(
                        value: connType,
                        decoration:
                            const InputDecoration(labelText: 'Connection Type'),
                        items: const [
                          DropdownMenuItem(
                              value: 'RS232', child: Text('RS232 Serial')),
                          DropdownMenuItem(
                              value: 'USB', child: Text('USB-to-Serial')),
                          DropdownMenuItem(
                              value: 'TCPIP', child: Text('TCP/IP (future)'))
                        ],
                        onChanged: (v) => connType = v!)),
                SizedBox(
                    width: 230,
                    child: DropdownButtonFormField<int>(
                        value: baud,
                        decoration:
                            const InputDecoration(labelText: 'Baud Rate'),
                        items: [
                          for (final b in [
                            1200,
                            2400,
                            4800,
                            9600,
                            19200,
                            38400,
                            57600,
                            115200
                          ])
                            DropdownMenuItem(value: b, child: Text('$b'))
                        ],
                        onChanged: (v) => baud = v!)),
                SizedBox(
                    width: 230,
                    child: DropdownButtonFormField<String>(
                        value: parity,
                        decoration: const InputDecoration(labelText: 'Parity'),
                        items: const [
                          DropdownMenuItem(value: 'None', child: Text('None')),
                          DropdownMenuItem(value: 'Even', child: Text('Even')),
                          DropdownMenuItem(value: 'Odd', child: Text('Odd'))
                        ],
                        onChanged: (v) => parity = v!)),
                SizedBox(
                    width: 110,
                    child: DropdownButtonFormField<int>(
                        value: dataBits,
                        decoration:
                            const InputDecoration(labelText: 'Data Bits'),
                        items: const [
                          DropdownMenuItem(value: 7, child: Text('7')),
                          DropdownMenuItem(value: 8, child: Text('8'))
                        ],
                        onChanged: (v) => dataBits = v!)),
                SizedBox(
                    width: 110,
                    child: DropdownButtonFormField<int>(
                        value: stopBits,
                        decoration:
                            const InputDecoration(labelText: 'Stop Bits'),
                        items: const [
                          DropdownMenuItem(value: 1, child: Text('1')),
                          DropdownMenuItem(value: 2, child: Text('2'))
                        ],
                        onChanged: (v) => stopBits = v!)),
                SizedBox(
                    width: 230,
                    child: DropdownButtonFormField<String>(
                        value: flow,
                        decoration:
                            const InputDecoration(labelText: 'Flow Control'),
                        items: const [
                          DropdownMenuItem(value: 'None', child: Text('None')),
                          DropdownMenuItem(
                              value: 'XOnXOff', child: Text('XOn/XOff')),
                          DropdownMenuItem(value: 'RTS', child: Text('RTS'))
                        ],
                        onChanged: (v) => flow = v!)),
                SizedBox(
                    width: 230,
                    child: DropdownButtonFormField<int>(
                        value: profileId,
                        decoration: const InputDecoration(
                            labelText: 'Active String Profile'),
                        items: [
                          for (final p in _profiles)
                            DropdownMenuItem(
                                value: p['id'] as int,
                                child: Text(p['stringProfileName'],
                                    overflow: TextOverflow.ellipsis))
                        ],
                        onChanged: (v) => profileId = v)),
                SwitchListTile(
                    title: const Text('Enabled'),
                    value: enabled,
                    onChanged: (v) => setD(() => enabled = v)),
              ]),
            ),
          ),
          actions: [
            TextButton(
                onPressed: () => Navigator.pop(ctx, false),
                child: const Text('Cancel')),
            FilledButton(
                onPressed: () => Navigator.pop(ctx, true),
                child: const Text('Save Configuration')),
          ],
        ),
      ),
    );
    if (ok != true) return;
    final res =
        await ApiClient.instance.dio.put('/api/devices/${d['id']}', data: {
      'deviceName': c['deviceName']!.text,
      'manufacturer': c['manufacturer']!.text,
      'modelNumber': c['modelNumber']!.text,
      'connectionType': connType,
      'comPort': c['comPort']!.text,
      'baudRate': baud,
      'parity': parity,
      'dataBits': dataBits,
      'stopBits': stopBits,
      'flowControl': flow,
      'characterEncoding': d['characterEncoding'] ?? 'ASCII',
      'readTimeoutMs': d['readTimeoutMs'] ?? 1000,
      'readIntervalMs': d['readIntervalMs'] ?? 200,
      'autoReconnect': d['autoReconnect'] ?? true,
      'reconnectAttempts': d['reconnectAttempts'] ?? 5,
      'isEnabled': enabled,
      'activeStringProfileId': profileId,
    });
    setState(() => _status = res.statusCode == 200
        ? res.data['message']
        : ApiClient.errorMessage(res));
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return ListView(padding: const EdgeInsets.all(14), children: [
      Text('Weighing Device / Digitizer Configuration',
          style: Theme.of(context)
              .textTheme
              .titleLarge
              ?.copyWith(fontWeight: FontWeight.w700)),
      if (_status != null)
        Padding(
            padding: const EdgeInsets.only(top: 6),
            child: Text(_status!,
                style: const TextStyle(
                    color: Color(0xFF2E7D32), fontWeight: FontWeight.w600))),
      const SizedBox(height: 10),
      Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            const Text('Devices',
                style: TextStyle(fontWeight: FontWeight.w700)),
            for (final d in _devices)
              ListTile(
                dense: true,
                leading: Icon(Icons.scale,
                    color: d['activeConfiguration'] == true
                        ? const Color(0xFF2E7D32)
                        : Colors.grey),
                title: Text(
                    '${d['deviceName']}  •  ${d['comPort']} @ ${d['baudRate']} ${d['parity']}-${d['dataBits']}-${d['stopBits']}'),
                subtitle: Text(
                    'Profile: ${d['activeStringProfileName'] ?? '-'} • ${d['activeConfiguration'] == true ? 'ACTIVE' : 'inactive'} • ${d['isEnabled'] == true ? 'enabled' : 'disabled'}'),
                trailing: PopupMenuButton<String>(
                  tooltip: 'Device actions',
                  onSelected: (action) {
                    if (action == 'edit')
                      _editDevice(Map<String, dynamic>.from(d));
                    else
                      _post('/api/devices/${d['id']}/$action');
                  },
                  itemBuilder: (_) => const [
                    PopupMenuItem(
                        value: 'edit', child: Text('Edit configuration')),
                    PopupMenuItem(value: 'connect', child: Text('Connect')),
                    PopupMenuItem(value: 'activate', child: Text('Activate')),
                    PopupMenuItem(
                        value: 'deactivate', child: Text('Deactivate')),
                  ],
                ),
              ),
            Wrap(spacing: 8, runSpacing: 8, children: [
              FilledButton.tonal(
                  onPressed: () => _post('/api/devices/start-reading'),
                  child: const Text('Start Reading')),
              const SizedBox(width: 8),
              FilledButton.tonal(
                  onPressed: () => _post('/api/devices/stop-reading'),
                  child: const Text('Stop Reading')),
              const SizedBox(width: 8),
              FilledButton.tonal(
                  onPressed: () => _post('/api/devices/disconnect'),
                  child: const Text('Disconnect')),
            ]),
          ]),
        ),
      ),
      Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            const Text('Parser Test (verify before activation)',
                style: TextStyle(fontWeight: FontWeight.w700)),
            const SizedBox(height: 8),
            Wrap(
                spacing: 10,
                runSpacing: 10,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  SizedBox(
                    width: 280,
                    child: DropdownButtonFormField<int>(
                      value: _testProfileId,
                      decoration:
                          const InputDecoration(labelText: 'String Profile'),
                      items: [
                        for (final p in _profiles)
                          DropdownMenuItem(
                              value: p['id'] as int,
                              child: Text(p['stringProfileName'],
                                  overflow: TextOverflow.ellipsis))
                      ],
                      onChanged: (v) => setState(() => _testProfileId = v),
                    ),
                  ),
                  const SizedBox(width: 10),
                  SizedBox(
                    width: 420,
                    child: TextField(
                      controller: _rawHex,
                      decoration: const InputDecoration(
                          labelText: 'RAW HEX frame',
                          hintText:
                              'Example: 02 20 30 30 31 35 30 30 03 0D 0A'),
                    ),
                  ),
                  const SizedBox(width: 10),
                  FilledButton(
                      onPressed: _testParser, child: const Text('Test Parser')),
                ]),
            if (_parseResult != null)
              Container(
                margin: const EdgeInsets.only(top: 10),
                padding: const EdgeInsets.all(12),
                width: double.infinity,
                decoration: BoxDecoration(
                    color: Colors.black87,
                    borderRadius: BorderRadius.circular(8)),
                child: Text(
                  'RAW HEX: ${_parseResult!['rawHex'] ?? '-'}\n'
                  'SIGN = ${_parseResult!['sign'] ?? '-'}    WEIGHT = ${_parseResult!['weightCharacters'] ?? '-'}\n'
                  'NUMERIC WEIGHT = ${_parseResult!['numericWeight'] ?? '-'} ${_parseResult!['weightUnit'] ?? ''}\n'
                  'FRAME = ${_parseResult!['frameValid'] == true ? 'VALID' : 'INVALID'}'
                  '${_parseResult!['error'] != null ? '\nERROR: ${_parseResult!['error']}' : ''}',
                  style: const TextStyle(
                      fontFamily: 'monospace',
                      color: Color(0xFF7CFC8F),
                      fontSize: 13),
                ),
              ),
          ]),
        ),
      ),
      Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            const Text(
                'Weight Simulator (development/demo without physical indicator)',
                style: TextStyle(fontWeight: FontWeight.w700)),
            const SizedBox(height: 8),
            Wrap(
                spacing: 8,
                runSpacing: 8,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  SizedBox(
                    width: 180,
                    child: TextField(
                        controller: _simWeight,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(
                            labelText: 'Target weight (KG)',
                            hintText: 'Example: 25000')),
                  ),
                  const SizedBox(width: 8),
                  FilledButton.tonal(
                      onPressed: () => _post('/api/devices/simulator/start', {
                            'targetKg':
                                double.tryParse(_simWeight.text) ?? 25000
                          }),
                      child: const Text('Start Simulator')),
                  const SizedBox(width: 8),
                  FilledButton.tonal(
                      onPressed: () => _post(
                          '/api/devices/simulator/set-weight',
                          {'kg': double.tryParse(_simWeight.text) ?? 0}),
                      child: const Text('Set Exact Weight')),
                  const SizedBox(width: 8),
                  FilledButton.tonal(
                      onPressed: () => _post('/api/devices/simulator/stop'),
                      child: const Text('Stop')),
                ]),
            if (_live != null)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                    'Live: ${_live!['weightQuintal']} Qtl (${_live!['weightKg']} KG) • '
                    '${_live!['deviceConnected'] == true ? 'Connected to ${_live!['deviceName']}' : 'Disconnected'} • '
                    '${_live!['stable'] == true ? 'Stable' : 'Unstable'}'),
              ),
          ]),
        ),
      ),
      Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            const Text('String Profiles',
                style: TextStyle(fontWeight: FontWeight.w700)),
            for (final p in _profiles)
              ListTile(
                dense: true,
                leading: const Icon(Icons.data_object),
                title: Text(
                    '${p['stringProfileName']} (Type ${p['stringType']}, ${p['parserType']})'),
                subtitle: Text(
                    'STX ${p['startByte'] ?? '-'} • Sign@${p['signPosition'] ?? '-'} • Weight@${p['weightStartPosition']} len ${p['weightLength']} • ETX ${p['endByte'] ?? '-'} • ${p['weightUnit']}'),
                trailing: TextButton(
                    onPressed: () =>
                        _post('/api/string-profiles/${p['id']}/duplicate'),
                    child: const Text('Duplicate')),
              ),
            const Text(
                'Profiles are fully data-driven: adding another indicator model requires only a new profile - no code change.',
                style: TextStyle(fontSize: 11, fontStyle: FontStyle.italic)),
          ]),
        ),
      ),
    ]);
  }
}
