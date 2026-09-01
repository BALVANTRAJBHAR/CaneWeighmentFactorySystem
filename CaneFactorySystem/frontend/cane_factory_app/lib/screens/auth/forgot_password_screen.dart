import 'package:flutter/material.dart';
import '../../core/api_client.dart';

/// Mobile -> OTP -> Verify -> New password -> old sessions revoked -> Login.
class ForgotPasswordScreen extends StatefulWidget {
  const ForgotPasswordScreen({super.key});
  @override
  State<ForgotPasswordScreen> createState() => _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends State<ForgotPasswordScreen> {
  int _step = 0;
  final _mobile = TextEditingController();
  final _otp = TextEditingController();
  final _newPwd = TextEditingController();
  String? _msg;
  bool _busy = false;

  Future<void> _call(String path, Map<String, dynamic> data, {required VoidCallback onOk}) async {
    setState(() {
      _busy = true;
      _msg = null;
    });
    final res = await ApiClient.instance.dio.post(path, data: data);
    setState(() => _busy = false);
    if (res.statusCode == 200) {
      setState(() => _msg = res.data['message']?.toString());
      onOk();
    } else {
      setState(() => _msg = ApiClient.errorMessage(res));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Forgot Password')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Card(
            child: Padding(
              padding: const EdgeInsets.all(28),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Stepper(
                    physics: const NeverScrollableScrollPhysics(),
                    shrinkWrap: true,
                    currentStep: _step,
                    controlsBuilder: (_, __) => const SizedBox.shrink(),
                    steps: const [
                      Step(title: Text('Mobile'), content: SizedBox.shrink()),
                      Step(title: Text('OTP'), content: SizedBox.shrink()),
                      Step(title: Text('New Password'), content: SizedBox.shrink()),
                    ],
                  ),
                  if (_step == 0)
                    TextField(
                      controller: _mobile,
                      keyboardType: TextInputType.number,
                      maxLength: 10,
                      decoration: const InputDecoration(
                          labelText: 'Registered Mobile Number', hintText: 'Example: 9876543210'),
                    ),
                  if (_step == 1)
                    TextField(
                      controller: _otp,
                      keyboardType: TextInputType.number,
                      maxLength: 6,
                      decoration: const InputDecoration(labelText: 'Enter OTP', hintText: '6-digit OTP'),
                    ),
                  if (_step == 2)
                    TextField(
                      controller: _newPwd,
                      obscureText: true,
                      decoration: const InputDecoration(
                          labelText: 'New Password',
                          helperText: 'Min 8 chars with uppercase, lowercase and digit'),
                    ),
                  if (_msg != null)
                    Padding(padding: const EdgeInsets.only(top: 10), child: Text(_msg!)),
                  const SizedBox(height: 16),
                  FilledButton(
                    onPressed: _busy
                        ? null
                        : () {
                            if (_step == 0) {
                              _call('/api/auth/forgot-password/start', {'mobile': _mobile.text.trim()},
                                  onOk: () => setState(() => _step = 1));
                            } else if (_step == 1) {
                              _call('/api/auth/forgot-password/verify',
                                  {'mobile': _mobile.text.trim(), 'otp': _otp.text.trim()},
                                  onOk: () => setState(() => _step = 2));
                            } else {
                              _call('/api/auth/forgot-password/reset', {
                                'mobile': _mobile.text.trim(),
                                'otp': _otp.text.trim(),
                                'newPassword': _newPwd.text
                              }, onOk: () => Navigator.pop(context));
                            }
                          },
                    child: Text(_step == 0
                        ? 'Send OTP'
                        : _step == 1
                            ? 'Verify OTP'
                            : 'Reset Password'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
