import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import '../core/api_client.dart';
import '../providers/auth_provider.dart';

enum FieldType { text, number, decimal, mobile, email, dropdown, toggle, date }

class FieldSpec {
  final String key;
  final String label;
  final String? hint;
  final FieldType type;
  final bool required;
  final int? maxLength;
  final String? optionsEndpoint; // e.g. /api/zones -> items[]
  final String optionValueKey;
  final String optionLabelKey;
  final String? dependsOn; // reload options when this field changes (cascade)
  const FieldSpec(this.key, this.label,
      {this.hint,
      this.type = FieldType.text,
      this.required = true,
      this.maxLength,
      this.optionsEndpoint,
      this.optionValueKey = 'id',
      this.optionLabelKey = 'name',
      this.dependsOn});
}

class ColumnSpec {
  final String key;
  final String label;
  const ColumnSpec(this.key, this.label);
}

/// Generic Master CRUD screen: permission-aware buttons, search, active/inactive filter,
/// sortable grid, form dialog with validation + placeholders, success toast, auto refresh.
class MasterCrudScreen extends StatefulWidget {
  final String title;
  final String module; // permission module e.g. "Zone"
  final String endpoint; // e.g. /api/zones
  final List<FieldSpec> fields;
  final List<ColumnSpec> columns;
  const MasterCrudScreen(
      {super.key, required this.title, required this.module, required this.endpoint, required this.fields, required this.columns});

  @override
  State<MasterCrudScreen> createState() => _MasterCrudScreenState();
}

class _MasterCrudScreenState extends State<MasterCrudScreen> {
  List<Map<String, dynamic>> _items = [];
  bool _loading = true;
  bool _includeInactive = false;
  String _search = '';
  String _sortBy = 'id';
  bool _desc = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final res = await ApiClient.instance.dio.get(widget.endpoint, queryParameters: {
        if (_search.isNotEmpty) 'search': _search,
        'includeInactive': _includeInactive,
        'sortBy': _sortBy,
        'desc': _desc,
      });
      if (res.statusCode == 200) {
        final data = res.data is Map ? res.data['items'] : res.data;
        _items = List<Map<String, dynamic>>.from((data as List).map((e) => Map<String, dynamic>.from(e)));
      }
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  void _toast(String msg, {bool error = false}) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(msg),
        backgroundColor: error ? Theme.of(context).colorScheme.error : const Color(0xFF2E7D32)));
  }

  Future<void> _openForm({Map<String, dynamic>? existing}) async {
    final saved = await showDialog<bool>(
      context: context,
      builder: (_) => _MasterFormDialog(
          title: widget.title, endpoint: widget.endpoint, fields: widget.fields, existing: existing, onToast: _toast),
    );
    if (saved == true) _load();
  }

  Future<void> _delete(Map<String, dynamic> item) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: Text('Delete ${widget.title}?'),
        content: const Text('This is a soft delete - history is preserved. Continue?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Delete')),
        ],
      ),
    );
    if (ok != true) return;
    final res = await ApiClient.instance.dio.delete('${widget.endpoint}/${item['id']}');
    if (res.statusCode == 200) {
      _toast(res.data['message'] ?? 'Deleted.');
      _load();
    } else {
      _toast(ApiClient.errorMessage(res), error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final canCreate = auth.can('${widget.module}.Create');
    final canEdit = auth.can('${widget.module}.Edit');
    final canDelete = auth.can('${widget.module}.Delete');

    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Row(children: [
          Text(widget.title, style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          SizedBox(
            width: 260,
            child: TextField(
              decoration: const InputDecoration(
                  hintText: 'Search...', prefixIcon: Icon(Icons.search, size: 18), isDense: true),
              onChanged: (v) {
                _search = v;
                _load();
              },
            ),
          ),
          const SizedBox(width: 8),
          FilterChip(
              label: const Text('Show inactive'),
              selected: _includeInactive,
              onSelected: (v) {
                _includeInactive = v;
                _load();
              }),
          const SizedBox(width: 8),
          PopupMenuButton<String>(
            tooltip: 'Sort',
            icon: const Icon(Icons.sort),
            onSelected: (v) {
              if (v == _sortBy) {
                _desc = !_desc;
              } else {
                _sortBy = v;
                _desc = false;
              }
              _load();
            },
            itemBuilder: (_) => const [
              PopupMenuItem(value: 'id', child: Text('Sort by ID')),
              PopupMenuItem(value: 'name', child: Text('Sort by Name')),
            ],
          ),
          if (canCreate)
            FilledButton.icon(onPressed: () => _openForm(), icon: const Icon(Icons.add), label: const Text('New')),
        ]),
        const SizedBox(height: 10),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _items.isEmpty
                    ? Center(
                        child: Column(mainAxisSize: MainAxisSize.min, children: [
                        const Icon(Icons.inbox_outlined, size: 48),
                        const SizedBox(height: 8),
                        Text('No ${widget.title} records found.'),
                      ]))
                    : SingleChildScrollView(
                        scrollDirection: Axis.vertical,
                        child: SingleChildScrollView(
                          scrollDirection: Axis.horizontal,
                          child: DataTable(
                            columns: [
                              for (final c in widget.columns) DataColumn(label: Text(c.label)),
                              const DataColumn(label: Text('Status')),
                              if (canEdit || canDelete) const DataColumn(label: Text('Actions')),
                            ],
                            rows: [
                              for (final item in _items)
                                DataRow(cells: [
                                  for (final c in widget.columns)
                                    DataCell(Text('${item[c.key] ?? ''}', overflow: TextOverflow.ellipsis)),
                                  DataCell(Chip(
                                    label: Text(item['status'] == true ? 'Active' : 'Inactive',
                                        style: const TextStyle(fontSize: 11, color: Colors.white)),
                                    backgroundColor:
                                        item['status'] == true ? const Color(0xFF2E7D32) : Colors.grey,
                                    visualDensity: VisualDensity.compact,
                                  )),
                                  if (canEdit || canDelete)
                                    DataCell(Row(mainAxisSize: MainAxisSize.min, children: [
                                      if (canEdit)
                                        IconButton(
                                            tooltip: 'Edit',
                                            icon: const Icon(Icons.edit_outlined, size: 18),
                                            onPressed: () => _openForm(existing: item)),
                                      if (canDelete)
                                        IconButton(
                                            tooltip: 'Delete',
                                            icon: const Icon(Icons.delete_outline, size: 18),
                                            onPressed: () => _delete(item)),
                                    ])),
                                ]),
                            ],
                          ),
                        ),
                      ),
          ),
        ),
      ]),
    );
  }
}

class _MasterFormDialog extends StatefulWidget {
  final String title;
  final String endpoint;
  final List<FieldSpec> fields;
  final Map<String, dynamic>? existing;
  final void Function(String, {bool error}) onToast;
  const _MasterFormDialog(
      {required this.title, required this.endpoint, required this.fields, this.existing, required this.onToast});

  @override
  State<_MasterFormDialog> createState() => _MasterFormDialogState();
}

class _MasterFormDialogState extends State<_MasterFormDialog> {
  final _formKey = GlobalKey<FormState>();
  final Map<String, dynamic> _values = {};
  final Map<String, List<Map<String, dynamic>>> _options = {};
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    for (final f in widget.fields) {
      _values[f.key] = widget.existing?[f.key];
      if (f.type == FieldType.toggle) _values[f.key] = widget.existing?[f.key] ?? true;
    }
    _values['status'] = widget.existing?['status'] ?? true;
    for (final f in widget.fields.where((f) => f.optionsEndpoint != null)) {
      _loadOptions(f);
    }
  }

  Future<void> _loadOptions(FieldSpec f) async {
    var url = f.optionsEndpoint!;
    if (f.dependsOn != null) {
      final dep = _values[f.dependsOn];
      if (dep == null) {
        setState(() => _options[f.key] = []);
        return;
      }
      url = '$url/$dep';
    }
    try {
      final res = await ApiClient.instance.dio.get(url);
      if (res.statusCode == 200) {
        final data = res.data is Map ? res.data['items'] : res.data;
        setState(() => _options[f.key] =
            List<Map<String, dynamic>>.from((data as List).map((e) => Map<String, dynamic>.from(e))));
      }
    } catch (_) {}
  }

  Future<void> _save() async {
    if (!_formKey.currentState!.validate()) return;
    _formKey.currentState!.save();
    setState(() {
      _busy = true;
      _error = null;
    });
    final isEdit = widget.existing != null;
    final res = isEdit
        ? await ApiClient.instance.dio.put('${widget.endpoint}/${widget.existing!['id']}', data: _values)
        : await ApiClient.instance.dio.post(widget.endpoint, data: _values);
    setState(() => _busy = false);
    if (res.statusCode == 200) {
      widget.onToast(res.data['message'] ?? 'Saved successfully.');
      if (mounted) Navigator.pop(context, true);
    } else {
      setState(() => _error = ApiClient.errorMessage(res));
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text(widget.existing == null ? 'New ${widget.title}' : 'Edit ${widget.title}'),
      content: SizedBox(
        width: 440,
        child: Form(
          key: _formKey,
          child: SingleChildScrollView(
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              for (final f in widget.fields) ...[
                _buildField(f),
                const SizedBox(height: 12),
              ],
              if (widget.existing != null)
                SwitchListTile(
                  title: const Text('Active'),
                  value: _values['status'] == true,
                  onChanged: (v) => setState(() => _values['status'] = v),
                ),
              if (_error != null)
                Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ]),
          ),
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        FilledButton(onPressed: _busy ? null : _save, child: Text(_busy ? 'Saving...' : 'Save')),
      ],
    );
  }

  Widget _buildField(FieldSpec f) {
    switch (f.type) {
      case FieldType.dropdown:
        final opts = _options[f.key] ?? [];
        return DropdownButtonFormField<dynamic>(
          value: opts.any((o) => o[f.optionValueKey] == _values[f.key]) ? _values[f.key] : null,
          decoration: InputDecoration(labelText: f.label, hintText: f.hint ?? 'Select ${f.label}'),
          items: [
            for (final o in opts)
              DropdownMenuItem(value: o[f.optionValueKey], child: Text('${o[f.optionLabelKey]}')),
          ],
          validator: (v) => f.required && v == null ? '${f.label} is required' : null,
          onChanged: (v) {
            setState(() => _values[f.key] = v);
            for (final dep in widget.fields.where((x) => x.dependsOn == f.key)) {
              _values[dep.key] = null;
              _loadOptions(dep);
            }
          },
        );
      case FieldType.toggle:
        return SwitchListTile(
          title: Text(f.label),
          value: _values[f.key] == true,
          onChanged: (v) => setState(() => _values[f.key] = v),
        );
      default:
        return TextFormField(
          initialValue: _values[f.key]?.toString(),
          maxLength: f.maxLength ?? (f.type == FieldType.mobile ? 10 : null),
          keyboardType: f.type == FieldType.number || f.type == FieldType.mobile
              ? TextInputType.number
              : f.type == FieldType.decimal
                  ? const TextInputType.numberWithOptions(decimal: true)
                  : TextInputType.text,
          inputFormatters: [
            if (f.type == FieldType.number || f.type == FieldType.mobile)
              FilteringTextInputFormatter.digitsOnly,
            if (f.type == FieldType.decimal)
              FilteringTextInputFormatter.allow(RegExp(r'^\d*\.?\d{0,2}')),
          ],
          decoration: InputDecoration(labelText: f.label, hintText: f.hint, counterText: ''),
          validator: (v) {
            final val = (v ?? '').trim();
            if (f.required && val.isEmpty) return '${f.label} is required';
            if (f.type == FieldType.mobile && val.isNotEmpty && val.length != 10) {
              return 'Mobile must be exactly 10 digits';
            }
            if (f.type == FieldType.email &&
                val.isNotEmpty &&
                !RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$').hasMatch(val)) {
              return 'Invalid email format';
            }
            return null;
          },
          onSaved: (v) {
            final val = (v ?? '').trim();
            _values[f.key] = f.type == FieldType.number
                ? int.tryParse(val)
                : f.type == FieldType.decimal
                    ? double.tryParse(val)
                    : val.isEmpty
                        ? null
                        : val;
          },
        );
    }
  }
}
