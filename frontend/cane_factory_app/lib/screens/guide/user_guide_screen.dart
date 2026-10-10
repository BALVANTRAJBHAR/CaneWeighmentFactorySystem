import 'package:flutter/material.dart';
import '../../core/api_client.dart';

/// Permission-aware operating guide. The API returns role notes plus detailed
/// workflows only for menus available to the signed-in user.
class UserGuideScreen extends StatefulWidget {
  const UserGuideScreen({super.key});

  @override
  State<UserGuideScreen> createState() => _UserGuideScreenState();
}

class _UserGuideScreenState extends State<UserGuideScreen> {
  List<dynamic> _guides = [];
  bool _loading = true;
  String? _error;
  String _query = '';
  final _scrollController = ScrollController();

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _scrollController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await ApiClient.instance.dio.get('/api/user-guide');
      if (!mounted) return;
      if (response.statusCode == 200 && response.data is List) {
        _guides = List<dynamic>.from(response.data);
      } else {
        _error = ApiClient.errorMessage(response);
      }
    } catch (error) {
      if (!mounted) return;
      _error = ApiClient.exceptionMessage(
          error, 'Could not load the user guide. Please try again.');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  List<dynamic> _matchingSections(dynamic guide) {
    final sections = List<dynamic>.from(guide['sections'] ?? const []);
    final query = _query.trim().toLowerCase();
    if (query.isEmpty) return sections;
    return sections.where((section) {
      final title = '${section['title'] ?? ''}'.toLowerCase();
      final steps = List<dynamic>.from(section['steps'] ?? const [])
          .join(' ')
          .toLowerCase();
      return title.contains(query) || steps.contains(query);
    }).toList();
  }

  IconData _iconFor(String title) {
    final value = title.toLowerCase();
    if (value.contains('gross') ||
        value.contains('tare') ||
        value.contains('weighment')) {
      return Icons.scale_outlined;
    }
    if (value.contains('sale')) return Icons.local_shipping_outlined;
    if (value.contains('grower') || value.contains('farmer')) {
      return Icons.agriculture_outlined;
    }
    if (value.contains('master')) return Icons.folder_open_outlined;
    if (value.contains('purchase')) return Icons.receipt_long_outlined;
    if (value.contains('payment')) return Icons.payments_outlined;
    if (value.contains('cash')) return Icons.menu_book_outlined;
    if (value.contains('expense')) {
      return Icons.account_balance_wallet_outlined;
    }
    if (value.contains('loan')) return Icons.savings_outlined;
    if (value.contains('report')) return Icons.summarize_outlined;
    if (value.contains('image') || value.contains('camera')) {
      return Icons.photo_library_outlined;
    }
    if (value.contains('print')) return Icons.print_outlined;
    if (value.contains('user') || value.contains('security')) {
      return Icons.group_outlined;
    }
    if (value.contains('device') || value.contains('digitizer')) {
      return Icons.settings_input_component_outlined;
    }
    if (value.contains('configuration') || value.contains('rule')) {
      return Icons.tune_outlined;
    }
    if (value.contains('audit')) return Icons.history_outlined;
    if (value.contains('failure')) return Icons.troubleshoot_outlined;
    return Icons.menu_book_outlined;
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const Center(child: CircularProgressIndicator());
    if (_error != null) {
      return Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: Card(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                Icon(Icons.cloud_off_outlined,
                    size: 48, color: Theme.of(context).colorScheme.error),
                const SizedBox(height: 12),
                Text(_error!, textAlign: TextAlign.center),
                const SizedBox(height: 16),
                FilledButton.icon(
                    onPressed: _load,
                    icon: const Icon(Icons.refresh),
                    label: const Text('Retry')),
              ]),
            ),
          ),
        ),
      );
    }

    final visible = <MapEntry<dynamic, List<dynamic>>>[];
    for (final guide in _guides) {
      final sections = _matchingSections(guide);
      if (sections.isNotEmpty) visible.add(MapEntry(guide, sections));
    }

    return Scrollbar(
      controller: _scrollController,
      thumbVisibility: true,
      child: ListView(
        controller: _scrollController,
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            color: Theme.of(context).colorScheme.primaryContainer,
            child: Padding(
              padding: const EdgeInsets.all(16),
              child:
                  Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                CircleAvatar(
                    radius: 24,
                    backgroundColor: Theme.of(context).colorScheme.primary,
                    foregroundColor: Theme.of(context).colorScheme.onPrimary,
                    child: const Icon(Icons.school_outlined)),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('User Guide',
                            style: Theme.of(context)
                                .textTheme
                                .headlineSmall
                                ?.copyWith(fontWeight: FontWeight.w800)),
                        const SizedBox(height: 4),
                        const Text(
                            'Your role instructions and step-by-step workflows for the menus available to your account.'),
                      ]),
                ),
              ]),
            ),
          ),
          const SizedBox(height: 10),
          TextField(
            decoration: const InputDecoration(
              labelText: 'Search guide',
              hintText: 'Example: Gross, Tare, Payment, Print, Report...',
              prefixIcon: Icon(Icons.search),
            ),
            onChanged: (value) => setState(() => _query = value),
          ),
          if (visible.isEmpty)
            const Padding(
              padding: EdgeInsets.all(40),
              child:
                  Center(child: Text('No guide section matches your search.')),
            ),
          for (final entry in visible) ...[
            const SizedBox(height: 18),
            Row(children: [
              Icon(Icons.verified_user_outlined,
                  color: Theme.of(context).colorScheme.primary),
              const SizedBox(width: 8),
              Expanded(
                child: Text('${entry.key['role']}',
                    style: Theme.of(context).textTheme.titleLarge?.copyWith(
                        color: Theme.of(context).colorScheme.primary,
                        fontWeight: FontWeight.w800)),
              ),
            ]),
            const SizedBox(height: 6),
            for (final section in entry.value)
              Card(
                clipBehavior: Clip.antiAlias,
                child: ExpansionTile(
                  leading: CircleAvatar(
                    backgroundColor:
                        Theme.of(context).colorScheme.primaryContainer,
                    foregroundColor: Theme.of(context).colorScheme.primary,
                    child: Icon(_iconFor('${section['title']}')),
                  ),
                  title: Text('${section['title']}',
                      style: const TextStyle(
                          fontWeight: FontWeight.w700, fontSize: 14)),
                  childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 14),
                  children: [
                    for (final indexed
                        in List<dynamic>.from(section['steps'] ?? const [])
                            .indexed)
                      Padding(
                        padding: const EdgeInsets.only(top: 10),
                        child: Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              CircleAvatar(
                                radius: 12,
                                backgroundColor: Theme.of(context)
                                    .colorScheme
                                    .secondaryContainer,
                                child: Text('${indexed.$1 + 1}',
                                    style: const TextStyle(
                                        fontSize: 11,
                                        fontWeight: FontWeight.w700)),
                              ),
                              const SizedBox(width: 10),
                              Expanded(
                                  child: SelectableText('${indexed.$2}',
                                      style: const TextStyle(height: 1.4))),
                            ]),
                      ),
                  ],
                ),
              ),
          ],
        ],
      ),
    );
  }
}
