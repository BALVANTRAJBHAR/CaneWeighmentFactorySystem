import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../providers/auth_provider.dart';

/// Grower Master: per-village auto grower code (101/1), Aadhaar (masked, encrypted server-side),
/// bank linkage and duplicate warnings with explicit confirmation.
class GrowerScreen extends StatefulWidget {
  const GrowerScreen({super.key});
  @override
  State<GrowerScreen> createState() => _GrowerScreenState();
}

class _GrowerScreenState extends State<GrowerScreen> {
  List _items = [];
  bool _loading = true;
  String _search = '';

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final res = await ApiClient.instance.dio
          .get('/api/growers', queryParameters: {if (_search.isNotEmpty) 'search': _search, 'includeInactive': true});
      if (res.statusCode == 200) _items = res.data['items'];
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  Future<void> _openForm({Map<String, dynamic>? existing}) async {
    final saved = await showDialog<bool>(context: context, builder: (_) => _GrowerForm(existing: existing));
    if (saved == true) _load();
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Row(children: [
          Text('Grower Master', style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          SizedBox(
            width: 300,
            child: TextField(
              decoration: const InputDecoration(
                  hintText: 'Search by name / father name / village...', prefixIcon: Icon(Icons.search, size: 18)),
              onChanged: (v) {
                _search = v;
                _load();
              },
            ),
          ),
          const SizedBox(width: 8),
          if (auth.can('Grower.Create'))
            FilledButton.icon(onPressed: () => _openForm(), icon: const Icon(Icons.add), label: const Text('New Grower')),
        ]),
        const SizedBox(height: 10),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : SingleChildScrollView(
                    scrollDirection: Axis.vertical,
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(columns: const [
                        DataColumn(label: Text('Code')),
                        DataColumn(label: Text('Name')),
                        DataColumn(label: Text('Father Name')),
                        DataColumn(label: Text('Village')),
                        DataColumn(label: Text('Mobile')),
                        DataColumn(label: Text('Created Date')),
                        DataColumn(label: Text('Bank')),
                        DataColumn(label: Text('Account')),
                        DataColumn(label: Text('Aadhaar')),
                        DataColumn(label: Text('Status')),
                        DataColumn(label: Text('Actions')),
                      ], rows: [
                        for (final g in _items)
                          DataRow(cells: [
                            DataCell(Text('${g['growerCode']}', style: const TextStyle(fontWeight: FontWeight.w700))),
                            DataCell(Text('${g['growerName']}')),
                            DataCell(Text('${g['fatherName']}')),
                            DataCell(Text('${g['villageName']}')),
                            DataCell(Text('${g['mobile']}')),
                            DataCell(Text(g['createdAt'] == null ? '-' : DateFormat('dd-MM-yyyy').format(DateTime.parse(g['createdAt'].toString()).toLocal()))),
                            DataCell(Text('${g['bankName'] ?? '-'}')),
                            DataCell(Text('${g['accountMasked'] ?? '-'}')),
                            DataCell(Text('${g['aadhaarMasked'] ?? '-'}')),
                            DataCell(Chip(
                                label: Text(g['status'] == true ? 'Active' : 'Inactive',
                                    style: const TextStyle(fontSize: 10, color: Colors.white)),
                                backgroundColor: g['status'] == true ? const Color(0xFF2E7D32) : Colors.grey,
                                visualDensity: VisualDensity.compact)),
                            DataCell(Row(mainAxisSize: MainAxisSize.min, children: [
                              if (auth.can('Grower.Edit'))
                                IconButton(
                                    icon: const Icon(Icons.edit_outlined, size: 18),
                                    onPressed: () => _openForm(existing: Map<String, dynamic>.from(g))),
                            ])),
                          ]),
                      ]),
                    ),
                  ),
          ),
        ),
      ]),
    );
  }
}

class _GrowerForm extends StatefulWidget {
  final Map<String, dynamic>? existing;
  const _GrowerForm({this.existing});
  @override
  State<_GrowerForm> createState() => _GrowerFormState();
}

class _GrowerFormState extends State<_GrowerForm> {
  final _formKey = GlobalKey<FormState>();
  final Map<String, TextEditingController> _c = {};
  int? _villageId;
  int? _bankId;
  bool _status = true;
  List _villages = [];
  List _banks = [];
  String? _error;
  List<String> _warnings = [];
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    for (final k in ['growerName', 'fatherName', 'mobile', 'email', 'bankAccountNumber', 'accountHolderName', 'aadhaarNumber']) {
      _c[k] = TextEditingController(text: widget.existing?[k]?.toString() ?? '');
    }
    _villageId = widget.existing?['villageId'];
    _bankId = widget.existing?['bankId'];
    _status = widget.existing?['status'] ?? true;
    _loadRefs();
  }

  Future<void> _loadRefs() async {
    final v = await ApiClient.instance.dio.get('/api/villages');
    final b = await ApiClient.instance.dio.get('/api/banks');
    setState(() {
      if (v.statusCode == 200) _villages = v.data['items'];
      if (b.statusCode == 200) _banks = b.data['items'];
    });
  }

  Future<void> _save({bool acceptWarnings = false}) async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
      _warnings = [];
    });
    final data = {
      'villageId': _villageId,
      'growerName': _c['growerName']!.text,
      'fatherName': _c['fatherName']!.text,
      'mobile': _c['mobile']!.text,
      'email': _c['email']!.text.isEmpty ? null : _c['email']!.text,
      'bankId': _bankId,
      'bankAccountNumber': _c['bankAccountNumber']!.text.isEmpty ? null : _c['bankAccountNumber']!.text,
      'accountHolderName': _c['accountHolderName']!.text.isEmpty ? null : _c['accountHolderName']!.text,
      'aadhaarNumber': _c['aadhaarNumber']!.text.isEmpty ? null : _c['aadhaarNumber']!.text,
      'status': _status,
      'acceptDuplicateWarning': acceptWarnings,
    };
    final res = widget.existing != null
        ? await ApiClient.instance.dio.put('/api/growers/${widget.existing!['id']}', data: data)
        : await ApiClient.instance.dio.post('/api/growers', data: data);
    setState(() => _busy = false);
    if (res.statusCode == 200) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text(res.data['message']), backgroundColor: const Color(0xFF2E7D32)));
        Navigator.pop(context, true);
      }
    } else if (res.statusCode == 422 && res.data['requiresConfirmation'] == true) {
      setState(() => _warnings = List<String>.from(res.data['warnings']));
    } else {
      setState(() => _error = ApiClient.errorMessage(res));
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text(widget.existing == null ? 'New Grower' : 'Edit Grower ${widget.existing!['growerCode']}'),
      content: SizedBox(
        width: 480,
        child: Form(
          key: _formKey,
          child: SingleChildScrollView(
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              DropdownButtonFormField<int>(
                value: _villageId,
                decoration: const InputDecoration(labelText: 'Village', hintText: 'Select Village'),
                items: [for (final v in _villages) DropdownMenuItem(value: v['id'] as int, child: Text('${v['id']} - ${v['villageName']}'))],
                validator: (v) => v == null ? 'Village is required' : null,
                onChanged: widget.existing == null ? (v) => setState(() => _villageId = v) : null,
              ),
              const SizedBox(height: 10),
              _text('growerName', 'Grower Name', 'Example: Ramesh Kumar'),
              _text('fatherName', 'Father Name', 'Example: Mahesh Kumar'),
              _text('mobile', 'Mobile', 'Example: 9876543210', digits: true, maxLen: 10,
                  validator: (v) => v!.length != 10 ? 'Mobile must be exactly 10 digits' : null),
              _text('email', 'Email (optional)', 'Example: farmer@gmail.com', required: false),
              DropdownButtonFormField<int>(
                value: _bankId,
                decoration: const InputDecoration(labelText: 'Bank (optional)', hintText: 'Select Bank'),
                items: [for (final b in _banks) DropdownMenuItem(value: b['id'] as int, child: Text('${b['bankName']} - ${b['branchName']}'))],
                onChanged: (v) => setState(() => _bankId = v),
              ),
              const SizedBox(height: 10),
              _text('bankAccountNumber', 'Bank Account Number (optional)', 'Example: 123456789012', required: false, digits: true, maxLen: 18),
              _text('accountHolderName', 'Account Holder Name (optional)', 'As per bank records', required: false),
              _text('aadhaarNumber', 'Aadhaar Number', 'Example: 123412341234 (stored encrypted)',
                  required: false, digits: true, maxLen: 12,
                  validator: (v) => v!.isNotEmpty && v.length != 12 && !v.contains('X') ? 'Aadhaar must be 12 digits' : null),
              if (widget.existing != null)
                SwitchListTile(title: const Text('Active'), value: _status, onChanged: (v) => setState(() => _status = v)),
              if (_warnings.isNotEmpty)
                Container(
                  margin: const EdgeInsets.only(top: 8),
                  padding: const EdgeInsets.all(10),
                  decoration: BoxDecoration(color: Colors.orange.withValues(alpha: 0.15), borderRadius: BorderRadius.circular(8)),
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    const Text('Possible duplicate:', style: TextStyle(fontWeight: FontWeight.w700)),
                    for (final w in _warnings) Text('• $w'),
                  ]),
                ),
              if (_error != null)
                Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
            ]),
          ),
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        if (_warnings.isNotEmpty)
          FilledButton.tonal(
              onPressed: _busy ? null : () => _save(acceptWarnings: true),
              child: const Text('Save Anyway')),
        FilledButton(onPressed: _busy ? null : () => _save(), child: Text(_busy ? 'Saving...' : 'Save')),
      ],
    );
  }

  Widget _text(String key, String label, String hint,
      {bool required = true, bool digits = false, int? maxLen, String? Function(String?)? validator}) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: TextFormField(
        controller: _c[key],
        maxLength: maxLen,
        inputFormatters: digits ? [FilteringTextInputFormatter.digitsOnly] : null,
        decoration: InputDecoration(labelText: label, hintText: hint, counterText: ''),
        validator: (v) {
          if (required && (v == null || v.trim().isEmpty)) return '$label is required';
          return validator?.call(v ?? '');
        },
      ),
    );
  }
}
