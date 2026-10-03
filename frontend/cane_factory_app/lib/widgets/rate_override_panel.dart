import 'package:camera/camera.dart';
import 'package:dio/dio.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/api_client.dart';

/// Shared Cane-Tare/Sale-Gross rate control. Evidence is sanitised and staged by
/// the API first; the returned opaque token is consumed only by the final save.
class RateOverridePanel extends StatefulWidget {
  final double? masterRate;
  final String transactionType;
  final int? transactionId;

  const RateOverridePanel({
    super.key,
    required this.masterRate,
    required this.transactionType,
    required this.transactionId,
  });

  @override
  State<RateOverridePanel> createState() => RateOverridePanelState();
}

class RateOverridePanelState extends State<RateOverridePanel> {
  final _rate = TextEditingController();
  final _remark = TextEditingController();
  List<Map<String, dynamic>> _admins = [];
  List<Map<String, dynamic>> _reasons = [];
  Map<String, dynamic>? _configuredCamera;
  int? _approvedByUserId;
  int? _rateReasonId;
  String? _evidenceToken;
  String? _evidenceName;
  String? _error;
  bool _uploading = false;

  @override
  void initState() {
    super.initState();
    _setMasterRate();
    _rate.addListener(_rateChanged);
    _loadOptions();
  }

  @override
  void didUpdateWidget(covariant RateOverridePanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.masterRate != widget.masterRate ||
        oldWidget.transactionId != widget.transactionId ||
        oldWidget.transactionType != widget.transactionType) {
      _setMasterRate(resetApproval: true);
    }
  }

  @override
  void dispose() {
    _rate.removeListener(_rateChanged);
    _rate.dispose();
    _remark.dispose();
    super.dispose();
  }

  void _setMasterRate({bool resetApproval = false}) {
    _rate.text = widget.masterRate?.toStringAsFixed(2) ?? '';
    if (resetApproval) {
      _approvedByUserId = null;
      _rateReasonId = null;
      _evidenceToken = null;
      _evidenceName = null;
      _remark.clear();
      _error = null;
    }
  }

  double? get _enteredRate => double.tryParse(_rate.text.trim());
  bool get _changed =>
      widget.masterRate != null &&
      _enteredRate != null &&
      (_enteredRate! - widget.masterRate!).abs() >= 0.005;

  void _rateChanged() {
    if (!mounted) return;
    final entered = _enteredRate;
    setState(() {
      _error = entered != null &&
              widget.masterRate != null &&
              entered > widget.masterRate! + 0.004
          ? 'Rate cannot exceed master rate ${widget.masterRate!.toStringAsFixed(2)}.'
          : null;
    });
  }

  Future<void> _loadOptions() async {
    try {
      final response =
          await ApiClient.instance.dio.get('/api/rate-overrides/options');
      if (!mounted || response.statusCode != 200 || response.data is! Map)
        return;
      setState(() {
        _admins = List<Map<String, dynamic>>.from(
            (response.data['admins'] as List? ?? [])
                .map((x) => Map<String, dynamic>.from(x)));
        _reasons = List<Map<String, dynamic>>.from(
            (response.data['reasons'] as List? ?? [])
                .map((x) => Map<String, dynamic>.from(x)));
        _configuredCamera = response.data['configuredCamera'] is Map
            ? Map<String, dynamic>.from(response.data['configuredCamera'])
            : null;
      });
    } catch (_) {}
  }

  /// Returns fields for the final save, or null after showing an inline error.
  Map<String, dynamic>? validateAndBuildPayload() {
    final rate = _enteredRate;
    if (widget.masterRate == null || rate == null || rate <= 0) {
      setState(() => _error = 'A valid master/entered rate is required.');
      return null;
    }
    if (rate > widget.masterRate! + 0.004) {
      setState(() => _error =
          'Rate cannot exceed master rate ${widget.masterRate!.toStringAsFixed(2)}.');
      return null;
    }
    if (_changed &&
        (_approvedByUserId == null ||
            _rateReasonId == null ||
            _evidenceToken == null)) {
      setState(() => _error =
          'Changed rate requires Approved By, Rate Reason and JPG/JPEG camera/attachment evidence.');
      return null;
    }
    setState(() => _error = null);
    return {
      'rate': rate,
      if (_changed) 'approvedByUserId': _approvedByUserId,
      if (_changed) 'rateReasonId': _rateReasonId,
      if (_changed) 'rateOverrideRemark': _remark.text.trim(),
      if (_changed) 'rateEvidenceToken': _evidenceToken,
    };
  }

  Future<void> _pickAttachment() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: const ['jpg', 'jpeg'],
      allowMultiple: false,
      withData: true,
    );
    if (result == null || result.files.isEmpty) return;
    final file = result.files.single;
    final bytes = file.bytes;
    if (bytes == null) {
      return _showError(
          'Selected image could not be read. Try another JPG/JPEG file.');
    }
    await _uploadEvidence(bytes, file.name, 'UPLOAD');
  }

  Future<void> _captureDeviceCamera() async {
    try {
      final bytes = await showDialog<Uint8List>(
          context: context, builder: (_) => const _DeviceCameraDialog());
      if (bytes != null)
        await _uploadEvidence(bytes, 'device-camera.jpg', 'DEVICE_CAMERA');
    } on CameraException catch (e) {
      _showError(
          'Device camera is not available (${e.description ?? e.code}). Use the configured camera or JPG upload.');
    } catch (_) {
      _showError(
          'Device camera is not available. Use the configured camera or JPG upload.');
    }
  }

  Future<void> _captureConfiguredCamera() async {
    if (!_canAttach()) return;
    setState(() {
      _uploading = true;
      _error = null;
    });
    try {
      final response = await ApiClient.instance.dio
          .post('/api/rate-overrides/evidence/capture-configured', data: {
        'transactionType': widget.transactionType,
        'transactionId': widget.transactionId,
      });
      if (!mounted) return;
      if (response.statusCode == 200) {
        setState(() {
          _evidenceToken = response.data['evidenceToken'];
          _evidenceName = response.data['imageName'];
        });
      } else {
        _showError(ApiClient.errorMessage(response));
      }
    } catch (error) {
      if (mounted) _showError(ApiClient.exceptionMessage(error));
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }

  Future<void> _uploadEvidence(
      Uint8List bytes, String fileName, String source) async {
    if (!_canAttach()) return;
    if (bytes.length > 6000000)
      return _showError('JPG/JPEG evidence must be 6 MB or smaller.');
    setState(() {
      _uploading = true;
      _error = null;
    });
    try {
      final response = await ApiClient.instance.dio.post(
        '/api/rate-overrides/evidence/upload',
        data: FormData.fromMap({
          'transactionType': widget.transactionType,
          'transactionId': widget.transactionId,
          'source': source,
          'image': MultipartFile.fromBytes(bytes, filename: fileName),
        }),
      );
      if (!mounted) return;
      if (response.statusCode == 200) {
        setState(() {
          _evidenceToken = response.data['evidenceToken'];
          _evidenceName = response.data['imageName'];
        });
      } else {
        _showError(ApiClient.errorMessage(response));
      }
    } catch (error) {
      if (mounted) _showError(ApiClient.exceptionMessage(error));
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }

  bool _canAttach() {
    if (widget.transactionId == null || widget.transactionId! <= 0) {
      _showError('Select the pending transaction before attaching evidence.');
      return false;
    }
    return true;
  }

  void _showError(String value) {
    if (mounted) setState(() => _error = value);
  }

  @override
  Widget build(BuildContext context) {
    if (widget.masterRate == null) {
      return Text('No active master rate is configured.',
          style: TextStyle(color: Theme.of(context).colorScheme.error));
    }
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Wrap(
          spacing: 10,
          runSpacing: 10,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox(
              width: 190,
              child: TextField(
                controller: _rate,
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
                inputFormatters: [
                  FilteringTextInputFormatter.allow(RegExp(r'^\d*\.?\d{0,2}'))
                ],
                decoration: InputDecoration(
                  labelText: 'Rate (editable downward)',
                  helperText:
                      'Master: ${widget.masterRate!.toStringAsFixed(2)} / Qtl',
                ),
              ),
            ),
            if (_changed) ...[
              SizedBox(
                width: 220,
                child: DropdownButtonFormField<int>(
                  value: _approvedByUserId,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Approved By *'),
                  items: [
                    for (final admin in _admins)
                      DropdownMenuItem(
                        value: admin['id'] as int,
                        child: Text(
                            '${admin['fullName']} (${admin['username']})',
                            overflow: TextOverflow.ellipsis),
                      )
                  ],
                  onChanged: (value) =>
                      setState(() => _approvedByUserId = value),
                ),
              ),
              SizedBox(
                width: 220,
                child: DropdownButtonFormField<int>(
                  value: _rateReasonId,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Rate Reason *'),
                  items: [
                    for (final reason in _reasons)
                      DropdownMenuItem(
                        value: reason['id'] as int,
                        child: Text('${reason['reasonName']}',
                            overflow: TextOverflow.ellipsis),
                      )
                  ],
                  onChanged: (value) => setState(() => _rateReasonId = value),
                ),
              ),
              SizedBox(
                width: 260,
                child: TextField(
                  controller: _remark,
                  maxLength: 500,
                  decoration: const InputDecoration(
                      labelText: 'Rate Remark (optional)', counterText: ''),
                ),
              ),
            ],
          ]),
      if (_changed) ...[
        const SizedBox(height: 8),
        Wrap(
            spacing: 8,
            runSpacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              OutlinedButton.icon(
                onPressed: _uploading ? null : _pickAttachment,
                icon: const Icon(Icons.attach_file),
                label: const Text('JPG/JPEG'),
              ),
              OutlinedButton.icon(
                onPressed: _uploading ? null : _captureDeviceCamera,
                icon: const Icon(Icons.camera_alt_outlined),
                label: const Text('Device Camera'),
              ),
              OutlinedButton.icon(
                onPressed: _uploading || _configuredCamera == null
                    ? null
                    : _captureConfiguredCamera,
                icon: const Icon(Icons.videocam_outlined),
                label: Text(_configuredCamera == null
                    ? 'Configured Camera unavailable'
                    : 'Configured Camera ${_configuredCamera!['cameraNumber']}'),
              ),
              if (_uploading)
                const SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(strokeWidth: 2)),
              if (_evidenceName != null)
                Chip(
                    avatar: const Icon(Icons.verified, size: 17),
                    label: Text('Evidence ready: $_evidenceName')),
            ]),
      ],
      if (_error != null)
        Padding(
          padding: const EdgeInsets.only(top: 6),
          child: Text(_error!,
              style: TextStyle(
                  color: Theme.of(context).colorScheme.error,
                  fontWeight: FontWeight.w600)),
        ),
    ]);
  }
}

class _DeviceCameraDialog extends StatefulWidget {
  const _DeviceCameraDialog();

  @override
  State<_DeviceCameraDialog> createState() => _DeviceCameraDialogState();
}

class _DeviceCameraDialogState extends State<_DeviceCameraDialog> {
  CameraController? _controller;
  String? _error;
  bool _taking = false;

  @override
  void initState() {
    super.initState();
    _initialise();
  }

  Future<void> _initialise() async {
    try {
      final cameras = await availableCameras();
      if (cameras.isEmpty) {
        throw CameraException('NO_CAMERA', 'No integrated camera was found.');
      }
      final rear =
          cameras.where((c) => c.lensDirection == CameraLensDirection.back);
      final selected = rear.isNotEmpty ? rear.first : cameras.first;
      final controller = CameraController(selected, ResolutionPreset.medium,
          enableAudio: false);
      await controller.initialize();
      if (!mounted) {
        await controller.dispose();
        return;
      }
      setState(() => _controller = controller);
    } on CameraException catch (e) {
      if (mounted) setState(() => _error = e.description ?? e.code);
    } catch (_) {
      if (mounted)
        setState(() => _error =
            'Integrated camera is unavailable on this device/browser.');
    }
  }

  Future<void> _take() async {
    final controller = _controller;
    if (controller == null || !controller.value.isInitialized || _taking)
      return;
    setState(() => _taking = true);
    try {
      final file = await controller.takePicture();
      final bytes = await file.readAsBytes();
      if (mounted) Navigator.pop(context, bytes);
    } on CameraException catch (e) {
      if (mounted)
        setState(() {
          _taking = false;
          _error = e.description ?? e.code;
        });
    }
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: const Text('Capture Rate Evidence'),
        content: SizedBox(
          width: 640,
          height: 440,
          child: _error != null
              ? Center(child: Text(_error!, textAlign: TextAlign.center))
              : _controller == null
                  ? const Center(child: CircularProgressIndicator())
                  : ClipRRect(
                      borderRadius: BorderRadius.circular(8),
                      child: CameraPreview(_controller!)),
        ),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Cancel')),
          FilledButton.icon(
              onPressed: _controller == null || _taking ? null : _take,
              icon: const Icon(Icons.camera),
              label: Text(_taking ? 'Capturing...' : 'Capture')),
        ],
      );
}
