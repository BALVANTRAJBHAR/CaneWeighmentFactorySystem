import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../providers/auth_provider.dart';

class ExpenseScreen extends StatefulWidget {
  const ExpenseScreen({super.key});

  @override
  State<ExpenseScreen> createState() => _ExpenseScreenState();
}

class _ExpenseScreenState extends State<ExpenseScreen> {
  final _dateFormat = DateFormat('dd-MM-yyyy');
  List<Map<String, dynamic>> _types = [];
  List<Map<String, dynamic>> _items = [];
  bool _loading = true;
  String? _error;
  Map<String, dynamic> _totals = {};

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final responses = await Future.wait([
        ApiClient.instance.dio.get('/api/expense-types'),
        ApiClient.instance.dio.get('/api/expenses'),
      ]);
      final typesData = responses[0].data is Map
          ? responses[0].data['items']
          : responses[0].data;
      final expenseData =
          responses[1].data is Map ? responses[1].data : <String, dynamic>{};
      _types = List<Map<String, dynamic>>.from(
          (typesData as List? ?? []).map((x) => Map<String, dynamic>.from(x)));
      _items = List<Map<String, dynamic>>.from(
          (expenseData['items'] as List? ?? [])
              .map((x) => Map<String, dynamic>.from(x)));
      _totals = Map<String, dynamic>.from(expenseData['totals'] ?? {});
    } catch (e) {
      _error = 'Could not load expenses: $e';
    }
    if (mounted) setState(() => _loading = false);
  }

  void _toast(String message, {bool error = false}) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(message),
      backgroundColor:
          error ? Theme.of(context).colorScheme.error : const Color(0xFF2E7D32),
    ));
  }

  Future<void> _openForm({Map<String, dynamic>? existing}) async {
    final saved = await showDialog<bool>(
      context: context,
      builder: (_) => _ExpenseFormDialog(
          types: _types, existing: existing, onToast: _toast),
    );
    if (saved == true) _load();
  }

  Future<void> _delete(Map<String, dynamic> item) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Delete Expense?'),
        content: const Text(
            'This is a soft delete and will be kept in audit history.'),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Cancel')),
          FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Delete')),
        ],
      ),
    );
    if (ok != true) return;
    final res =
        await ApiClient.instance.dio.delete('/api/expenses/${item['id']}');
    if (res.statusCode == 200) {
      _toast(res.data['message'] ?? 'Expense deleted.');
      _load();
    } else {
      _toast(ApiClient.errorMessage(res), error: true);
    }
  }

  String _date(dynamic value) {
    final parsed = DateTime.tryParse('$value');
    return parsed == null ? '-' : _dateFormat.format(parsed.toLocal());
  }

  String _money(dynamic value) =>
      (num.tryParse('$value') ?? 0).toStringAsFixed(2);

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final canCreate = auth.can('Expense.Create');
    final canEdit = auth.can('Expense.Edit');
    final canDelete = auth.can('Expense.Delete');
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Row(children: [
          Text('Expense Management',
              style: Theme.of(context)
                  .textTheme
                  .titleLarge
                  ?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          if (canCreate)
            FilledButton.icon(
                onPressed: () => _openForm(),
                icon: const Icon(Icons.add),
                label: const Text('Add Expense')),
          const SizedBox(width: 8),
          IconButton(
              onPressed: _load,
              tooltip: 'Refresh',
              icon: const Icon(Icons.refresh)),
        ]),
        const SizedBox(height: 10),
        if (_error != null)
          Text(_error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error)),
        if (_totals.isNotEmpty)
          Card(
            color: Theme.of(context).colorScheme.primaryContainer,
            child: Padding(
              padding: const EdgeInsets.all(10),
              child: Wrap(spacing: 24, runSpacing: 8, children: [
                Text('Records: ${_totals['totalCount'] ?? 0}',
                    style: const TextStyle(fontWeight: FontWeight.w600)),
                Text('Total Expense: ₹${_money(_totals['totalAmount'])}',
                    style: const TextStyle(fontWeight: FontWeight.w700)),
              ]),
            ),
          ),
        const SizedBox(height: 8),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _items.isEmpty
                    ? const Center(child: Text('No expenses saved yet.'))
                    : SingleChildScrollView(
                        child: SingleChildScrollView(
                          scrollDirection: Axis.horizontal,
                          child: DataTable(
                            columns: [
                              const DataColumn(label: Text('Date')),
                              const DataColumn(label: Text('Expense Name')),
                              const DataColumn(label: Text('Quantity')),
                              const DataColumn(label: Text('Per Unit (₹)')),
                              const DataColumn(label: Text('Total (₹)')),
                              const DataColumn(label: Text('Remarks')),
                              if (canEdit || canDelete)
                                const DataColumn(label: Text('Actions')),
                            ],
                            rows: [
                              for (final item in _items)
                                DataRow(cells: [
                                  DataCell(Text(_date(item['expenseDate']))),
                                  DataCell(
                                      Text('${item['expenseName'] ?? ''}')),
                                  DataCell(Text('${item['quantity'] ?? ''}')),
                                  DataCell(Text(_money(item['unitCharge']))),
                                  DataCell(Text(_money(item['totalAmount']))),
                                  DataCell(Text('${item['remarks'] ?? '-'}')),
                                  if (canEdit || canDelete)
                                    DataCell(Row(
                                        mainAxisSize: MainAxisSize.min,
                                        children: [
                                          if (canEdit)
                                            IconButton(
                                                tooltip: 'Edit',
                                                icon: const Icon(
                                                    Icons.edit_outlined,
                                                    size: 18),
                                                onPressed: () =>
                                                    _openForm(existing: item)),
                                          if (canDelete)
                                            IconButton(
                                                tooltip: 'Delete',
                                                icon: const Icon(
                                                    Icons.delete_outline,
                                                    size: 18),
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

class _ExpenseFormDialog extends StatefulWidget {
  final List<Map<String, dynamic>> types;
  final Map<String, dynamic>? existing;
  final void Function(String, {bool error}) onToast;
  const _ExpenseFormDialog(
      {required this.types, this.existing, required this.onToast});

  @override
  State<_ExpenseFormDialog> createState() => _ExpenseFormDialogState();
}

class _ExpenseFormDialogState extends State<_ExpenseFormDialog> {
  final _formKey = GlobalKey<FormState>();
  final _quantity = TextEditingController();
  final _unitCharge = TextEditingController();
  final _total = TextEditingController();
  final _remarks = TextEditingController();
  DateTime _date = DateTime.now();
  int? _typeId;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    final e = widget.existing;
    _typeId = e?['expenseTypeId'];
    _date =
        DateTime.tryParse('${e?['expenseDate']}')?.toLocal() ?? DateTime.now();
    _quantity.text = e?['quantity']?.toString() ?? '';
    _unitCharge.text = e?['unitCharge']?.toString() ?? '';
    _remarks.text = e?['remarks']?.toString() ?? '';
    _quantity.addListener(_calculate);
    _unitCharge.addListener(_calculate);
    _calculate();
  }

  @override
  void dispose() {
    _quantity.dispose();
    _unitCharge.dispose();
    _total.dispose();
    _remarks.dispose();
    super.dispose();
  }

  void _calculate() {
    final quantity = double.tryParse(_quantity.text) ?? 0;
    final charge = double.tryParse(_unitCharge.text) ?? 0;
    _total.text = (quantity * charge).toStringAsFixed(2);
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _date,
      firstDate: DateTime(2020),
      lastDate: DateTime(2035),
    );
    if (picked != null) setState(() => _date = picked);
  }

  Future<void> _save() async {
    if (!_formKey.currentState!.validate()) return;
    if (_typeId == null) return;
    setState(() => _saving = true);
    final payload = {
      'expenseTypeId': _typeId,
      'expenseDate': _date.toIso8601String(),
      'quantity': double.parse(_quantity.text),
      'unitCharge': double.parse(_unitCharge.text),
      'remarks': _remarks.text.trim().isEmpty ? null : _remarks.text.trim(),
    };
    final id = widget.existing?['id'];
    final res = id == null
        ? await ApiClient.instance.dio.post('/api/expenses', data: payload)
        : await ApiClient.instance.dio.put('/api/expenses/$id', data: payload);
    if (!mounted) return;
    setState(() => _saving = false);
    if (res.statusCode == 200) {
      widget.onToast(res.data['message'] ?? 'Expense saved successfully.');
      Navigator.pop(context, true);
    } else {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(ApiClient.errorMessage(res)),
          backgroundColor: Theme.of(context).colorScheme.error));
    }
  }

  @override
  Widget build(BuildContext context) {
    final df = DateFormat('dd-MM-yyyy');
    return AlertDialog(
      title: Text(widget.existing == null ? 'Add Expense' : 'Edit Expense'),
      content: SizedBox(
        width: 460,
        child: Form(
          key: _formKey,
          child: SingleChildScrollView(
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              DropdownButtonFormField<int>(
                value: widget.types.any((x) => x['id'] == _typeId)
                    ? _typeId
                    : null,
                decoration: const InputDecoration(labelText: 'Expense Name'),
                items: [
                  for (final type in widget.types)
                    DropdownMenuItem<int>(
                        value: type['id'],
                        child: Text('${type['expenseName'] ?? ''}')),
                ],
                validator: (v) => v == null ? 'Expense name is required' : null,
                onChanged: (v) => setState(() => _typeId = v),
              ),
              const SizedBox(height: 12),
              Align(
                alignment: Alignment.centerLeft,
                child: OutlinedButton.icon(
                    onPressed: _pickDate,
                    icon: const Icon(Icons.date_range),
                    label: Text(df.format(_date))),
              ),
              const SizedBox(height: 12),
              TextFormField(
                controller: _quantity,
                decoration:
                    const InputDecoration(labelText: 'Quantity / Unit Count'),
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
                inputFormatters: [
                  FilteringTextInputFormatter.allow(RegExp(r'^\d*\.?\d{0,3}'))
                ],
                validator: (v) => (double.tryParse(v ?? '') ?? 0) <= 0
                    ? 'Quantity must be greater than zero'
                    : null,
              ),
              const SizedBox(height: 12),
              TextFormField(
                controller: _unitCharge,
                decoration:
                    const InputDecoration(labelText: 'Per Unit Charge (₹)'),
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
                inputFormatters: [
                  FilteringTextInputFormatter.allow(RegExp(r'^\d*\.?\d{0,2}'))
                ],
                validator: (v) => (double.tryParse(v ?? '') ?? -1) < 0
                    ? 'Charge cannot be negative'
                    : null,
              ),
              const SizedBox(height: 12),
              TextFormField(
                controller: _total,
                readOnly: true,
                decoration: const InputDecoration(
                    labelText: 'Total Amount (₹)',
                    prefixIcon: Icon(Icons.calculate_outlined)),
              ),
              const SizedBox(height: 12),
              TextFormField(
                  controller: _remarks,
                  maxLength: 500,
                  decoration:
                      const InputDecoration(labelText: 'Remarks (optional)')),
            ]),
          ),
        ),
      ),
      actions: [
        TextButton(
            onPressed: _saving ? null : () => Navigator.pop(context, false),
            child: const Text('Cancel')),
        FilledButton(
            onPressed: _saving ? null : _save,
            child: Text(_saving ? 'Saving...' : 'Save Expense')),
      ],
    );
  }
}
