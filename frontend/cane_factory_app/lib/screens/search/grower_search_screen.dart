import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../../core/api_client.dart';

/// Powerful Grower search: radio buttons for Name / Father Name / Village,
/// live search on text change, plus direct six-digit Grower ID lookup.
class GrowerSearchScreen extends StatefulWidget {
  const GrowerSearchScreen({super.key});
  @override
  State<GrowerSearchScreen> createState() => _GrowerSearchScreenState();
}

class _GrowerSearchScreenState extends State<GrowerSearchScreen> {
  String _searchBy = 'name';
  List _items = [];
  bool _loading = false;
  Map<String, dynamic>? _codeResult;
  String? _codeError;

  Future<void> _search(String text) async {
    if (text.trim().isEmpty) {
      setState(() => _items = []);
      return;
    }
    setState(() => _loading = true);
    try {
      final res = await ApiClient.instance.dio.get('/api/growers',
          queryParameters: {'search': text.trim(), 'searchBy': _searchBy});
      if (res.statusCode == 200) _items = res.data['items'];
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  Future<void> _lookupId(String text) async {
    setState(() {
      _codeResult = null;
      _codeError = null;
    });
    final id = int.tryParse(text.trim());
    if (id == null || id < 100001 || id > 999999) {
      setState(() =>
          _codeError = 'Enter a valid 6-digit Grower ID (for example 100001).');
      return;
    }
    final res = await ApiClient.instance.dio
        .get('/api/growers/by-id', queryParameters: {'id': id});
    setState(() {
      if (res.statusCode == 200) {
        _codeResult = Map<String, dynamic>.from(res.data);
      } else {
        _codeError = ApiClient.errorMessage(res);
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Text('Farmer / Grower Search',
            style: Theme.of(context)
                .textTheme
                .titleLarge
                ?.copyWith(fontWeight: FontWeight.w700)),
        const SizedBox(height: 10),
        Card(
          child: Padding(
            padding: const EdgeInsets.all(14),
            child:
                Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                const Text('Search by: ',
                    style: TextStyle(fontWeight: FontWeight.w600)),
                RadioGroup<String>(
                  groupValue: _searchBy,
                  onChanged: (v) => setState(() => _searchBy = v!),
                  child: Row(children: [
                    for (final opt in [
                      ('name', 'Name'),
                      ('father', 'Father Name'),
                      ('village', 'Village')
                    ])
                      Row(mainAxisSize: MainAxisSize.min, children: [
                        Radio<String>(value: opt.$1),
                        Text(opt.$2),
                      ]),
                  ]),
                ),
              ]),
              TextField(
                autofocus: true,
                decoration: const InputDecoration(
                    hintText: 'Search by name / father name / village...',
                    prefixIcon: Icon(Icons.search)),
                onChanged: _search,
              ),
              const SizedBox(height: 12),
              Row(children: [
                Expanded(
                  child: TextField(
                    decoration: const InputDecoration(
                        labelText: 'Direct Grower ID lookup',
                        hintText: 'Example: 100001',
                        prefixIcon: Icon(Icons.qr_code_2)),
                    keyboardType: TextInputType.number,
                    inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                    maxLength: 6,
                    onSubmitted: _lookupId,
                  ),
                ),
              ]),
              if (_codeResult != null)
                Container(
                  margin: const EdgeInsets.only(top: 10),
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                      color: const Color(0xFF2E7D32).withValues(alpha: 0.1),
                      borderRadius: BorderRadius.circular(8)),
                  child: Text(
                      '${_codeResult!['growerId']} — ${_codeResult!['growerName']} S/o ${_codeResult!['fatherName']}, '
                      'Village: ${_codeResult!['villageName']}, Mobile: ${_codeResult!['mobile']}, '
                      'Bank: ${_codeResult!['bankName'] ?? '-'} (${_codeResult!['accountMasked'] ?? '-'})'),
                ),
              if (_codeError != null)
                Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text(_codeError!,
                        style: TextStyle(
                            color: Theme.of(context).colorScheme.error))),
            ]),
          ),
        ),
        const SizedBox(height: 10),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _items.isEmpty
                    ? const Center(child: Text('Type to search growers...'))
                    : ListView.separated(
                        itemCount: _items.length,
                        separatorBuilder: (_, __) => const Divider(height: 1),
                        itemBuilder: (_, i) {
                          final g = _items[i];
                          return ListTile(
                            leading: CircleAvatar(
                                child: Text(
                                    '${g['id']}'.padLeft(6, '0').substring(4))),
                            title: Text('${g['id']} — ${g['growerName']}'),
                            subtitle: Text(
                                'S/o ${g['fatherName']} • Village: ${g['villageName']} • Mobile: ${g['mobile']}'),
                            trailing: Text('${g['bankName'] ?? ''}'),
                          );
                        },
                      ),
          ),
        ),
      ]),
    );
  }
}
