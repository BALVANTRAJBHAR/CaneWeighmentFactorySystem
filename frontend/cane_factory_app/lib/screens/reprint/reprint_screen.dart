import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';

import '../../core/api_client.dart';
import '../../core/file_download.dart';
import '../../core/print_service.dart';
import '../../providers/auth_provider.dart';

enum _ReprintType {
  purchase,
  salePurchase,
  payment;

  String get label => switch (this) {
        _ReprintType.purchase => 'Cane Weighment',
        _ReprintType.salePurchase => 'Sale Weighment',
        _ReprintType.payment => 'Payment',
      };
}

enum _PaymentLookup { paymentId, adviceNumber }

class ReprintScreen extends StatefulWidget {
  const ReprintScreen({super.key});

  @override
  State<ReprintScreen> createState() => _ReprintScreenState();
}

class _ReprintScreenState extends State<ReprintScreen> {
  final _number = TextEditingController();
  _ReprintType _type = _ReprintType.purchase;
  _PaymentLookup _paymentLookup = _PaymentLookup.paymentId;
  Map<String, dynamic>? _printConfig;
  String? _configError;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _loadPrintConfig();
  }

  @override
  void dispose() {
    _number.dispose();
    super.dispose();
  }

  Future<void> _loadPrintConfig() async {
    try {
      final response =
          await ApiClient.instance.dio.get('/api/print/reprint/config');
      if (!mounted) return;
      if (response.statusCode == 200) {
        setState(() {
          _printConfig = Map<String, dynamic>.from(response.data as Map);
          _configError = null;
        });
      } else {
        setState(() => _configError = ApiClient.errorMessage(
            response, 'Print configuration could not be loaded.'));
      }
    } catch (error) {
      if (mounted) {
        setState(() => _configError = ApiClient.exceptionMessage(
            error, 'Print configuration could not be loaded.'));
      }
    }
  }

  int? _validatedNumber() {
    final value = int.tryParse(_number.text.trim());
    if (value == null || value <= 0) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('Enter a valid number greater than 0.')));
      return null;
    }
    return value;
  }

  String _endpoint(_ReprintType type, int value, String target) {
    final base = switch (type) {
      _ReprintType.purchase =>
        '/api/print/reprint/purchase/' + value.toString(),
      _ReprintType.salePurchase =>
        '/api/print/reprint/sale-purchase/' + value.toString(),
      _ReprintType.payment => _paymentLookup == _PaymentLookup.paymentId
          ? '/api/print/reprint/payment/' + value.toString()
          : '/api/print/reprint/payment-advice/' + value.toString(),
    };
    return base + '?target=' + target + '&format=final';
  }

  String _fileName(_ReprintType type, int value) {
    final key = type == _ReprintType.payment &&
            _paymentLookup == _PaymentLookup.adviceNumber
        ? 'Advice'
        : type.label.replaceAll(' ', '').replaceAll('/', '');
    return 'DUPLICATE-' + key + '-' + value.toString() + '.pdf';
  }

  Future<void> _openPdf(_ReprintType selected) async {
    final value = _validatedNumber();
    if (value == null) return;
    setState(() => _busy = true);
    try {
      await openPdfAfterSave(
        context,
        _endpoint(selected, value, 'A4'),
        _fileName(selected, value),
        successMessage:
            'Duplicate / Reprint PDF opened. This action is recorded in the audit log.',
        failurePrefix: 'Reprint PDF could not be opened:',
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _print(_ReprintType selected) async {
    final value = _validatedNumber();
    if (value == null) return;
    final config = _printConfig;
    if (config == null) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content:
              Text(_configError ?? 'Print configuration is not available.'),
          backgroundColor: Colors.red));
      return;
    }

    final printerType = config['printerType']?.toString() ?? 'A4';
    final printerName = config['printerName']?.toString() ?? '';
    setState(() => _busy = true);
    try {
      final outcome = await PrintService.printDocument(
        documentUrl: _endpoint(selected, value, printerType),
        printerType: printerType,
        printerName: printerName,
        copies: 1,
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(outcome.message),
        backgroundColor: outcome.success ? Colors.green : Colors.red,
      ));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final available = <_ReprintType>[
      if (auth.can('Weighment.Print')) _ReprintType.purchase,
      if (auth.can('SalePurchase.Print')) _ReprintType.salePurchase,
      if (auth.can('Payment.Print')) _ReprintType.payment,
    ];
    if (available.isEmpty) {
      return const Center(
          child: Text('You do not have permission to reprint documents.'));
    }
    final selected = available.contains(_type) ? _type : available.first;
    final identifierLabel = selected == _ReprintType.purchase
        ? 'Cane Weighment ID'
        : selected == _ReprintType.salePurchase
            ? 'Sale Weighment ID'
            : _paymentLookup == _PaymentLookup.paymentId
                ? 'Payment ID'
                : 'Advice Number';

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(16),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 950),
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('Print / Reprint',
                style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 4),
            const Text(
                'Generate a clearly marked DUPLICATE / REPRINT copy of an existing transaction.'),
            const SizedBox(height: 16),
            Card(
              child: Padding(
                padding: const EdgeInsets.all(18),
                child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Document Type',
                          style: Theme.of(context).textTheme.titleMedium),
                      const SizedBox(height: 4),
                      Wrap(
                        spacing: 18,
                        runSpacing: 4,
                        children: [
                          for (final option in available)
                            InkWell(
                              borderRadius: BorderRadius.circular(8),
                              onTap: () => setState(() {
                                _type = option;
                                _number.clear();
                              }),
                              child: Row(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    Radio<_ReprintType>(
                                      value: option,
                                      groupValue: selected,
                                      onChanged: (_) => setState(() {
                                        _type = option;
                                        _number.clear();
                                      }),
                                    ),
                                    Text(option.label),
                                  ]),
                            ),
                        ],
                      ),
                      if (selected == _ReprintType.payment) ...[
                        const Divider(height: 24),
                        Text('Find Payment By',
                            style: Theme.of(context).textTheme.titleSmall),
                        Wrap(spacing: 16, children: [
                          RadioMenuButton<_PaymentLookup>(
                            value: _PaymentLookup.paymentId,
                            groupValue: _paymentLookup,
                            onChanged: (value) => setState(() {
                              _paymentLookup = value!;
                              _number.clear();
                            }),
                            child: const Text('Payment ID'),
                          ),
                          RadioMenuButton<_PaymentLookup>(
                            value: _PaymentLookup.adviceNumber,
                            groupValue: _paymentLookup,
                            onChanged: (value) => setState(() {
                              _paymentLookup = value!;
                              _number.clear();
                            }),
                            child: const Text('Advice Number'),
                          ),
                        ]),
                      ],
                      const SizedBox(height: 12),
                      TextField(
                        controller: _number,
                        enabled: !_busy,
                        keyboardType: TextInputType.number,
                        inputFormatters: [
                          FilteringTextInputFormatter.digitsOnly
                        ],
                        textInputAction: TextInputAction.done,
                        onSubmitted: (_) => _openPdf(selected),
                        decoration: InputDecoration(
                          labelText: identifierLabel,
                          hintText: 'Enter ' + identifierLabel,
                          prefixIcon: const Icon(Icons.numbers),
                          border: const OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 14),
                      Wrap(spacing: 12, runSpacing: 10, children: [
                        FilledButton.icon(
                          onPressed: _busy ? null : () => _openPdf(selected),
                          icon: const Icon(Icons.picture_as_pdf_outlined),
                          label: const Text('Open Duplicate PDF'),
                        ),
                        OutlinedButton.icon(
                          onPressed: _busy ? null : () => _print(selected),
                          icon: const Icon(Icons.print_outlined),
                          label: const Text('Print Duplicate'),
                        ),
                        if (_busy)
                          const Padding(
                            padding: EdgeInsets.all(8),
                            child: SizedBox(
                                width: 22,
                                height: 22,
                                child: CircularProgressIndicator(
                                    strokeWidth: 2.5)),
                          ),
                      ]),
                    ]),
              ),
            ),
            const SizedBox(height: 12),
            Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Icon(Icons.info_outline),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Text(
                          'Purchase automatically prints Final/Tare when available, otherwise Gross. '
                          'Completed Sale/Purchase prints Gross/Final, otherwise Tare. '
                          'Every final copy shows DUPLICATE / REPRINT and is recorded in Audit Log.',
                        ),
                      ),
                    ]),
              ),
            ),
            if (_printConfig != null) ...[
              const SizedBox(height: 8),
              Text(
                'Configured printer: ' +
                    (_printConfig!['printerName']?.toString().isNotEmpty == true
                        ? _printConfig!['printerName'].toString()
                        : 'Not selected') +
                    ' (' +
                    (_printConfig!['printerType']?.toString() ?? '-') +
                    ')',
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ] else if (_configError != null) ...[
              const SizedBox(height: 8),
              Text(_configError!,
                  style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
          ]),
        ),
      ),
    );
  }
}
