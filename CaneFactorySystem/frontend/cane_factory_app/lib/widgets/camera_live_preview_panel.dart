import 'dart:async';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../core/api_client.dart';

/// Credential-safe, periodically refreshed real camera frames.  Providers remain
/// server-side; the app receives only an authenticated JPEG response.
class CameraLivePreviewPanel extends StatefulWidget {
  const CameraLivePreviewPanel(
      {super.key, required this.cameras, this.compact = false});

  final List cameras;
  final bool compact;

  @override
  State<CameraLivePreviewPanel> createState() => _CameraLivePreviewPanelState();
}

class _CameraLivePreviewPanelState extends State<CameraLivePreviewPanel> {
  final Map<int, Uint8List> _frames = {};
  final Map<int, String> _errors = {};
  final Set<int> _loading = {};
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _refresh();
    _timer = Timer.periodic(const Duration(seconds: 3), (_) => _refresh());
  }

  @override
  void didUpdateWidget(covariant CameraLivePreviewPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.cameras != widget.cameras) _refresh();
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _refresh() async {
    await Future.wait(
        widget.cameras.take(6).map((camera) => _refreshOne(camera)));
  }

  Future<void> _refreshOne(dynamic camera) async {
    final id = camera['id'] as int?;
    if (id == null || _loading.contains(id)) return;
    _loading.add(id);
    try {
      final response = await ApiClient.instance.dio.get(
        '/api/config/cameras/$id/snapshot',
        options: Options(
            responseType: ResponseType.bytes,
            validateStatus: (s) => s != null && s < 500),
      );
      if (!mounted) return;
      if (response.statusCode == 200 && response.data is List<int>) {
        setState(() {
          _frames[id] = Uint8List.fromList(response.data as List<int>);
          _errors.remove(id);
        });
      } else {
        setState(() => _errors[id] = _message(response));
      }
    } catch (_) {
      if (mounted) setState(() => _errors[id] = 'Connection Error');
    } finally {
      _loading.remove(id);
    }
  }

  String _message(Response response) {
    if (response.statusCode == 409 || response.statusCode == 404)
      return 'Camera Offline';
    return 'Connection Error';
  }

  @override
  Widget build(BuildContext context) {
    if (widget.cameras.isEmpty) {
      return const SizedBox.shrink();
    }
    return Card(
      clipBehavior: Clip.antiAlias,
      child: Padding(
        padding: const EdgeInsets.all(8),
        child: LayoutBuilder(builder: (context, constraints) {
          final columns = constraints.maxWidth < 330
              ? 1
              : constraints.maxWidth < 700
                  ? 2
                  : 3;
          return Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('Live Cameras',
                    style: TextStyle(fontWeight: FontWeight.w700)),
                const SizedBox(height: 6),
                Expanded(
                  child: GridView.builder(
                    padding: EdgeInsets.zero,
                    itemCount:
                        widget.cameras.length > 6 ? 6 : widget.cameras.length,
                    gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                        crossAxisCount: columns,
                        crossAxisSpacing: 8,
                        mainAxisSpacing: 8,
                        childAspectRatio: widget.compact ? 1.55 : 1.7),
                    itemBuilder: (_, index) =>
                        _cameraCard(widget.cameras[index]),
                  ),
                ),
              ]);
        }),
      ),
    );
  }

  Widget _cameraCard(dynamic camera) {
    final id = camera['id'] as int? ?? -1;
    final frame = _frames[id];
    final error = _errors[id];
    final cameraNo = '${camera['cameraNumber'] ?? '-'}'.padLeft(2, '0');
    return DecoratedBox(
      decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(8),
          border: Border.all(color: Colors.black26)),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Expanded(
          child: ClipRRect(
            borderRadius: const BorderRadius.vertical(top: Radius.circular(7)),
            child: frame != null
                ? Image.memory(frame, fit: BoxFit.cover, gaplessPlayback: true)
                : Container(
                    color: Colors.black87,
                    alignment: Alignment.center,
                    child: Column(mainAxisSize: MainAxisSize.min, children: [
                      Icon(
                          error == null
                              ? Icons.videocam_outlined
                              : Icons.videocam_off_outlined,
                          color: error == null
                              ? Colors.white70
                              : Colors.orangeAccent),
                      const SizedBox(height: 4),
                      Text(error ?? 'Connecting…',
                          textAlign: TextAlign.center,
                          style: const TextStyle(
                              color: Colors.white, fontSize: 11)),
                    ]),
                  ),
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
          child: Text(
              'Camera $cameraNo • ${camera['vendor'] ?? ''} ${camera['model'] ?? ''}',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style:
                  const TextStyle(fontSize: 11, fontWeight: FontWeight.w600)),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(6, 0, 6, 4),
          child: Text(error ?? (frame == null ? 'Connecting' : 'Live'),
              style: TextStyle(
                  fontSize: 10,
                  color: error == null
                      ? const Color(0xFF2E7D32)
                      : Colors.orange.shade800)),
        ),
      ]),
    );
  }
}
