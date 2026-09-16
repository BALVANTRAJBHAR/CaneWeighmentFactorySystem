import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/hindi_transliteration.dart';
import '../../core/api_client.dart';
import '../../providers/auth_provider.dart';

/// User & Role management (Developer/Admin per permissions). No public registration exists.
class UsersScreen extends StatefulWidget {
  const UsersScreen({super.key});
  @override
  State<UsersScreen> createState() => _UsersScreenState();
}

class _UsersScreenState extends State<UsersScreen> {
  List _users = [];
  List _roles = [];
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final u = await ApiClient.instance.dio
          .get('/api/users', queryParameters: {'includeInactive': true});
      if (u.statusCode == 200) _users = u.data['items'];
      final r = await ApiClient.instance.dio.get('/api/roles');
      if (r.statusCode == 200) _roles = r.data;
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  Future<void> _openForm([Map<String, dynamic>? existing]) async {
    final isDeveloper = context.read<AuthProvider>().hasRole('Developer');
    final c = {
      'username': TextEditingController(text: existing?['username'] ?? ''),
      'fullName': TextEditingController(text: existing?['fullName'] ?? ''),
      'fullNameHi': TextEditingController(text: existing?['fullNameHi'] ?? ''),
      'mobile': TextEditingController(text: existing?['mobile'] ?? ''),
      'email': TextEditingController(text: existing?['email'] ?? ''),
      'password': TextEditingController(),
    };
    var fullNameHiEdited = false;
    final selectedRoles = <int>{...List<int>.from(existing?['roleIds'] ?? [])};
    bool status = existing?['status'] ?? true;

    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setD) => AlertDialog(
          title: Text(existing == null
              ? 'New User'
              : 'Edit User ${existing['username']}'),
          content: SizedBox(
            width: 440,
            child: SingleChildScrollView(
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                if (existing == null)
                  TextField(
                      controller: c['username'],
                      decoration: const InputDecoration(
                          labelText: 'Username',
                          hintText: 'Example: operator1')),
                const SizedBox(height: 10),
                TextField(
                    controller: c['fullName'],
                    decoration: const InputDecoration(
                        labelText: 'Full Name',
                        hintText: 'Example: Ram Prasad'),
                    onChanged: (value) {
                      if (!fullNameHiEdited) {
                        c['fullNameHi']!.text = HindiTransliterator.transliterate(value);
                      }
                    }),
                const SizedBox(height: 10),
                TextField(
                    controller: c['fullNameHi'],
                    decoration: const InputDecoration(labelText: 'Full Name (Hindi)', suffixIcon: Icon(Icons.translate)),
                    onChanged: (_) => fullNameHiEdited = true),
                const SizedBox(height: 10),
                TextField(
                    controller: c['mobile'],
                    maxLength: 10,
                    decoration: const InputDecoration(
                        labelText: 'Mobile',
                        hintText: 'Example: 9876543210',
                        counterText: '')),
                const SizedBox(height: 10),
                TextField(
                    controller: c['email'],
                    decoration: const InputDecoration(
                        labelText: 'Email (optional)',
                        hintText: 'Example: user@factory.com')),
                const SizedBox(height: 10),
                if (existing == null)
                  TextField(
                      controller: c['password'],
                      obscureText: true,
                      decoration: const InputDecoration(
                          labelText: 'Temporary Password',
                          helperText: 'User must change it on first login')),
                const SizedBox(height: 8),
                Align(
                    alignment: Alignment.centerLeft,
                    child: Text('Roles:',
                        style: Theme.of(ctx).textTheme.labelLarge)),
                Wrap(spacing: 6, children: [
                  for (final r in _roles.where((r) =>
                      isDeveloper || r['name']?.toString() != 'Developer'))
                    FilterChip(
                      label: Text(r['name']),
                      selected: selectedRoles.contains(r['id']),
                      onSelected: (v) => setD(() => v
                          ? selectedRoles.add(r['id'])
                          : selectedRoles.remove(r['id'])),
                    ),
                ]),
                if (existing != null)
                  SwitchListTile(
                      title: const Text(
                          'Active (deactivation revokes all sessions)'),
                      value: status,
                      onChanged: (v) => setD(() => status = v)),
              ]),
            ),
          ),
          actions: [
            TextButton(
                onPressed: () => Navigator.pop(ctx, false),
                child: const Text('Cancel')),
            FilledButton(
                onPressed: () => Navigator.pop(ctx, true),
                child: const Text('Save')),
          ],
        ),
      ),
    );
    if (ok != true) return;
    final res = existing == null
        ? await ApiClient.instance.dio.post('/api/users', data: {
            'username': c['username']!.text,
            'fullName': c['fullName']!.text,
            'fullNameHi': c['fullNameHi']!.text,
            'mobile': c['mobile']!.text,
            'email': c['email']!.text.isEmpty ? null : c['email']!.text,
            'temporaryPassword': c['password']!.text,
            'roleIds': selectedRoles.toList(),
          })
        : await ApiClient.instance.dio
            .put('/api/users/${existing['id']}', data: {
            'fullName': c['fullName']!.text,
            'fullNameHi': c['fullNameHi']!.text,
            'mobile': c['mobile']!.text,
            'email': c['email']!.text.isEmpty ? null : c['email']!.text,
            'status': status,
            'roleIds': selectedRoles.toList(),
          });
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(res.statusCode == 200
            ? res.data['message']
            : ApiClient.errorMessage(res)),
        backgroundColor: res.statusCode == 200
            ? const Color(0xFF2E7D32)
            : Theme.of(context).colorScheme.error));
    _load();
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Row(children: [
          Text('Users & Roles',
              style: Theme.of(context)
                  .textTheme
                  .titleLarge
                  ?.copyWith(fontWeight: FontWeight.w700)),
          const Spacer(),
          if (auth.can('User.Create'))
            FilledButton.icon(
                onPressed: () => _openForm(),
                icon: const Icon(Icons.person_add_outlined),
                label: const Text('New User')),
        ]),
        const SizedBox(height: 10),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : ListView.separated(
                    itemCount: _users.length,
                    separatorBuilder: (_, __) => const Divider(height: 1),
                    itemBuilder: (_, i) {
                      final u = _users[i];
                      return ListTile(
                        leading: CircleAvatar(
                            child: Text('${u['username']}'
                                .substring(0, 1)
                                .toUpperCase())),
                        title: Text('${u['username']} — ${u['fullName']}'),
                        subtitle: Text(
                            'Roles: ${(u['roles'] as List).join(", ")} • Mobile: ${u['mobile']} • Last login: ${u['lastLoginAt'] ?? 'never'}${u['mustChangePassword'] == true ? ' • PENDING PASSWORD CHANGE' : ''}'),
                        trailing: Wrap(spacing: 4, children: [
                          Chip(
                              label: Text(
                                  u['status'] == true ? 'Active' : 'Inactive',
                                  style: const TextStyle(
                                      fontSize: 10, color: Colors.white)),
                              backgroundColor: u['status'] == true
                                  ? const Color(0xFF2E7D32)
                                  : Colors.grey,
                              visualDensity: VisualDensity.compact),
                          if (auth.can('User.Edit') &&
                              (auth.hasRole('Developer') ||
                                  !(u['roles'] as List).contains('Developer')))
                            IconButton(
                                icon: const Icon(Icons.edit_outlined, size: 18),
                                onPressed: () =>
                                    _openForm(Map<String, dynamic>.from(u))),
                        ]),
                      );
                    },
                  ),
          ),
        ),
      ]),
    );
  }
}
