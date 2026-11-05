import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../providers/auth_provider.dart';

class ChangePasswordScreen extends StatefulWidget {
  final bool forced;
  const ChangePasswordScreen({super.key, this.forced = false});
  @override
  State<ChangePasswordScreen> createState() => _ChangePasswordScreenState();
}

class _ChangePasswordScreenState extends State<ChangePasswordScreen> {
  final _current = TextEditingController();
  final _new = TextEditingController();
  final _confirm = TextEditingController();
  final _formKey = GlobalKey<FormState>();
  String? _error;
  bool _busy = false;
  bool _obscureCurrent = true;
  bool _obscureNew = true;
  bool _obscureConfirm = true;

  InputDecoration _passwordDecoration(
      String label, bool obscure, VoidCallback toggle,
      {String? helperText}) {
    return InputDecoration(
      labelText: label,
      helperText: helperText,
      suffixIcon: IconButton(
        tooltip: obscure ? 'Show password' : 'Hide password',
        onPressed: _busy ? null : toggle,
        icon: Icon(obscure
            ? Icons.visibility_outlined
            : Icons.visibility_off_outlined),
      ),
    );
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    final err = await context
        .read<AuthProvider>()
        .changePassword(_current.text, _new.text, _confirm.text);
    if (!mounted) return;
    setState(() => _busy = false);
    if (err != null) {
      setState(() => _error = err);
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Password changed successfully.')));
      if (!widget.forced) Navigator.pop(context);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar:
          widget.forced ? null : AppBar(title: const Text('Change Password')),
      body: Center(
        child: SingleChildScrollView(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 440),
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(28),
                child: Form(
                  key: _formKey,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      if (widget.forced) ...[
                        const Icon(Icons.password, size: 44),
                        const SizedBox(height: 8),
                        Text('Password change required',
                            textAlign: TextAlign.center,
                            style: Theme.of(context).textTheme.titleLarge),
                        const Text(
                            'Your temporary password must be changed before continuing.',
                            textAlign: TextAlign.center),
                        const SizedBox(height: 20),
                      ],
                      TextFormField(
                        controller: _current,
                        obscureText: _obscureCurrent,
                        decoration: _passwordDecoration(
                            'Current Password',
                            _obscureCurrent,
                            () => setState(
                                () => _obscureCurrent = !_obscureCurrent)),
                        validator: (v) =>
                            (v == null || v.isEmpty) ? 'Required' : null,
                      ),
                      const SizedBox(height: 12),
                      TextFormField(
                        controller: _new,
                        obscureText: _obscureNew,
                        decoration: _passwordDecoration(
                            'New Password',
                            _obscureNew,
                            () => setState(() => _obscureNew = !_obscureNew),
                            helperText:
                                'Min 8 chars with uppercase, lowercase and digit'),
                        validator: (v) {
                          if (v == null || v.length < 8)
                            return 'At least 8 characters';
                          if (!v.contains(RegExp(r'[A-Z]')))
                            return 'Needs an uppercase letter';
                          if (!v.contains(RegExp(r'[a-z]')))
                            return 'Needs a lowercase letter';
                          if (!v.contains(RegExp(r'[0-9]')))
                            return 'Needs a digit';
                          return null;
                        },
                      ),
                      const SizedBox(height: 12),
                      TextFormField(
                        controller: _confirm,
                        obscureText: _obscureConfirm,
                        decoration: _passwordDecoration(
                            'Confirm New Password',
                            _obscureConfirm,
                            () => setState(
                                () => _obscureConfirm = !_obscureConfirm)),
                        validator: (v) =>
                            v != _new.text ? 'Passwords do not match' : null,
                        onFieldSubmitted: (_) => _submit(),
                      ),
                      if (_error != null) ...[
                        const SizedBox(height: 10),
                        Text(_error!,
                            style: TextStyle(
                                color: Theme.of(context).colorScheme.error)),
                      ],
                      const SizedBox(height: 18),
                      FilledButton(
                          onPressed: _busy ? null : _submit,
                          child: Text(_busy ? 'Saving...' : 'Change Password')),
                      if (widget.forced)
                        TextButton(
                            onPressed: () =>
                                context.read<AuthProvider>().logout(),
                            child: const Text('Back to Login')),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
