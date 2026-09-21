import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_client.dart';
import '../../providers/auth_provider.dart';

class WeighmentCorrectionsScreen extends StatefulWidget {
  const WeighmentCorrectionsScreen({super.key, this.api});
  final Dio? api;

  @override
  State<WeighmentCorrectionsScreen> createState() =>
      _WeighmentCorrectionsScreenState();
}

class _WeighmentCorrectionsScreenState
    extends State<WeighmentCorrectionsScreen> {
  final _id = TextEditingController();
  final _vehicle = TextEditingController();
  String _kind = 'purchase';
  Map<String, dynamic>? _record, _preview;
  List<Map<String, dynamic>> _vehicles = [], _types = [], _varieties = [];
  int? _vehicleType, _varietyType, _variety;
  String? _message, _updater;
  bool _busy = false, _error = false;
  Dio get _api => widget.api ?? ApiClient.instance.dio;
  bool get _cane => _kind == 'purchase';
  bool get _allowed {
    final auth = context.read<AuthProvider>();
    return auth.hasRole('Admin') || auth.hasRole('Developer');
  }

  @override
  void dispose() {
    _id.dispose();
    _vehicle.dispose();
    super.dispose();
  }

  void _show(String message, {bool error = true}) {
    setState(() {
      _message = message;
      _error = error;
    });
  }

  Future<void> _handleError(Response response) async {
    final message = ApiClient.errorMessage(response);
    final code = response.data is Map ? response.data['code'] : null;
    if (code == 'PAYMENT_COMPLETED' || code == 'STALE_RECORD') {
      setState(() {
        _record = null;
        _preview = null;
      });
    }
    _show(message);
    if (code == 'PAYMENT_COMPLETED') {
      await showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Payment already completed'),
          content: Text(message),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('OK'),
            ),
          ],
        ),
      );
    }
  }

  Future<void> _search() async {
    if (!_allowed || _busy) return;
    final id = int.tryParse(_id.text.trim());
    if (id == null || id <= 0) {
      _show('Enter a valid ID greater than zero.');
      return;
    }
    setState(() {
      _busy = true;
      _record = null;
      _preview = null;
      _message = null;
    });
    try {
      final response = await _api.get('/api/weighment-corrections/$_kind/$id');
      if (!mounted) return;
      if (response.statusCode != 200) {
        await _handleError(response);
        return;
      }
      final data = Map<String, dynamic>.from(response.data as Map);
      setState(() {
        _record = Map<String, dynamic>.from(data['record'] as Map);
        _vehicles = List<Map<String, dynamic>>.from(
          (data['vehicleTypes'] as List).map(
            (x) => Map<String, dynamic>.from(x),
          ),
        );
        _types = List<Map<String, dynamic>>.from(
          (data['varietyTypes'] as List).map(
            (x) => Map<String, dynamic>.from(x),
          ),
        );
        _varieties = List<Map<String, dynamic>>.from(
          (data['varieties'] as List).map((x) => Map<String, dynamic>.from(x)),
        );
        _vehicleType = _record!['vehicleTypeId'];
        _varietyType = _record!['varietyTypeId'];
        _variety = _record!['varietyId'];
        _vehicle.text = _record!['vehicleNumber'] ?? '';
        _updater = data['updatedByName'];
      });
    } catch (error) {
      if (mounted) _show(ApiClient.exceptionMessage(error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Map<String, dynamic> _payload() => {
        'vehicleTypeId': _vehicleType,
        'vehicleNumber': _vehicle.text,
        'revision': _record!['revision'],
        if (_cane) 'varietyTypeId': _varietyType,
        if (_cane) 'varietyId': _variety,
        if (_cane) 'expectedRate': _preview?['rate'],
      };

  Future<void> _calculateOrSave(bool save) async {
    if (!_allowed || _busy || _record == null) return;
    if (_vehicleType == null ||
        (_cane && (_varietyType == null || _variety == null)) ||
        _vehicle.text.trim().isEmpty) {
      _show('Select all required fields and enter the vehicle number.');
      return;
    }
    if (save && _preview == null) {
      _show('Calculate / Preview before updating.');
      return;
    }
    setState(() {
      _busy = true;
      _message = null;
    });
    try {
      if (save) {
        final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) => AlertDialog(
            title: Text(
              'Update ${_cane ? 'purchase' : 'sale'} #${_record!['id']}?',
            ),
            content: Text(
              'Vehicle: ${_preview!['vehicleNumber']}\nRate: ${_money(_preview!['rate'])}\nAmount: ${_money(_preview!['amount'])}\n\nMeasured weights will not change. Your user and update time will be recorded.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(context, false),
                child: const Text('Cancel'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(context, true),
                child: const Text('Confirm Update'),
              ),
            ],
          ),
        );
        if (confirmed != true || !mounted) return;
      }
      final url = '/api/weighment-corrections/$_kind/${_record!['id']}';
      final response = save
          ? await _api.put(url, data: _payload())
          : await _api.post('$url/preview', data: _payload());
      if (!mounted) return;
      if (response.statusCode != 200) {
        setState(() => _preview = null);
        await _handleError(response);
        return;
      }
      final data = Map<String, dynamic>.from(response.data as Map);
      setState(() {
        if (save) {
          _record = {
            ..._record!,
            ...data,
            'vehicleTypeId': _vehicleType,
            if (_cane) 'varietyTypeId': _varietyType,
            if (_cane) 'varietyId': _variety,
          };
          _vehicle.text = data['vehicleNumber'];
          _updater = data['updatedByName'];
          _preview = null;
        } else {
          _preview = data;
        }
      });
      _show(data['message'], error: false);
    } catch (error) {
      if (mounted) {
        setState(() => _preview = null);
        _show(ApiClient.exceptionMessage(error));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  String _money(dynamic value) => value is num ? value.toStringAsFixed(2) : '-';
  String _selectedVarietyTypeName() {
    for (final type in _types) {
      if (type['id'] == _varietyType) {
        return type['name']?.toString() ?? 'selected';
      }
    }
    return 'selected';
  }

  String _date(dynamic value) {
    final date = DateTime.tryParse(value?.toString() ?? '');
    return date == null
        ? '-'
        : DateFormat('dd-MM-yyyy HH:mm:ss').format(date.toLocal());
  }

  Widget _detailCell(String text, {required bool header}) => Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        child: Text(
          text,
          overflow: TextOverflow.ellipsis,
          maxLines: 2,
          style: header
              ? const TextStyle(fontWeight: FontWeight.w700)
              : const TextStyle(fontSize: 15),
        ),
      );

  TableRow _detailRow(String firstLabel, String firstValue, String secondLabel,
          String secondValue) =>
      TableRow(children: [
        _detailCell(firstLabel, header: true),
        _detailCell(firstValue, header: false),
        _detailCell(secondLabel, header: true),
        _detailCell(secondValue, header: false),
      ]);

  Widget _detailsTable(Map<String, dynamic> record) => SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: SizedBox(
          width: 1040,
          child: Table(
            border: TableBorder.all(
              color: Theme.of(context).dividerColor,
              borderRadius: BorderRadius.circular(6),
            ),
            columnWidths: const {
              0: FixedColumnWidth(155),
              1: FlexColumnWidth(),
              2: FixedColumnWidth(175),
              3: FlexColumnWidth(),
            },
            defaultVerticalAlignment: TableCellVerticalAlignment.middle,
            children: [
              _detailRow('Purchase ID', '${record['id']}', 'Grower Code / Item',
                  '${record['growerCode'] ?? record['itemName'] ?? '-'}'),
              _detailRow('Name / Party', '${record['name'] ?? '-'}', 'Status',
                  '${record['status'] ?? '-'}'),
              _detailRow(
                  'Gross Weight',
                  '${_money(record['grossWeightQuintal'])} Qtl',
                  'Tare Weight',
                  '${_money(record['tareWeightQuintal'])} Qtl'),
              _detailRow(
                  'Final Weight',
                  '${_money(record['finalWeightQuintal'])} Qtl',
                  'Currently Saved Rate',
                  '${_money(record['rate'])} / Qtl'),
              _detailRow(
                  'Currently Saved Amount',
                  _money(record['amount']),
                  'Original Gross Date',
                  _date(record['rateDate']).split(' ').first),
              _detailRow(
                  'Last Updated By',
                  '${_updater ?? record['updatedBy'] ?? '-'}',
                  'Last Updated At',
                  _date(record['updatedAt'])),
            ],
          ),
        ),
      );

  Widget _dropdown(
    String label,
    List<Map<String, dynamic>> options,
    int? value,
    void Function(int?) changed,
  ) =>
      SizedBox(
        width: 255,
        child: DropdownButtonFormField<int>(
          // Recreate after a type/search change; never retain a variety from another type.
          key: ValueKey('$label-$value-${_record?['id']}'),
          initialValue: options.any((x) => x['id'] == value) ? value : null,
          isExpanded: true,
          decoration: InputDecoration(labelText: label),
          items: options
              .map(
                (x) => DropdownMenuItem<int>(
                  value: x['id'],
                  child: Text(x['name'], overflow: TextOverflow.ellipsis),
                ),
              )
              .toList(),
          onChanged: _busy
              ? null
              : (value) => setState(() {
                    changed(value);
                    _preview = null;
                    _message = null;
                  }),
        ),
      );

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    if (!auth.hasRole('Admin') && !auth.hasRole('Developer')) {
      return const Center(
        child: Text('Only Admin and Developer can correct weighments.'),
      );
    }
    final record = _record;
    return ListView(
      padding: const EdgeInsets.all(20),
      children: [
        Text(
          'Weighment Corrections',
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        const SizedBox(height: 8),
        const Text(
          'Admin / Developer only. Paid cane purchases cannot be edited. Gross, tare and final weights remain unchanged.',
        ),
        const SizedBox(height: 12),
        RadioGroup<String>(
          groupValue: _kind,
          onChanged: (value) {
            if (_busy || value == null) return;
            setState(() {
              _kind = value;
              _record = null;
              _preview = null;
              _message = null;
              _id.clear();
            });
          },
          child: Wrap(
            spacing: 12,
            children: [
              for (final kind in ['purchase', 'sale'])
                SizedBox(
                  width: 220,
                  child: RadioListTile<String>(
                    title: Text(
                      kind == 'purchase' ? 'Cane Purchase' : 'Sale / Purchase',
                    ),
                    value: kind,
                    enabled: !_busy,
                  ),
                ),
            ],
          ),
        ),
        if (!_cane)
          const Padding(
            padding: EdgeInsets.only(bottom: 14),
            child: Text(
              'Sale / Purchase uses Item and Party, not cane varieties. Only vehicle details can be corrected here. This module has no linked payment status; rate and amount remain unchanged.',
            ),
          ),
        Wrap(
          spacing: 12,
          runSpacing: 12,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox(
              width: 255,
              child: TextField(
                controller: _id,
                enabled: !_busy,
                decoration: InputDecoration(
                  labelText: _cane ? 'Purchase ID' : 'Sale / Purchase ID',
                ),
                keyboardType: TextInputType.number,
                inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                onChanged: (_) => setState(() {
                  _record = null;
                  _preview = null;
                  _message = null;
                }),
                onSubmitted: (_) => _search(),
              ),
            ),
            FilledButton.icon(
              onPressed: _busy ? null : _search,
              icon: const Icon(Icons.search),
              label: const Text('Search'),
            ),
          ],
        ),
        if (_busy)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 12),
            child: LinearProgressIndicator(),
          ),
        if (_message != null)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 14),
            child: Text(
              _message!,
              style: TextStyle(
                color: _error
                    ? Theme.of(context).colorScheme.error
                    : Theme.of(context).colorScheme.primary,
              ),
            ),
          ),
        if (record != null) ...[
          const SizedBox(height: 18),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '${_cane ? 'Cane Purchase' : 'Sale / Purchase'} Details',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  const SizedBox(height: 8),
                  _detailsTable(record),
                  if (_cane)
                    const Padding(
                      padding: EdgeInsets.only(top: 10),
                      child: Text(
                        'The selected variety type preview uses the Rate Master effective on the original gross date.',
                      ),
                    ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 18),
          Wrap(
            spacing: 14,
            runSpacing: 18,
            children: [
              _dropdown(
                'Vehicle Type',
                _vehicles,
                _vehicleType,
                (v) => _vehicleType = v,
              ),
              SizedBox(
                width: 255,
                child: TextField(
                  controller: _vehicle,
                  enabled: !_busy,
                  decoration: const InputDecoration(
                    labelText: 'Vehicle Number',
                  ),
                  onChanged: (_) => setState(() {
                    _preview = null;
                    _message = null;
                  }),
                ),
              ),
              if (_cane)
                _dropdown('Variety Type', _types, _varietyType, (v) {
                  _varietyType = v;
                  _variety = null;
                }),
              if (_cane)
                _dropdown(
                  'Variety',
                  _varieties
                      .where((x) => x['varietyTypeId'] == _varietyType)
                      .toList(),
                  _variety,
                  (v) => _variety = v,
                ),
            ],
          ),
          const SizedBox(height: 20),
          if (_preview != null)
            Padding(
              padding: const EdgeInsets.only(bottom: 14),
              child: Text(
                'New preview${_cane ? ' — ${_selectedVarietyTypeName()} rate' : ''}: Vehicle ${_preview!['vehicleNumber']}    Rate: ${_money(_preview!['rate'])}    Amount: ${_money(_preview!['amount'])}',
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
          Wrap(
            spacing: 12,
            runSpacing: 12,
            children: [
              OutlinedButton(
                onPressed: _busy ? null : () => _calculateOrSave(false),
                child: const Text('Calculate / Preview'),
              ),
              FilledButton.icon(
                onPressed: _busy || _preview == null
                    ? null
                    : () => _calculateOrSave(true),
                icon: const Icon(Icons.save_outlined),
                label: const Text('Update'),
              ),
            ],
          ),
        ],
      ],
    );
  }
}
