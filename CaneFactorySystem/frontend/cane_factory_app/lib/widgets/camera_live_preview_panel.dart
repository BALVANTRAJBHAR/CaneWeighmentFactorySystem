import 'dart:async';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../core/api_client.dart';

/// Credential-safe continuous camera preview. The backend owns RTSP and ffmpeg;
/// Flutter receives one multipart MJPEG stream per camera and renders frames as
/// they arrive. No timer or HTTP snapshot polling is used here.
class CameraLivePreviewPanel extends StatefulWidget {
  const CameraLivePreviewPanel(
      {super.key,
      required this.cameras,
      this.compact = false,
      this.squareCards = false});

  final List cameras;
  final bool compact;
  final bool squareCards;

  @override
  State<CameraLivePreviewPanel> createState() => _CameraLivePreviewPanelState();
}

class _CameraLivePreviewPanelState extends State<CameraLivePreviewPanel> {
  final Map<int, Uint8List> _frames = {};
  final Map<int, String> _errors = {};
  final Map<int, CancelToken> _cancelTokens = {};
  final Set<int> _active = {};

  @override
  void initState() {
    super.initState();
    _startStreams();
  }

  @override
  void didUpdateWidget(covariant CameraLivePreviewPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    _startStreams();
  }

  @override
  void dispose() {
    for (final token in _cancelTokens.values) {
      token.cancel('Live preview closed');
    }
    _cancelTokens.clear();
    super.dispose();
  }

  void _startStreams() {
    for (final camera in widget.cameras.take(6)) {
      final id = camera['id'] as int?;
      if (id != null && _active.add(id)) {
        unawaited(_runStream(id, camera));
      }
    }
  }

  Future<void> _runStream(int id, dynamic camera) async {
    final cancelToken = CancelToken();
    _cancelTokens[id] = cancelToken;
    var backoffSeconds = 1;

    try {
      while (mounted && !cancelToken.isCancelled) {
        try {
          _setError(id, _frames[id] == null ? 'Connecting…' : 'Reconnecting…');
          final response = await ApiClient.instance.dio.get<ResponseBody>(
            '/api/config/cameras/$id/live',
            cancelToken: cancelToken,
            options: Options(
              responseType: ResponseType.stream,
              validateStatus: (s) => s != null && s < 500,
            ),
          );

          if (response.statusCode != 200 || response.data == null) {
            throw _StreamStatusException(_message(response.statusCode));
          }

          backoffSeconds = 1;
          _setError(id, null);
          await _consumeMjpeg(id, response.data!.stream, cancelToken);
          if (cancelToken.isCancelled || !mounted) break;
          throw const _StreamStatusException('Stream disconnected');
        } on DioException catch (error) {
          if (cancelToken.isCancelled || !mounted) break;
          _setError(id, _message(error.response?.statusCode));
        } on _StreamStatusException catch (error) {
          if (cancelToken.isCancelled || !mounted) break;
          _setError(id, error.message);
        } catch (_) {
          if (cancelToken.isCancelled || !mounted) break;
          _setError(id, 'Connection Error');
        }

        if (!mounted || cancelToken.isCancelled) break;
        await Future<void>.delayed(Duration(seconds: backoffSeconds));
        backoffSeconds = (backoffSeconds * 2).clamp(1, 10);
      }
    } finally {
      _cancelTokens.remove(id);
      _active.remove(id);
    }
  }

  Future<void> _consumeMjpeg(
      int id, Stream<Uint8List> chunks, CancelToken cancelToken) async {
    final buffer = <int>[];
    await for (final chunk in chunks) {
      if (cancelToken.isCancelled) return;
      buffer.addAll(chunk);

      Uint8List? latestFrame;

      while (true) {
        final start = _findMarker(buffer, 0, 0xff, 0xd8);
        if (start < 0) {
          if (buffer.length > 1) {
            buffer.removeRange(0, buffer.length - 1);
          }
          break;
        }
        if (start > 0) buffer.removeRange(0, start);
        final end = _findMarker(buffer, 2, 0xff, 0xd9);
        if (end < 0) break;

        latestFrame = Uint8List.fromList(buffer.sublist(0, end + 2));
        buffer.removeRange(0, end + 2);
      }

      // If the network delivered several frames in one chunk, render only
      // the newest one. This prevents a slow UI decode from displaying a
      // backlog several frames behind the camera.
      if (latestFrame != null && latestFrame.length > 100 && mounted) {
        setState(() {
          _frames[id] = latestFrame!;
          _errors.remove(id);
        });
      }
    }
  }

  int _findMarker(List<int> bytes, int from, int first, int second) {
    for (var i = from; i + 1 < bytes.length; i++) {
      if (bytes[i] == first && bytes[i + 1] == second) return i;
    }
    return -1;
  }

  void _setError(int id, String? error) {
    if (!mounted) return;
    setState(() {
      if (error == null) {
        _errors.remove(id);
      } else {
        _errors[id] = error;
      }
    });
  }

  String _message(int? statusCode) {
    if (statusCode == 401 || statusCode == 403) return 'Authentication Failed';
    if (statusCode == 404 || statusCode == 409) return 'Camera Offline';
    return 'Connection Error';
  }

  @override
  Widget build(BuildContext context) {
    if (widget.cameras.isEmpty) return const SizedBox.shrink();
    return Card(
      clipBehavior: Clip.antiAlias,
      child: Padding(
        padding: const EdgeInsets.all(8),
        child: LayoutBuilder(builder: (context, constraints) {
          final configured = widget.cameras.length.clamp(1, 6);
          final columns = constraints.maxWidth < 330
              ? 1
              : configured == 1
                  ? 1
                  : configured <= 4
                      ? 2
                      : 3;
          final cardWidth = columns == 1
              ? constraints.maxWidth
              : (constraints.maxWidth - ((columns - 1) * 8)) / columns;
          final cardExtent = widget.squareCards
              ? cardWidth + 52
              : (widget.compact ? 205.0 : 230.0);
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
                        mainAxisExtent: cardExtent),
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

class _StreamStatusException implements Exception {
  const _StreamStatusException(this.message);
  final String message;
}
