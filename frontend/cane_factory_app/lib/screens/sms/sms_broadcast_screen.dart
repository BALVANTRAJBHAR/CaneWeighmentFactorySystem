import 'package:flutter/material.dart';
import '../../core/api_client.dart';

/// One-off farmer SMS sender. Delivery is queued server-side; the SMS Logs tab
/// remains the source of truth for provider acceptance / failures.
class SmsBroadcastScreen extends StatefulWidget {
  const SmsBroadcastScreen({super.key});

  @override
  State<SmsBroadcastScreen> createState() => _SmsBroadcastScreenState();
}

class _SmsBroadcastScreenState extends State<SmsBroadcastScreen> {
  final _message = TextEditingController();
  final Set<int> _selected = {};
  List<Map<String, dynamic>> _growers = [];
  bool _loading = true;
  bool _sending = false;
  bool _sendToAll = true;
  String _search = '';

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _message.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    final res = await ApiClient.instance.dio.get('/api/sms-broadcast/growers');
    if (!mounted) return;
    if (res.statusCode == 200 && res.data is Map) {
      setState(() {
        _growers = (res.data['items'] as List? ?? [])
            .map((x) => Map<String, dynamic>.from(x as Map))
            .toList();
        _loading = false;
      });
    } else {
      setState(() => _loading = false);
      _notice(ApiClient.errorMessage(res), error: true);
    }
  }

  List<Map<String, dynamic>> get _filtered {
    final q = _search.trim().toLowerCase();
    if (q.isEmpty) return _growers;
    return _growers.where((g) => '${g['growerCode']} ${g['growerName']} ${g['fatherName']}'
        .toLowerCase().contains(q)).toList();
  }

  void _notice(String message, {bool error = false}) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(message),
      backgroundColor: error ? Theme.of(context).colorScheme.error : const Color(0xFF2E7D32),
    ));
  }

  Future<void> _send() async {
    if (_message.text.trim().isEmpty) {
      _notice('Please enter an SMS message.', error: true);
      return;
    }
    if (!_sendToAll && _selected.isEmpty) {
      _notice('Select at least one grower or choose All Active Growers.', error: true);
      return;
    }
    final target = _sendToAll ? '${_growers.length} active growers' : '${_selected.length} selected growers';
    final yes = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Queue SMS?'),
        content: Text('The message will be queued for $target. SMS Logs will show SENT or FAILED delivery status.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Queue SMS')),
        ],
      ),
    );
    if (yes != true) return;
    setState(() => _sending = true);
    final res = await ApiClient.instance.dio.post('/api/sms-broadcast/send', data: {
      'sendToAll': _sendToAll,
      'growerIds': _sendToAll ? null : _selected.toList(),
      'message': _message.text.trim(),
    });
    if (!mounted) return;
    setState(() => _sending = false);
    if (res.statusCode == 200) {
      _message.clear();
      _selected.clear();
      _notice(res.data['message']?.toString() ?? 'SMS queued.');
    } else {
      _notice(ApiClient.errorMessage(res), error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final visible = _filtered;
    return Column(children: [
      Padding(
        padding: const EdgeInsets.fromLTRB(18, 18, 18, 8),
        child: Row(children: [
          Expanded(child: Text('Send SMS to Growers', style: Theme.of(context).textTheme.headlineSmall)),
          IconButton(onPressed: _loading ? null : _load, icon: const Icon(Icons.refresh), tooltip: 'Refresh growers'),
        ]),
      ),
      Expanded(child: ListView(padding: const EdgeInsets.symmetric(horizontal: 18), children: [
        const Text('The SMS provider credentials and SMS Master switch are configured in Configuration → SMS. This page only queues messages; open SMS Logs to verify delivery.',
            style: TextStyle(fontSize: 12, fontStyle: FontStyle.italic)),
        const SizedBox(height: 14),
        SwitchListTile(
          contentPadding: EdgeInsets.zero,
          title: const Text('Send to All Active Growers'),
          subtitle: Text('${_growers.length} growers with a valid mobile number'),
          value: _sendToAll,
          onChanged: _loading ? null : (v) => setState(() => _sendToAll = v),
        ),
        if (!_sendToAll) ...[
          TextField(
            decoration: const InputDecoration(labelText: 'Search grower by code, name or father name', prefixIcon: Icon(Icons.search)),
            onChanged: (v) => setState(() => _search = v),
          ),
          const SizedBox(height: 8),
          Text('${_selected.length} selected', style: const TextStyle(fontWeight: FontWeight.w700)),
          if (_loading)
            const Padding(padding: EdgeInsets.all(24), child: Center(child: CircularProgressIndicator()))
          else
            SizedBox(height: 300, child: Card(child: ListView.builder(
              itemCount: visible.length,
              itemBuilder: (_, index) {
                final g = visible[index];
                final id = (g['id'] as num).toInt();
                return CheckboxListTile(
                  value: _selected.contains(id),
                  onChanged: (v) => setState(() => v == true ? _selected.add(id) : _selected.remove(id)),
                  title: Text('${g['growerCode']}  •  ${g['growerName']}'),
                  subtitle: Text('${g['fatherName'] ?? ''}  •  ${g['mobile']}'),
                );
              },
            ))),
        ],
        const SizedBox(height: 16),
        TextField(
          controller: _message,
          maxLength: 1000,
          minLines: 3,
          maxLines: 6,
          decoration: const InputDecoration(labelText: 'SMS Message', alignLabelWithHint: true),
        ),
        const SizedBox(height: 6),
        Align(alignment: Alignment.centerLeft, child: FilledButton.icon(
          onPressed: _sending || _loading ? null : _send,
          icon: const Icon(Icons.sms_outlined),
          label: Text(_sending ? 'Queuing SMS...' : 'Queue SMS'),
        )),
        const SizedBox(height: 18),
      ])),
    ]);
  }
}
