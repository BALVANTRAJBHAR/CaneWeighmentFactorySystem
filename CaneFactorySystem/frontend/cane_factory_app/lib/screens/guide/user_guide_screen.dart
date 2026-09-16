import 'package:flutter/material.dart';
import '../../core/api_client.dart';

/// Role-specific User Guide - the server returns only the guides for the caller's roles.
class UserGuideScreen extends StatefulWidget {
  const UserGuideScreen({super.key});
  @override
  State<UserGuideScreen> createState() => _UserGuideScreenState();
}

class _UserGuideScreenState extends State<UserGuideScreen> {
  List _guides = [];
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    ApiClient.instance.dio.get('/api/user-guide').then((res) {
      if (mounted) {
        setState(() {
          if (res.statusCode == 200) _guides = res.data;
          _loading = false;
        });
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const Center(child: CircularProgressIndicator());
    return ListView(padding: const EdgeInsets.all(16), children: [
      Text('User Guide', style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w700)),
      for (final guide in _guides) ...[
        const SizedBox(height: 12),
        Text('${guide['role']} Guide',
            style: Theme.of(context)
                .textTheme
                .titleLarge
                ?.copyWith(color: Theme.of(context).colorScheme.primary, fontWeight: FontWeight.w700)),
        for (final section in guide['sections'])
          Card(
            child: ExpansionTile(
              title: Text('${section['title']}', style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14)),
              childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
              children: [
                for (final step in section['steps'])
                  Padding(
                    padding: const EdgeInsets.only(bottom: 6),
                    child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      const Text('•  '),
                      Expanded(child: Text('$step')),
                    ]),
                  ),
              ],
            ),
          ),
      ],
    ]);
  }
}
