import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../../core/api_client.dart';

class AndroidSimGatewayScreen extends StatefulWidget {
  const AndroidSimGatewayScreen({super.key});

  @override
  State<AndroidSimGatewayScreen> createState() =>
      _AndroidSimGatewayScreenState();
}

class _AndroidSimGatewayScreenState extends State<AndroidSimGatewayScreen> {
  static const _channel =
      MethodChannel('com.affllp.canefactory/android_sim_gateway');
  final server = TextEditingController(text: ApiClient.baseUrl);
  final device = TextEditingController();
  final key = TextEditingController();
  final poll = TextEditingController(text: '5');
  String sim = 'DEFAULT';
  bool configured = false;
  bool running = false;
  bool busy = false;
  String status = 'Not configured';

  @override
  void initState() {
    super.initState();
    _loadStatus();
  }

  Future<void> _loadStatus() async {
    try {
      final raw = await _channel.invokeMapMethod<String, dynamic>('status');
      if (!mounted || raw == null) return;
      setState(() {
        configured = raw['configured'] == true;
        running = raw['running'] == true;
        if ((raw['serverUrl'] ?? '').toString().isNotEmpty) {
          server.text = raw['serverUrl'].toString();
        }
        device.text = raw['deviceId']?.toString() ?? '';
        sim = raw['simSlot']?.toString() ?? 'DEFAULT';
        poll.text = '${raw['pollIntervalSeconds'] ?? 5}';
        status = running
            ? 'Gateway ON'
            : (configured ? 'Configured / stopped' : 'Not configured');
      });
    } on PlatformException catch (e) {
      _message(e.message ?? 'Could not read gateway status.', false);
    }
  }

  Future<void> _configure() async {
    await _run(() async {
      final result =
          await _channel.invokeMapMethod<String, dynamic>('configure', {
        'serverUrl': server.text.trim(),
        'deviceId': device.text.trim(),
        'apiKey': key.text,
        'simSlot': sim,
        'pollIntervalSeconds': int.tryParse(poll.text) ?? 5,
      });
      key.clear();
      configured = true;
      status = result?['active'] == true
          ? 'Credentials valid / server Active'
          : 'Credentials valid / server Disabled';
      _message('Gateway credentials validated and saved securely.', true);
    });
  }

  Future<void> _test() async {
    await _run(() async {
      final result =
          await _channel.invokeMapMethod<String, dynamic>('testConnection');
      status = result?['active'] == true
          ? 'Connected / Active'
          : 'Connected / server Disabled';
      _message('Connection successful.', true);
    });
  }

  Future<void> _start() async {
    await _run(() async {
      final result = await _channel.invokeMapMethod<String, dynamic>('start');
      running = result?['running'] == true;
      status = result?['active'] == true
          ? 'Gateway ON / Active'
          : 'Gateway ON / server Disabled';
      _message(
          'Foreground gateway started. It will restart after phone reboot.',
          true);
    });
  }

  Future<void> _stop() async {
    await _run(() async {
      await _channel.invokeMethod('stop');
      running = false;
      status = 'Configured / stopped';
      _message('Gateway stopped. Boot auto-start is disabled.', true);
    });
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => busy = true);
    try {
      await action();
    } on PlatformException catch (e) {
      _message(e.message ?? 'Gateway operation failed.', false);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  void _message(String text, bool success) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(text),
      backgroundColor:
          success ? Colors.green : Theme.of(context).colorScheme.error,
    ));
  }

  @override
  Widget build(BuildContext context) {
    return ListView(padding: const EdgeInsets.all(18), children: [
      Text('Android SIM Gateway',
          style: Theme.of(context).textTheme.headlineSmall),
      const SizedBox(height: 6),
      const Text(
          'This phone sends queued factory SMS through its physical SIM. Start keeps a foreground notification visible; after reboot, Boot Receiver restarts it automatically.'),
      const SizedBox(height: 16),
      Card(
          child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(children: [
                TextField(
                    controller: server,
                    decoration: const InputDecoration(
                        labelText: 'Server API URL (HTTPS)',
                        hintText: 'https://factory.example.com')),
                const SizedBox(height: 12),
                TextField(
                    controller: device,
                    decoration: const InputDecoration(labelText: 'Device ID')),
                const SizedBox(height: 12),
                TextField(
                    controller: key,
                    obscureText: true,
                    decoration: const InputDecoration(
                        labelText: 'Device API Key',
                        hintText: 'Required only when configuring / rotating')),
                const SizedBox(height: 12),
                Row(children: [
                  Expanded(
                      child: DropdownButtonFormField<String>(
                          value: sim,
                          decoration:
                              const InputDecoration(labelText: 'SIM Slot'),
                          items: const [
                            DropdownMenuItem(
                                value: 'DEFAULT', child: Text('Default SIM')),
                            DropdownMenuItem(
                                value: 'SIM1', child: Text('SIM 1')),
                            DropdownMenuItem(
                                value: 'SIM2', child: Text('SIM 2')),
                          ],
                          onChanged:
                              busy ? null : (v) => setState(() => sim = v!))),
                  const SizedBox(width: 12),
                  Expanded(
                      child: TextField(
                          controller: poll,
                          keyboardType: TextInputType.number,
                          decoration: const InputDecoration(
                              labelText: 'Poll Interval Seconds'))),
                ]),
                const SizedBox(height: 14),
                Align(
                    alignment: Alignment.centerLeft,
                    child: Text('Status: $status',
                        style: const TextStyle(fontWeight: FontWeight.w700))),
                const SizedBox(height: 12),
                Wrap(spacing: 10, runSpacing: 10, children: [
                  FilledButton.icon(
                      onPressed: busy ? null : _configure,
                      icon: const Icon(Icons.save_outlined),
                      label: const Text('Validate & Save')),
                  OutlinedButton.icon(
                      onPressed: busy || !configured ? null : _test,
                      icon: const Icon(Icons.link),
                      label: const Text('Test Connection')),
                  FilledButton.tonalIcon(
                      onPressed: busy || !configured || running ? null : _start,
                      icon: const Icon(Icons.play_arrow),
                      label: const Text('Start Gateway')),
                  OutlinedButton.icon(
                      onPressed: busy || !running ? null : _stop,
                      icon: const Icon(Icons.stop),
                      label: const Text('Stop Gateway')),
                ]),
              ]))),
      const SizedBox(height: 12),
      const Text(
          'Android may ask for SMS, phone/SIM, and notification permissions on first start. Allow them, then tap Start Gateway again.'),
    ]);
  }
}
