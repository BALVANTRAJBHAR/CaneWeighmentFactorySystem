import 'dart:async';

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
  final _search = TextEditingController();
  Timer? _searchDebounce;
  String _category = 'SYSTEM';
  String _roleName = '';
  int _page = 1;
  int _totalCount = 0;
  static const int _pageSize = 50;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _searchDebounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  Future<void> _load({bool resetPage = false}) async {
    if (resetPage) _page = 1;
    if (mounted) setState(() => _loading = true);
    try {
      final responses = await Future.wait([
        ApiClient.instance.dio.get('/api/users', queryParameters: {
          'includeInactive': true,
          'category': _category,
          if (_roleName.isNotEmpty) 'role': _roleName,
          if (_search.text.trim().isNotEmpty) 'search': _search.text.trim(),
          'page': _page,
          'pageSize': _pageSize,
        }),
        if (_roles.isEmpty) ApiClient.instance.dio.get('/api/roles'),
      ]);
      final usersResponse = responses.first;
      if (usersResponse.statusCode == 200) {
        _users = usersResponse.data['items'];
        _totalCount = (usersResponse.data['totalCount'] as num?)?.toInt() ?? 0;
      }
      if (responses.length > 1 && responses[1].statusCode == 200) {
        _roles = responses[1].data;
      }
    } catch (_) {}
    if (mounted) setState(() => _loading = false);
  }

  void _searchChanged(String _) {
    _searchDebounce?.cancel();
    _searchDebounce =
        Timer(const Duration(milliseconds: 350), () => _load(resetPage: true));
  }

  Future<void> _manageFarmer(Map<String, dynamic> farmer) async {
    final password = TextEditingController();
    var obscure = true;
    var saving = false;
    String? error;
    final changed = await showDialog<bool>(
      context: context,
      barrierDismissible: !saving,
      builder: (ctx) => StatefulBuilder(builder: (ctx, setDialogState) {
        Future<void> resetPassword() async {
          final temporaryPassword = password.text;
          if (temporaryPassword.length < 8) {
            setDialogState(() =>
                error = 'Temporary password must be at least 8 characters.');
            return;
          }
          setDialogState(() {
            saving = true;
            error = null;
          });
          try {
            final response = await ApiClient.instance.dio.post(
                '/api/users/${farmer['id']}/reset-password',
                data: {'temporaryPassword': temporaryPassword});
            if (!ctx.mounted) return;
            if (response.statusCode == 200) {
              Navigator.pop(ctx, true);
            } else {
              setDialogState(() {
                saving = false;
                error = ApiClient.errorMessage(response);
              });
            }
          } catch (exception) {
            if (ctx.mounted) {
              setDialogState(() {
                saving = false;
                error = ApiClient.exceptionMessage(exception);
              });
            }
          }
        }

        return AlertDialog(
          title: const Text('Manage Farmer User'),
          content: SizedBox(
            width: 460,
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              ListTile(
                contentPadding: EdgeInsets.zero,
                leading: const Icon(Icons.agriculture_outlined),
                title: Text('${farmer['fullName']}'),
                subtitle: Text(
                    'Grower ID: ${farmer['growerId']}  •  Mobile: ${farmer['mobile']}\nUsername: ${farmer['username']}'),
              ),
              const Align(
                alignment: Alignment.centerLeft,
                child: Text(
                    'Name and mobile remain linked to Grower Master. Use this screen to securely reset login access.'),
              ),
              const SizedBox(height: 14),
              TextField(
                controller: password,
                obscureText: obscure,
                enabled: !saving,
                decoration: InputDecoration(
                  labelText: 'New Temporary Password',
                  helperText:
                      'The farmer must change this password on the next login.',
                  suffixIcon: IconButton(
                    onPressed: saving
                        ? null
                        : () => setDialogState(() => obscure = !obscure),
                    icon: Icon(obscure
                        ? Icons.visibility_outlined
                        : Icons.visibility_off_outlined),
                  ),
                ),
              ),
              if (error != null)
                Padding(
                  padding: const EdgeInsets.only(top: 10),
                  child: Text(error!,
                      style: TextStyle(color: Theme.of(ctx).colorScheme.error)),
                ),
            ]),
          ),
          actions: [
            TextButton(
                onPressed: saving ? null : () => Navigator.pop(ctx, false),
                child: const Text('Close')),
            FilledButton.icon(
                onPressed: saving ? null : resetPassword,
                icon: saving
                    ? const SizedBox(
                        width: 16,
                        height: 16,
                        child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.password_outlined),
                label: const Text('Reset Password')),
          ],
        );
      }),
    );
    password.dispose();
    if (changed == true && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text(
              'Farmer password reset. Existing sessions were revoked and password change is required at next login.'),
          backgroundColor: Color(0xFF2E7D32)));
      _load();
    }
  }

  Future<void> _openForm([Map<String, dynamic>? existing]) async {
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
                        c['fullNameHi']!.text =
                            HindiTransliterator.transliterate(value);
                      }
                    }),
                const SizedBox(height: 10),
                TextField(
                    controller: c['fullNameHi'],
                    decoration: const InputDecoration(
                        labelText: 'Full Name (Hindi)',
                        suffixIcon: Icon(Icons.translate)),
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
                      r['name']?.toString() != 'Developer' &&
                      r['name']?.toString() != 'Farmer'))
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
    // Defense in depth: hide the reserved Developer identity locally for
    // non-Developer sessions even if an older API deployment returns it.
    final visibleUsers = auth.hasRole('Developer')
        ? _users
        : _users
            .where((u) => !(u['roles'] as List).contains('Developer'))
            .toList();
    final selectableRoles = _roles.where((role) {
      final name = role['name']?.toString() ?? '';
      if (name == 'Farmer') return _category != 'SYSTEM';
      if (name == 'Developer' && !auth.hasRole('Developer')) return false;
      return _category != 'FARMER';
    }).toList();
    final firstResult = _totalCount == 0 ? 0 : ((_page - 1) * _pageSize) + 1;
    final lastResult = (_page * _pageSize).clamp(0, _totalCount);
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
        Wrap(spacing: 10, runSpacing: 10, children: [
          SizedBox(
            width: 190,
            child: DropdownButtonFormField<String>(
              key: ValueKey('category-$_category'),
              initialValue: _category,
              isExpanded: true,
              decoration: const InputDecoration(
                  labelText: 'User Category',
                  prefixIcon: Icon(Icons.category_outlined)),
              items: const [
                DropdownMenuItem(value: 'SYSTEM', child: Text('System Users')),
                DropdownMenuItem(value: 'FARMER', child: Text('Farmers')),
                DropdownMenuItem(value: 'ALL', child: Text('All Users')),
              ],
              onChanged: (value) {
                if (value == null) return;
                setState(() {
                  _category = value;
                  _roleName = '';
                });
                _load(resetPage: true);
              },
            ),
          ),
          SizedBox(
            width: 190,
            child: DropdownButtonFormField<String>(
              key: ValueKey('role-$_category-$_roleName'),
              initialValue: _roleName,
              isExpanded: true,
              decoration: const InputDecoration(
                  labelText: 'Specific Role',
                  prefixIcon: Icon(Icons.admin_panel_settings_outlined)),
              items: [
                DropdownMenuItem(
                    value: '',
                    child: Text(
                        _category == 'FARMER' ? 'All Farmers' : 'All Roles')),
                for (final role in selectableRoles)
                  DropdownMenuItem(
                      value: role['name'].toString(),
                      child: Text(role['name'].toString())),
              ],
              onChanged: _category == 'FARMER'
                  ? null
                  : (value) {
                      setState(() => _roleName = value ?? '');
                      _load(resetPage: true);
                    },
            ),
          ),
          SizedBox(
            width: 390,
            child: TextField(
              controller: _search,
              onChanged: _searchChanged,
              onSubmitted: (_) => _load(resetPage: true),
              decoration: InputDecoration(
                labelText: 'Search',
                hintText: 'Name, mobile, username, User ID or Grower ID',
                prefixIcon: const Icon(Icons.search),
                suffixIcon: _search.text.isEmpty
                    ? null
                    : IconButton(
                        tooltip: 'Clear search',
                        onPressed: () {
                          _search.clear();
                          setState(() {});
                          _load(resetPage: true);
                        },
                        icon: const Icon(Icons.clear)),
              ),
            ),
          ),
        ]),
        if (_category == 'FARMER')
          const Padding(
            padding: EdgeInsets.only(top: 7),
            child: Text(
                'Only farmers who have logged in at least once are shown. Auto-created accounts that never logged in remain hidden.'),
          ),
        const SizedBox(height: 10),
        Expanded(
          child: Card(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : Column(children: [
                    Expanded(
                      child: visibleUsers.isEmpty
                          ? const Center(
                              child: Text('No matching users found.'))
                          : ListView.separated(
                              itemCount: visibleUsers.length,
                              separatorBuilder: (_, __) =>
                                  const Divider(height: 1),
                              itemBuilder: (_, i) {
                                final u = visibleUsers[i];
                                final username = '${u['username']}';
                                final isFarmer =
                                    (u['roles'] as List).contains('Farmer');
                                return ListTile(
                                  leading: CircleAvatar(
                                      child: Text(username.isEmpty
                                          ? '?'
                                          : username
                                              .substring(0, 1)
                                              .toUpperCase())),
                                  title: Text(
                                      '$username — ${u['fullName']}${isFarmer ? ' (Grower ${u['growerId']})' : ''}'),
                                  subtitle: Text(
                                      'Roles: ${(u['roles'] as List).join(", ")} • Mobile: ${u['mobile']} • Last login: ${u['lastLoginAt'] ?? 'never'}${u['mustChangePassword'] == true ? ' • PASSWORD CHANGE REQUIRED' : ''}'),
                                  trailing: Wrap(spacing: 4, children: [
                                    Chip(
                                        label: Text(
                                            u['status'] == true
                                                ? 'Active'
                                                : 'Inactive',
                                            style: const TextStyle(
                                                fontSize: 10,
                                                color: Colors.white)),
                                        backgroundColor: u['status'] == true
                                            ? const Color(0xFF2E7D32)
                                            : Colors.grey,
                                        visualDensity: VisualDensity.compact),
                                    if (auth.can('User.Edit') &&
                                        !(u['roles'] as List)
                                            .contains('Developer'))
                                      IconButton(
                                          tooltip: isFarmer
                                              ? 'Manage farmer login / reset password'
                                              : 'Edit user',
                                          icon: const Icon(Icons.edit_outlined,
                                              size: 18),
                                          onPressed: () => isFarmer
                                              ? _manageFarmer(
                                                  Map<String, dynamic>.from(u))
                                              : _openForm(
                                                  Map<String, dynamic>.from(
                                                      u))),
                                  ]),
                                );
                              },
                            ),
                    ),
                    const Divider(height: 1),
                    Padding(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 12, vertical: 6),
                      child: Row(children: [
                        Text(
                            'Showing $firstResult–$lastResult of $_totalCount'),
                        const Spacer(),
                        IconButton(
                            tooltip: 'Previous page',
                            onPressed: _page <= 1
                                ? null
                                : () {
                                    _page--;
                                    _load();
                                  },
                            icon: const Icon(Icons.chevron_left)),
                        Text('Page $_page'),
                        IconButton(
                            tooltip: 'Next page',
                            onPressed: lastResult >= _totalCount
                                ? null
                                : () {
                                    _page++;
                                    _load();
                                  },
                            icon: const Icon(Icons.chevron_right)),
                      ]),
                    ),
                  ]),
          ),
        ),
      ]),
    );
  }
}
