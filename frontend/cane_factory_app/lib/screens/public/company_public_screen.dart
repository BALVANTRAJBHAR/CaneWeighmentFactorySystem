import 'package:flutter/material.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:url_launcher/url_launcher.dart';
import '../../core/api_client.dart';

class CompanyPublicScreen extends StatefulWidget {
  const CompanyPublicScreen({super.key, this.onLogin});
  final VoidCallback? onLogin;

  @override
  State<CompanyPublicScreen> createState() => _CompanyPublicScreenState();
}

class _CompanyPublicScreenState extends State<CompanyPublicScreen> {
  Map<String, dynamic> company = {};
  int section = 0;
  bool sending = false;
  String serviceType = 'Sugar Mill Services';
  String version = '1.0.0';
  final form = GlobalKey<FormState>();
  final name = TextEditingController();
  final email = TextEditingController();
  final mobile = TextEditingController();
  final business = TextEditingController();
  final requirement = TextEditingController();

  @override
  void initState() {
    super.initState();
    PackageInfo.fromPlatform().then((info) {
      if (mounted)
        setState(() => version = '${info.version} (build ${info.buildNumber})');
    }).catchError((_) {});
    ApiClient.instance.dio.get('/api/public/company').then((response) {
      if (mounted && response.statusCode == 200) {
        setState(() => company = Map<String, dynamic>.from(response.data));
      }
    }).catchError((_) {});
  }

  @override
  void dispose() {
    for (final c in [name, email, mobile, business, requirement]) {
      c.dispose();
    }
    super.dispose();
  }

  String value(String key, [String fallback = '']) =>
      company[key]?.toString().trim().isNotEmpty == true
          ? company[key].toString().trim()
          : fallback;

  Future<void> open(String url) async {
    final uri = Uri.tryParse(url);
    if (uri != null &&
        (uri.scheme == 'https' ||
            uri.scheme == 'tel' ||
            uri.scheme == 'mailto')) {
      await launchUrl(uri, mode: LaunchMode.externalApplication);
    }
  }

  Future<void> send() async {
    if (!(form.currentState?.validate() ?? false)) return;
    setState(() => sending = true);
    String message;
    try {
      final result = await ApiClient.instance.dio
          .post('/api/public/company/quotation', data: {
        'name': name.text.trim(),
        'email': email.text.trim(),
        'mobile': mobile.text.trim(),
        'company': business.text.trim(),
        'requirement': requirement.text.trim(),
        'serviceType': serviceType,
        'website': '',
      });
      message = result.data?['message']?.toString() ?? 'Request sent.';
      if (result.statusCode == 200) form.currentState?.reset();
    } catch (_) {
      message = 'Could not send the request. Please use the contact details.';
    }
    if (mounted) {
      setState(() => sending = false);
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
    }
  }

  Widget input(String label, TextEditingController controller,
          {int lines = 1, bool required = false}) =>
      TextFormField(
        controller: controller,
        maxLines: lines,
        decoration: InputDecoration(
            labelText: label, border: const OutlineInputBorder()),
        validator: required
            ? (text) =>
                text?.trim().isEmpty == true ? '$label is required' : null
            : null,
      );

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final name = value('companyName', 'Sugarcane Factory Management');
    final links = <(String, IconData, String)>[
      (
        'Instagram',
        Icons.camera_alt_outlined,
        value('instagramUrl', 'https://www.instagram.com/')
      ),
      (
        'Facebook',
        Icons.facebook_outlined,
        value('facebookUrl', 'https://www.facebook.com/')
      ),
      ('X', Icons.alternate_email, value('twitterUrl', 'https://x.com/')),
      (
        'YouTube',
        Icons.play_circle_outline,
        value('youtubeUrl', 'https://www.youtube.com/')
      ),
      (
        'LinkedIn',
        Icons.business_center_outlined,
        value('linkedinUrl', 'https://www.linkedin.com/')
      ),
    ];
    return Scaffold(
      appBar: widget.onLogin == null
          ? null
          : AppBar(
              title: Text(name, maxLines: 1, overflow: TextOverflow.ellipsis),
              actions: [
                TextButton.icon(
                    onPressed: widget.onLogin,
                    icon: const Icon(Icons.login),
                    label: const Text('Login'))
              ],
            ),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        Center(
            child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1000),
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Container(
              padding: const EdgeInsets.all(28),
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(24),
                gradient: const LinearGradient(
                    colors: [Color(0xFF163A2C), Color(0xFF2C6845)]),
              ),
              child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Image.network(
                        '${ApiClient.baseUrl}/api/public/company/logo',
                        width: 54,
                        height: 54,
                        errorBuilder: (_, __, ___) => const Icon(
                            Icons.factory_outlined,
                            color: Colors.white,
                            size: 42)),
                    const SizedBox(height: 16),
                    Text(name,
                        style: const TextStyle(
                            color: Colors.white,
                            fontSize: 28,
                            fontWeight: FontWeight.bold)),
                    const SizedBox(height: 8),
                    const Text(
                        'Sugar mill operations, grower services and reliable weighment management.',
                        style: TextStyle(color: Colors.white70, fontSize: 16)),
                  ]),
            ),
            const SizedBox(height: 16),
            Wrap(spacing: 8, runSpacing: 8, children: [
              for (var i = 0; i < 5; i++)
                ChoiceChip(
                    label: Text([
                      'Home',
                      'Services',
                      'Contact',
                      'About Us',
                      'Request Quotation'
                    ][i]),
                    selected: section == i,
                    onSelected: (_) => setState(() => section = i)),
            ]),
            const SizedBox(height: 18),
            Card(
                child: Padding(
                    padding: const EdgeInsets.all(22),
                    child: switch (section) {
                      0 => const Text(
                          'Welcome to our sugar mill. Our platform connects growers, weighment, payments and factory operations in one trusted system.'),
                      1 => const Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text('Services',
                                  style: TextStyle(
                                      fontSize: 22,
                                      fontWeight: FontWeight.bold)),
                              SizedBox(height: 8),
                              Text(
                                  'Grower management • Cane purchase and weighment • Payments • Reports • Factory operations'),
                            ]),
                      2 => Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text('Contact',
                                  style: theme.textTheme.headlineSmall),
                              if (value('address').isNotEmpty)
                                ListTile(
                                    leading:
                                        const Icon(Icons.location_on_outlined),
                                    title: Text(value('address'))),
                              for (final key in [
                                'contactMobile1',
                                'contactMobile2'
                              ])
                                if (value(key).isNotEmpty)
                                  ListTile(
                                      leading: const Icon(Icons.phone_outlined),
                                      title: Text(value(key)),
                                      onTap: () => open('tel:${value(key)}')),
                              if (value('contactEmail').isNotEmpty)
                                ListTile(
                                    leading: const Icon(Icons.email_outlined),
                                    title: Text(value('contactEmail')),
                                    onTap: () => open(
                                        'mailto:${value('contactEmail')}')),
                              const Text('Social media',
                                  style:
                                      TextStyle(fontWeight: FontWeight.bold)),
                              Wrap(spacing: 6, children: [
                                for (final link in links)
                                  ActionChip(
                                      avatar: Icon(link.$2, size: 18),
                                      label: Text(link.$1),
                                      onPressed: () => open(link.$3))
                              ]),
                            ]),
                      3 => Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text('About Us',
                                  style: theme.textTheme.headlineSmall),
                              const SizedBox(height: 8),
                              Text(
                                  '$name is a sugar mill serving growers and supporting transparent cane weighing, purchases and payments.'),
                              const SizedBox(height: 12),
                              Text(value('address')),
                              const SizedBox(height: 12),
                              Text('Application version $version'),
                            ]),
                      _ => Form(
                          key: form,
                          child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                Text('Request a Quotation',
                                    style: theme.textTheme.headlineSmall),
                                const SizedBox(height: 8),
                                const Text(
                                    'No account is required. Your request is emailed to the factory.'),
                                const SizedBox(height: 12),
                                DropdownButtonFormField<String>(
                                  initialValue: serviceType,
                                  decoration: const InputDecoration(
                                      labelText: 'Service'),
                                  items: const [
                                    DropdownMenuItem(
                                        value: 'Sugar Mill Services',
                                        child: Text('Sugar Mill Services')),
                                    DropdownMenuItem(
                                        value: 'Developer / Software',
                                        child: Text('Developer / Software')),
                                    DropdownMenuItem(
                                        value: 'Other', child: Text('Other')),
                                  ],
                                  onChanged: (value) => setState(
                                      () => serviceType = value ?? 'Other'),
                                ),
                                for (final field in <Widget>[
                                  input('Name', this.name, required: true),
                                  input('Company', business),
                                  input('Mobile', mobile, required: true),
                                  input('Email', email),
                                  input('Requirement', requirement,
                                      lines: 5, required: true),
                                ]) ...[const SizedBox(height: 12), field],
                                const SizedBox(height: 14),
                                FilledButton.icon(
                                    onPressed: sending ? null : send,
                                    icon: const Icon(Icons.send_outlined),
                                    label: Text(sending
                                        ? 'Sending...'
                                        : 'Send Request')),
                              ])),
                    })),
          ]),
        )),
      ]),
    );
  }
}
