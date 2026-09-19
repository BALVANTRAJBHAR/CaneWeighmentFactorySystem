import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api_client.dart';
import '../../providers/auth_provider.dart';

enum _ImageSource {
  purchase,
  salePurchase,
  payment;

  String get apiType => switch (this) {
        _ImageSource.purchase => 'purchase',
        _ImageSource.salePurchase => 'sale-purchase',
        _ImageSource.payment => 'payment',
      };

  String get label => switch (this) {
        _ImageSource.purchase => 'Cane Purchase',
        _ImageSource.salePurchase => 'Sale / Purchase',
        _ImageSource.payment => 'Payment',
      };

  String get hint => switch (this) {
        _ImageSource.purchase => 'Purchase ID, Grower Code or Grower Name',
        _ImageSource.salePurchase => 'Sale ID or Party Name',
        _ImageSource.payment =>
          'Payment ID, Advice Number, Grower Code or Grower Name',
      };
}

/// Search and open only the evidence images permitted for the signed-in user.
class ImageViewScreen extends StatefulWidget {
  const ImageViewScreen({super.key});

  @override
  State<ImageViewScreen> createState() => _ImageViewScreenState();
}

class _ImageViewScreenState extends State<ImageViewScreen> {
  final _search = TextEditingController();
  _ImageSource _source = _ImageSource.purchase;
  List<Map<String, dynamic>> _items = const [];
  bool _loading = false;
  String? _message;
  bool _messageIsError = false;

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _runSearch(_ImageSource source) async {
    final term = _search.text.trim();
    if (term.isEmpty) {
      setState(() {
        _message = 'Enter ' + source.hint.toLowerCase() + '.';
        _messageIsError = true;
      });
      return;
    }
    setState(() {
      _loading = true;
      _items = const [];
      _message = null;
    });
    try {
      final response = await ApiClient.instance.dio.get('/api/images/search',
          queryParameters: {'type': source.apiType, 'q': term});
      if (response.statusCode != 200) {
        throw ApiClient.errorMessage(response, 'Images could not be searched.');
      }
      final data = Map<String, dynamic>.from(response.data as Map);
      final rows = (data['items'] as List? ?? const [])
          .map((row) => Map<String, dynamic>.from(row as Map))
          .toList();
      if (!mounted) return;
      setState(() {
        _items = rows;
        _message = rows.isEmpty
            ? 'No active image record was found for this search.'
            : rows.length.toString() +
                ' image' +
                (rows.length == 1 ? '' : 's') +
                ' found. Click an image to open and zoom.';
        _messageIsError = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _message = error is String
            ? error
            : ApiClient.exceptionMessage(
                error, 'Images could not be searched.');
        _messageIsError = true;
      });
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final sources = <_ImageSource>[
      if (auth.can('Image.View')) _ImageSource.purchase,
      if (auth.can('Image.View')) _ImageSource.salePurchase,
      if (auth.can('CashEvidence.View')) _ImageSource.payment,
    ];
    if (sources.isEmpty) {
      return const Center(
          child: Text('You do not have permission to view captured images.'));
    }
    final selected = sources.contains(_source) ? _source : sources.first;

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('View Images', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 4),
          Text(
            'Search evidence by transaction, grower, advice number, or party. '
            'Click any result to view the image with zoom controls.',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          const SizedBox(height: 16),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Image Type',
                        style: Theme.of(context).textTheme.titleMedium),
                    const SizedBox(height: 4),
                    Wrap(
                      spacing: 16,
                      runSpacing: 4,
                      children: [
                        for (final option in sources)
                          InkWell(
                            borderRadius: BorderRadius.circular(8),
                            onTap: () => setState(() {
                              _source = option;
                              _items = const [];
                              _message = null;
                            }),
                            child:
                                Row(mainAxisSize: MainAxisSize.min, children: [
                              Radio<_ImageSource>(
                                value: option,
                                groupValue: selected,
                                onChanged: (_) => setState(() {
                                  _source = option;
                                  _items = const [];
                                  _message = null;
                                }),
                              ),
                              Text(option.label),
                            ]),
                          ),
                      ],
                    ),
                    const SizedBox(height: 12),
                    Row(children: [
                      Expanded(
                        child: TextField(
                          controller: _search,
                          textInputAction: TextInputAction.search,
                          onSubmitted: (_) => _runSearch(selected),
                          decoration: InputDecoration(
                            labelText: 'Search',
                            hintText: selected.hint,
                            prefixIcon: const Icon(Icons.search),
                            border: const OutlineInputBorder(),
                          ),
                        ),
                      ),
                      const SizedBox(width: 12),
                      FilledButton.icon(
                        onPressed: _loading ? null : () => _runSearch(selected),
                        icon: const Icon(Icons.search),
                        label: const Text('Search'),
                      ),
                    ]),
                    const SizedBox(height: 8),
                    Text(_helpText(selected),
                        style: Theme.of(context).textTheme.bodySmall),
                  ]),
            ),
          ),
          if (_message != null) ...[
            const SizedBox(height: 12),
            _Notice(
              text: _message!,
              error: _messageIsError,
              hasResults: _items.isNotEmpty,
            ),
          ],
          const SizedBox(height: 8),
          Expanded(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _items.isEmpty
                    ? Center(
                        child: Text(
                          _message == null
                              ? 'Select a type, enter a search value, then press Search.'
                              : '',
                          textAlign: TextAlign.center,
                        ),
                      )
                    : ListView.separated(
                        itemCount: _items.length,
                        separatorBuilder: (_, __) => const SizedBox(height: 8),
                        itemBuilder: (_, index) =>
                            _ImageResultCard(item: _items[index]),
                      ),
          ),
        ]),
      ),
    );
  }

  String _helpText(_ImageSource source) => switch (source) {
        _ImageSource.purchase =>
          'Purchase ID returns its image records. Grower Code or Name can return multiple purchases.',
        _ImageSource.salePurchase =>
          'Sale ID returns its image records. Party Name can return multiple transactions.',
        _ImageSource.payment =>
          'Payment ID or Advice Number returns matching images. Grower Code or Name can return multiple payments.',
      };
}

class _Notice extends StatelessWidget {
  final String text;
  final bool error;
  final bool hasResults;

  const _Notice(
      {required this.text, required this.error, required this.hasResults});

  @override
  Widget build(BuildContext context) {
    final color = error
        ? Theme.of(context).colorScheme.error
        : Theme.of(context).colorScheme.primary;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.10),
        border: Border.all(color: color.withValues(alpha: 0.45)),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(children: [
        Icon(
            error
                ? Icons.error_outline
                : hasResults
                    ? Icons.photo_library_outlined
                    : Icons.info_outline,
            color: color),
        const SizedBox(width: 8),
        Expanded(child: Text(text, style: TextStyle(color: color))),
      ]),
    );
  }
}

class _ImageResultCard extends StatelessWidget {
  final Map<String, dynamic> item;
  const _ImageResultCard({required this.item});

  String _v(Object? value) =>
      value?.toString().trim().isNotEmpty == true ? value.toString() : '-';

  String _date(Object? value) {
    final parsed = DateTime.tryParse(value?.toString() ?? '');
    if (parsed == null) return _v(value);
    final date = parsed.toLocal();
    return date.day.toString().padLeft(2, '0') +
        '-' +
        date.month.toString().padLeft(2, '0') +
        '-' +
        date.year.toString() +
        ' ' +
        date.hour.toString().padLeft(2, '0') +
        ':' +
        date.minute.toString().padLeft(2, '0');
  }

  @override
  Widget build(BuildContext context) {
    final type = item['sourceType']?.toString();
    final data = switch (type) {
      'purchase' => (
          'Purchase ID: ' + _v(item['purchaseId']),
          <String>[
            'Grower: ' +
                _v(item['growerCode']) +
                ' • ' +
                _v(item['growerName']),
            'Vehicle: ' + _v(item['vehicleNumber']),
            'Stage: ' + _v(item['captureStage']),
            'Status: ' + _v(item['transactionStatus']),
          ]
        ),
      'sale-purchase' => (
          'Sale / Purchase ID: ' + _v(item['salePurchaseId']),
          <String>[
            'Party: ' + _v(item['partyName']),
            'Vehicle: ' + _v(item['vehicleNumber']),
            'Driver: ' + _v(item['driverName']),
            'Stage: ' + _v(item['captureStage']),
            'Status: ' + _v(item['transactionStatus']),
          ]
        ),
      _ => (
          'Payment ID: ' +
              _v(item['paymentId']) +
              '  •  Advice No: ' +
              _v(item['adviceNumber']),
          <String>[
            'Grower: ' +
                _v(item['growerCode']) +
                ' • ' +
                _v(item['growerName']),
            if (item['purchaseId'] != null)
              'Purchase ID: ' + _v(item['purchaseId']),
            'Mode: ' + _v(item['paymentMode']),
            'Payable: ' + _v(item['amount']),
            'Status: ' + _v(item['transactionStatus']),
          ]
        ),
    };

    return Card(
      clipBehavior: Clip.antiAlias,
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          SizedBox(width: 150, height: 112, child: _Thumbnail(item: item)),
          const SizedBox(width: 14),
          Expanded(
            child:
                Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(data.$1,
                  style: Theme.of(context).textTheme.titleMedium,
                  overflow: TextOverflow.ellipsis),
              const SizedBox(height: 5),
              Wrap(
                spacing: 14,
                runSpacing: 5,
                children: data.$2
                    .map((text) => Text(text,
                        style: Theme.of(context).textTheme.bodySmall))
                    .toList(),
              ),
              const SizedBox(height: 8),
              Text(
                _v(item['imageName']) +
                    '  •  Captured: ' +
                    _date(item['capturedAt']),
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ]),
          ),
        ]),
      ),
    );
  }
}

class _Thumbnail extends StatefulWidget {
  final Map<String, dynamic> item;
  const _Thumbnail({required this.item});

  @override
  State<_Thumbnail> createState() => _ThumbnailState();
}

class _ThumbnailState extends State<_Thumbnail> {
  late Future<Uint8List> _bytes;

  @override
  void initState() {
    super.initState();
    _bytes = _load();
  }

  @override
  void didUpdateWidget(covariant _Thumbnail oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.item['imageId'] != widget.item['imageId'] ||
        oldWidget.item['sourceType'] != widget.item['sourceType']) {
      _bytes = _load();
    }
  }

  Future<Uint8List> _load() async {
    final id = widget.item['imageId'].toString();
    final type = widget.item['sourceType']?.toString();
    final path = type == 'payment'
        ? '/api/images/payment/' + id + '/file'
        : type == 'sale-purchase'
            ? '/api/images/sale-purchase/' + id + '/file'
            : '/api/images/' + id + '/file';
    final response = await ApiClient.instance.dio.get<List<int>>(path,
        options: Options(responseType: ResponseType.bytes));
    if (response.statusCode != 200 || response.data == null) {
      throw ApiClient.errorMessage(response, 'Image could not be loaded.');
    }
    final data = response.data!;
    return data is Uint8List ? data : Uint8List.fromList(data);
  }

  void _open(Uint8List bytes) {
    showDialog<void>(
      context: context,
      builder: (_) => _ZoomDialog(item: widget.item, bytes: bytes),
    );
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<Uint8List>(
        future: _bytes,
        builder: (_, snapshot) {
          if (snapshot.hasData) {
            return InkWell(
              borderRadius: BorderRadius.circular(6),
              onTap: () => _open(snapshot.data!),
              child: Stack(fit: StackFit.expand, children: [
                Ink.image(
                  image: MemoryImage(snapshot.data!),
                  fit: BoxFit.cover,
                  child: const SizedBox.expand(),
                ),
                const Align(
                  alignment: Alignment.bottomRight,
                  child: Padding(
                    padding: EdgeInsets.all(6),
                    child: CircleAvatar(
                        radius: 15, child: Icon(Icons.zoom_in, size: 18)),
                  ),
                ),
              ]),
            );
          }
          if (snapshot.hasError) {
            return Container(
              decoration: BoxDecoration(
                color: Theme.of(context).colorScheme.errorContainer,
                borderRadius: BorderRadius.circular(6),
              ),
              child: const Center(child: Icon(Icons.broken_image_outlined)),
            );
          }
          return const Center(child: CircularProgressIndicator());
        },
      );
}

class _ZoomDialog extends StatefulWidget {
  final Map<String, dynamic> item;
  final Uint8List bytes;
  const _ZoomDialog({required this.item, required this.bytes});

  @override
  State<_ZoomDialog> createState() => _ZoomDialogState();
}

class _ZoomDialogState extends State<_ZoomDialog> {
  final _transform = TransformationController();

  @override
  void dispose() {
    _transform.dispose();
    super.dispose();
  }

  void _zoom(double factor) {
    setState(() => _transform.value = _transform.value.clone()..scale(factor));
  }

  @override
  Widget build(BuildContext context) {
    final size = MediaQuery.of(context).size;
    return Dialog(
      child: SizedBox(
        width: size.width * 0.88,
        height: size.height * 0.84,
        child: Column(children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 8, 8),
            child: Row(children: [
              Expanded(
                child: Text(widget.item['imageName']?.toString() ?? 'Image',
                    style: Theme.of(context).textTheme.titleMedium,
                    overflow: TextOverflow.ellipsis),
              ),
              IconButton(
                  tooltip: 'Zoom out',
                  onPressed: () => _zoom(0.8),
                  icon: const Icon(Icons.zoom_out)),
              IconButton(
                  tooltip: 'Reset zoom',
                  onPressed: () =>
                      setState(() => _transform.value = Matrix4.identity()),
                  icon: const Icon(Icons.fit_screen_outlined)),
              IconButton(
                  tooltip: 'Zoom in',
                  onPressed: () => _zoom(1.25),
                  icon: const Icon(Icons.zoom_in)),
              IconButton(
                  tooltip: 'Close',
                  onPressed: () => Navigator.pop(context),
                  icon: const Icon(Icons.close)),
            ]),
          ),
          Expanded(
            child: ClipRect(
              child: InteractiveViewer(
                transformationController: _transform,
                minScale: 0.5,
                maxScale: 8,
                child: Center(child: Image.memory(widget.bytes)),
              ),
            ),
          ),
          const Padding(
            padding: EdgeInsets.all(10),
            child: Text(
                'Use mouse wheel / pinch, or the + and − buttons to zoom.'),
          ),
        ]),
      ),
    );
  }
}
