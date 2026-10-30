import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/hindi_transliteration.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/pinned_table_scroll.dart';

/// Grower Master: six-digit Grower ID, Aadhaar (masked, encrypted server-side),
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
  final ScrollController _verticalController = ScrollController();
  final ScrollController _horizontalController = ScrollController();

  @override
  void dispose() {
    _verticalController.dispose();
    _horizontalController.dispose();
    super.dispose();
  }

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final res = await ApiClient.instance.dio.get('/api/growers',
          queryParameters: {
            if (_search.isNotEmpty) 'search': _search,
            'includeInactive': true
          });
      if (res.statusCode == 200) _items = res.data['items'];
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  Future<void> _openForm({Map<String, dynamic>? existing}) async {
    var formData = existing;
    if (existing != null) {
      try {
        // The list intentionally contains a masked Aadhaar. Fetch the edit-only
        // model so account number, holder name and Aadhaar prefill correctly.
        final response =
            await ApiClient.instance.dio.get('/api/growers/${existing['id']}');
        if (response.statusCode != 200) {
          throw ApiClient.errorMessage(
              response, 'Grower details could not be loaded.');
        }
        formData = Map<String, dynamic>.from(response.data as Map);
      } catch (error) {
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(ApiClient.exceptionMessage(
              error, 'Grower details could not be loaded.')),
          backgroundColor: Theme.of(context).colorScheme.error,
        ));
        return;
      }
    }
    if (!mounted) return;
    final saved = await showDialog<bool>(
        context: context, builder: (_) => _GrowerForm(existing: formData));
    if (saved == true) _load();
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final canEdit = auth.hasRole('Developer') || auth.can('Grower.Edit');
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        LayoutBuilder(builder: (context, constraints) {
          final compact = constraints.maxWidth < 700;
          final title = Text('Grower Master',
              style: Theme.of(context)
                  .textTheme
                  .titleLarge
                  ?.copyWith(fontWeight: FontWeight.w700));
          final search = TextField(
            decoration: const InputDecoration(
                hintText: 'Search by ID / name / father / village...',
                prefixIcon: Icon(Icons.search, size: 18)),
            onChanged: (v) {
              _search = v;
              _load();
            },
          );
          final create = auth.can('Grower.Create')
              ? FilledButton.icon(
                  onPressed: () => _openForm(),
                  icon: const Icon(Icons.add),
                  label: const Text('New Grower'))
              : null;
          if (compact) {
            return Column(children: [
              Row(children: [
                Expanded(child: title),
                if (create != null) create,
              ]),
              const SizedBox(height: 10),
              search,
            ]);
          }
          return Row(children: [
            title,
            const Spacer(),
            SizedBox(width: 300, child: search),
            if (create != null) ...[
              const SizedBox(width: 8),
              create,
            ],
          ]);
        }),
        const SizedBox(height: 10),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : PinnedTableScroll(
                    verticalController: _verticalController,
                    horizontalController: _horizontalController,
                    minTableWidth: 1900,
                    child: DataTable(columns: const [
                      DataColumn(label: Text('Grower ID')),
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
                          DataCell(Text('${g['id']}',
                              style: const TextStyle(
                                  fontWeight: FontWeight.w700))),
                          DataCell(Text('${g['growerName']}',
                              overflow: TextOverflow.ellipsis)),
                          DataCell(Text('${g['fatherName']}')),
                          DataCell(Text('${g['villageName']}')),
                          DataCell(Text('${g['mobile']}')),
                          DataCell(Text(g['createdAt'] == null
                              ? '-'
                              : DateFormat('dd-MM-yyyy').format(
                                  DateTime.parse(g['createdAt'].toString())
                                      .toLocal()))),
                          DataCell(Text('${g['bankName'] ?? '-'}')),
                          DataCell(Text(
                              '${g['bankAccountNumber'] ?? g['accountMasked'] ?? '-'}')),
                          DataCell(Text('${g['aadhaarMasked'] ?? '-'}')),
                          DataCell(Chip(
                              label: Text(
                                  g['status'] == true ? 'Active' : 'Inactive',
                                  style: const TextStyle(
                                      fontSize: 10, color: Colors.white)),
                              backgroundColor: g['status'] == true
                                  ? const Color(0xFF2E7D32)
                                  : Colors.grey,
                              visualDensity: VisualDensity.compact)),
                          DataCell(
                              Row(mainAxisSize: MainAxisSize.min, children: [
                            if (canEdit)
                              TextButton.icon(
                                  icon:
                                      const Icon(Icons.edit_outlined, size: 18),
                                  label: const Text('Edit'),
                                  onPressed: () => _openForm(
                                      existing: Map<String, dynamic>.from(g))),
                          ])),
                        ]),
                    ]),
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
  final _bankIfsc = TextEditingController();
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
    for (final k in [
      'growerName',
      'growerNameHi',
      'fatherName',
      'fatherNameHi',
      'mobile',
      'email',
      'bankAccountNumber',
      'accountHolderName',
      'aadhaarNumber'
    ]) {
      _c[k] =
          TextEditingController(text: widget.existing?[k]?.toString() ?? '');
    }
    _villageId = widget.existing?['villageId'];
    _bankId = widget.existing?['bankId'];
    _bankIfsc.text = widget.existing?['bankIfsc']?.toString() ?? '';
    _status = widget.existing?['status'] ?? true;
    _loadRefs();
  }

  @override
  void dispose() {
    for (final controller in _c.values) {
      controller.dispose();
    }
    _bankIfsc.dispose();
    super.dispose();
  }

  void _syncBankIfsc() {
    final matches = _banks.where((bank) => bank['id'] == _bankId);
    _bankIfsc.text =
        matches.isEmpty ? '' : (matches.first['ifsc']?.toString() ?? '');
  }

  Future<void> _loadRefs() async {
    final v = await ApiClient.instance.dio.get('/api/villages');
    final b = await ApiClient.instance.dio.get('/api/banks');
    setState(() {
      if (v.statusCode == 200) _villages = v.data['items'];
      if (b.statusCode == 200) _banks = b.data['items'];
      _syncBankIfsc();
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
      'growerNameHi': _c['growerNameHi']!.text,
      'fatherName': _c['fatherName']!.text,
      'fatherNameHi': _c['fatherNameHi']!.text,
      'mobile': _c['mobile']!.text,
      'email': _c['email']!.text.isEmpty ? null : _c['email']!.text,
      'bankId': _bankId,
      'bankAccountNumber': _c['bankAccountNumber']!.text.isEmpty
          ? null
          : _c['bankAccountNumber']!.text,
      'accountHolderName': _c['accountHolderName']!.text.isEmpty
          ? null
          : _c['accountHolderName']!.text,
      'aadhaarNumber':
          _c['aadhaarNumber']!.text.isEmpty ? null : _c['aadhaarNumber']!.text,
      'status': _status,
      'acceptDuplicateWarning': acceptWarnings,
    };
    try {
      final res = widget.existing != null
          ? await ApiClient.instance.dio
              .put('/api/growers/${widget.existing!['id']}', data: data)
          : await ApiClient.instance.dio.post('/api/growers', data: data);
      if (!mounted) return;
      if (res.statusCode == 200) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
            content: Text(res.data['message']),
            backgroundColor: const Color(0xFF2E7D32)));
        Navigator.pop(context, true);
      } else if (res.statusCode == 422 &&
          res.data['requiresConfirmation'] == true) {
        setState(() => _warnings = List<String>.from(res.data['warnings']));
      } else {
        setState(() => _error = ApiClient.errorMessage(res));
      }
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = ApiClient.exceptionMessage(error,
            'Grower save timed out or the server could not complete the request. Check the API/SQL Server log, then try again.');
      });
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final mobile = MediaQuery.sizeOf(context).width < 600;
    return AlertDialog(
      insetPadding: EdgeInsets.symmetric(
          horizontal: mobile ? 12 : 40, vertical: mobile ? 20 : 40),
      title: Text(widget.existing == null
          ? 'New Grower'
          : 'Edit Grower ID ${widget.existing!['id']}'),
      content: SizedBox(
        width: mobile ? double.maxFinite : 480,
        height: MediaQuery.sizeOf(context).height * (mobile ? 0.68 : 0.62),
        child: Form(
          key: _formKey,
          child: SingleChildScrollView(
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              DropdownButtonFormField<int>(
                value: _villageId,
                isExpanded: true,
                decoration: const InputDecoration(
                    labelText: 'Village', hintText: 'Select Village'),
                items: [
                  for (final v in _villages)
                    DropdownMenuItem(
                        value: v['id'] as int,
                        child: Text('${v['id']} - ${v['villageName']}',
                            overflow: TextOverflow.ellipsis))
                ],
                validator: (v) => v == null ? 'Village is required' : null,
                onChanged: widget.existing == null
                    ? (v) => setState(() => _villageId = v)
                    : null,
              ),
              const SizedBox(height: 10),
              _bilingual('growerName', 'Grower Name', 'Example: Ramesh Kumar'),
              _bilingual('fatherName', 'Father Name', 'Example: Mahesh Kumar'),
              _text('mobile', 'Mobile', 'Example: 9876543210',
                  digits: true,
                  maxLen: 10,
                  validator: (v) => v!.length != 10
                      ? 'Mobile must be exactly 10 digits'
                      : null),
              _text('email', 'Email (optional)', 'Example: farmer@gmail.com',
                  required: false),
              DropdownButtonFormField<int>(
                value: _bankId,
                isExpanded: true,
                decoration: const InputDecoration(
                    labelText: 'Bank (optional)', hintText: 'Select Bank'),
                items: [
                  for (final b in _banks)
                    DropdownMenuItem(
                        value: b['id'] as int,
                        child: Text('${b['bankName']} - ${b['branchName']}',
                            overflow: TextOverflow.ellipsis))
                ],
                onChanged: (v) => setState(() {
                  _bankId = v;
                  _syncBankIfsc();
                }),
              ),
              const SizedBox(height: 10),
              if (widget.existing != null)
                Padding(
                  padding: const EdgeInsets.only(bottom: 10),
                  child: TextFormField(
                    controller: _bankIfsc,
                    readOnly: true,
                    decoration: const InputDecoration(
                      labelText: 'IFSC Code',
                      hintText: 'Selected bank IFSC',
                      prefixIcon: Icon(Icons.account_balance_outlined),
                    ),
                  ),
                ),
              _text('bankAccountNumber', 'Bank Account Number (optional)',
                  'Example: 123456789012',
                  required: false, digits: true, maxLen: 18),
              _text('accountHolderName', 'Account Holder Name (optional)',
                  'As per bank records',
                  required: false),
              _text('aadhaarNumber', 'Aadhaar Number',
                  'Example: 123412341234 (stored encrypted)',
                  required: false,
                  digits: true,
                  maxLen: 12,
                  validator: (v) =>
                      v!.isNotEmpty && v.length != 12 && !v.contains('X')
                          ? 'Aadhaar must be 12 digits'
                          : null),
              if (widget.existing != null)
                SwitchListTile(
                    title: const Text('Active'),
                    value: _status,
                    onChanged: (v) => setState(() => _status = v)),
              if (_warnings.isNotEmpty)
                Container(
                  margin: const EdgeInsets.only(top: 8),
                  padding: const EdgeInsets.all(10),
                  decoration: BoxDecoration(
                      color: Colors.orange.withValues(alpha: 0.15),
                      borderRadius: BorderRadius.circular(8)),
                  child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Text('Possible duplicate:',
                            style: TextStyle(fontWeight: FontWeight.w700)),
                        for (final w in _warnings) Text('• $w'),
                      ]),
                ),
              if (_error != null)
                Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text(_error!,
                        style: TextStyle(
                            color: Theme.of(context).colorScheme.error))),
            ]),
          ),
        ),
      ),
      actions: [
        TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel')),
        if (_warnings.isNotEmpty)
          FilledButton.tonal(
              onPressed: _busy ? null : () => _save(acceptWarnings: true),
              child: const Text('Save Anyway')),
        FilledButton(
            onPressed: _busy ? null : () => _save(),
            child: Text(_busy ? 'Saving...' : 'Save')),
      ],
    );
  }

  Widget _text(String key, String label, String hint,
      {bool required = true,
      bool digits = false,
      int? maxLen,
      String? Function(String?)? validator}) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: TextFormField(
        controller: _c[key],
        maxLength: maxLen,
        inputFormatters:
            digits ? [FilteringTextInputFormatter.digitsOnly] : null,
        decoration:
            InputDecoration(labelText: label, hintText: hint, counterText: ''),
        validator: (v) {
          if (required && (v == null || v.trim().isEmpty))
            return '$label is required';
          return validator?.call(v ?? '');
        },
      ),
    );
  }

  Widget _bilingual(String key, String label, String hint) {
    var auto = true;
    var manuallyEdited = false;
    void sync(String value) {
      if (!auto || manuallyEdited) return;
      final translated = HindiTransliterator.transliterate(value);
      _c['${key}Hi']!.value = TextEditingValue(
          text: translated,
          selection: TextSelection.collapsed(offset: translated.length));
    }

    return StatefulBuilder(
        builder: (context, setLocal) => LayoutBuilder(
              builder: (context, constraints) {
                final primary = TextFormField(
                    controller: _c[key],
                    onChanged: sync,
                    decoration: InputDecoration(
                        labelText: label, hintText: hint, counterText: ''),
                    validator: (v) => v == null || v.trim().isEmpty
                        ? '$label is required'
                        : null);
                final hindi = TextFormField(
                    controller: _c['${key}Hi'],
                    onChanged: (_) => manuallyEdited = true,
                    decoration: InputDecoration(
                        labelText: '$label (Hindi)',
                        suffixIcon: IconButton(
                            tooltip: auto
                                ? 'Switch to manual Hindi'
                                : 'Enable transliteration',
                            icon: Icon(Icons.translate,
                                color: auto
                                    ? Theme.of(context).colorScheme.primary
                                    : null),
                            onPressed: () => setLocal(() => auto = !auto))));
                return Padding(
                  padding: const EdgeInsets.only(bottom: 10),
                  child: constraints.maxWidth < 440
                      ? Column(children: [
                          primary,
                          const SizedBox(height: 10),
                          hindi,
                        ])
                      : Row(children: [
                          Expanded(child: primary),
                          const SizedBox(width: 10),
                          Expanded(child: hindi),
                        ]),
                );
              },
            ));
  }
}
