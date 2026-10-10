import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/api_client.dart';

class AuditScreen extends StatefulWidget {
  const AuditScreen({super.key});
  @override
  State<AuditScreen> createState() => _AuditScreenState();
}

class _AuditScreenState extends State<AuditScreen> {
  List _items = [];
  bool _loading = true;
  String _module = '';

  String _localTimestamp(dynamic value) {
    final raw = value?.toString();
    if (raw == null || raw.isEmpty) return '-';
    final parsed = DateTime.tryParse(raw);
    if (parsed == null) return raw;
    // Older API versions returned SQL UTC values without a trailing Z. Treat
    // those legacy/unspecified values as UTC as well, then show device-local time.
    final utc = parsed.isUtc
        ? parsed
        : DateTime.utc(
            parsed.year,
            parsed.month,
            parsed.day,
            parsed.hour,
            parsed.minute,
            parsed.second,
            parsed.millisecond,
            parsed.microsecond);
    return DateFormat('dd-MM-yyyy HH:mm:ss').format(utc.toLocal());
  }

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final res = await ApiClient.instance.dio.get('/api/audit',
          queryParameters: {if (_module.isNotEmpty) 'module': _module});
      if (res.statusCode == 200) _items = res.data['items'];
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Row(children: [
          Text('Audit Log',
              style: Theme.of(context)
                  .textTheme
                  .titleLarge
                  ?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          SizedBox(
            width: 200,
            child: TextField(
              decoration: const InputDecoration(
                  hintText: 'Filter by module...',
                  prefixIcon: Icon(Icons.filter_alt_outlined, size: 18)),
              onSubmitted: (v) {
                _module = v.trim();
                _load();
              },
            ),
          ),
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ]),
        const SizedBox(height: 10),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : ListView.separated(
                    itemCount: _items.length,
                    separatorBuilder: (_, __) => const Divider(height: 1),
                    itemBuilder: (_, i) {
                      final a = _items[i];
                      return ListTile(
                        dense: true,
                        leading: Icon(
                            a['success'] == true
                                ? Icons.check_circle_outline
                                : Icons.error_outline,
                            color: a['success'] == true
                                ? const Color(0xFF2E7D32)
                                : Colors.red,
                            size: 20),
                        title: Text(
                            '${a['action']} • ${a['module']} ${a['entity'] ?? ''} ${a['entityId'] ?? ''}',
                            style: const TextStyle(
                                fontSize: 13, fontWeight: FontWeight.w600)),
                        subtitle: Text(
                            'User: ${a['username'] ?? 'anonymous'} (${a['role'] ?? '-'}) • ${_localTimestamp(a['timestamp'])} • IP: ${a['ip'] ?? '-'}'
                            '${a['failureReason'] != null ? ' • ${a['failureReason']}' : ''}',
                            style: const TextStyle(fontSize: 11)),
                        onTap: (a['oldValue'] != null || a['newValue'] != null)
                            ? () => showDialog(
                                context: context,
                                builder: (_) => AlertDialog(
                                      title: const Text('Change Details'),
                                      content: SingleChildScrollView(
                                          child: Text(
                                              'OLD: ${a['oldValue'] ?? '-'}\n\nNEW: ${a['newValue'] ?? '-'}')),
                                    ))
                            : null,
                      );
                    },
                  ),
          ),
        ),
      ]),
    );
  }
}
