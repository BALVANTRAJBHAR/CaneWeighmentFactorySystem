import 'package:flutter/material.dart';
import 'package:dio/dio.dart';
import 'package:file_picker/file_picker.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:url_launcher/url_launcher.dart';
import '../../core/api_client.dart';

class CompanyPublicScreen extends StatefulWidget {
  const CompanyPublicScreen(
      {super.key,
      this.onLogin,
      this.initialSection = 0,
      this.showSections = true});
  final VoidCallback? onLogin;
  final int initialSection;
  final bool showSections;

  @override
  State<CompanyPublicScreen> createState() => _CompanyPublicScreenState();
}

class _CompanyPublicScreenState extends State<CompanyPublicScreen> {
  static const services = <(String, IconData, String)>[
    (
      'Farmer & Sugarcane',
      Icons.agriculture_outlined,
      'Sugarcane procurement, grower registration and payment inquiries.'
    ),
    (
      'B2B Sugar Supply',
      Icons.inventory_2_outlined,
      'Bulk sugar supply for businesses and industry.'
    ),
    (
      'Vendor & Supplier',
      Icons.local_shipping_outlined,
      'Equipment, packaging and logistics partnerships.'
    ),
    (
      'Careers',
      Icons.work_outline,
      'Plant, field, engineering and office opportunities.'
    ),
    (
      'Clean Energy',
      Icons.bolt_outlined,
      'Bagasse power and steam supply inquiries.'
    ),
    (
      'General Inquiry',
      Icons.chat_bubble_outline,
      'Media, partnerships and other questions.'
    ),
    (
      'Developer / Software',
      Icons.code_outlined,
      'Software and technology services.'
    ),
  ];

  void requestFor(String category) => setState(() {
        serviceType = category;
        section = 4;
      });

  Map<String, dynamic> company = {};
  late int section;
  bool sending = false;
  String serviceType = 'Formal Quotation';
  PlatformFile? attachment;
  String version = '1.0.0';
  final form = GlobalKey<FormState>();
  final name = TextEditingController();
  final email = TextEditingController();
  final mobile = TextEditingController();
  final business = TextEditingController();
  final village = TextEditingController();
  final position = TextEditingController();
  final productCategory = TextEditingController();
  final requirement = TextEditingController();

  @override
  void initState() {
    super.initState();
    section = widget.initialSection;
    PackageInfo.fromPlatform().then((info) {
      if (mounted) {
        setState(() => version = '${info.version} (build ${info.buildNumber})');
      }
    }).catchError((_) {});
    ApiClient.instance.dio.get('/api/public/company').then((response) {
      if (mounted && response.statusCode == 200) {
        setState(() => company = Map<String, dynamic>.from(response.data));
      }
    }).catchError((_) {});
  }

  @override
  void dispose() {
    for (final c in [
      name,
      email,
      mobile,
      business,
      village,
      position,
      productCategory,
      requirement
    ]) {
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

  Future<void> openAddress(String address) => open(
      'https://www.google.com/maps/search/?api=1&query=${Uri.encodeQueryComponent(address)}');

  Widget aboutFeature(
          BuildContext context, IconData icon, String title, String detail) =>
      Container(
        width: 290,
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          border: Border.all(color: Theme.of(context).dividerColor),
          borderRadius: BorderRadius.circular(14),
        ),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Container(
            padding: const EdgeInsets.all(9),
            decoration: BoxDecoration(
              color: Theme.of(context).colorScheme.primaryContainer,
              borderRadius: BorderRadius.circular(10),
            ),
            child: Icon(icon,
                size: 22,
                color: Theme.of(context).colorScheme.onPrimaryContainer),
          ),
          const SizedBox(width: 12),
          Expanded(
              child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                Text(title,
                    style: const TextStyle(fontWeight: FontWeight.w700)),
                const SizedBox(height: 4),
                Text(detail),
              ])),
        ]),
      );

  Future<void> send() async {
    if (!(form.currentState?.validate() ?? false)) return;
    setState(() => sending = true);
    String message;
    try {
      final data = FormData.fromMap({
        'name': name.text.trim(),
        'email': email.text.trim(),
        'mobile': mobile.text.trim(),
        'company': business.text.trim(),
        'village': village.text.trim(),
        'position': position.text.trim(),
        'productCategory': productCategory.text.trim(),
        'requirement': requirement.text.trim(),
        'serviceType': serviceType,
        'website': '',
      });
      if (attachment != null) {
        data.files.add(MapEntry(
            'attachment',
            MultipartFile.fromBytes(attachment!.bytes!,
                filename: attachment!.name)));
      }
      final result = await ApiClient.instance.dio.post(
          '/api/public/company/quotation',
          data: data,
          options: Options(
              contentType: 'multipart/form-data',
              sendTimeout: const Duration(seconds: 30),
              receiveTimeout: const Duration(seconds: 30)));
      message = result.data?['message']?.toString() ?? 'Request sent.';
      if (result.statusCode == 200) {
        for (final c in [
          name,
          email,
          mobile,
          business,
          village,
          position,
          productCategory,
          requirement
        ]) {
          c.clear();
        }
        if (mounted) {
          setState(() => attachment = null);
        }
      }
    } on DioException catch (error) {
      message = error.response?.data is Map
          ? error.response?.data['message']?.toString() ??
              'Could not send the request.'
          : 'Could not send the request. Please use the contact details.';
    } catch (_) {
      message = 'Could not send the request. Please use the contact details.';
    }
    if (mounted) {
      setState(() => sending = false);
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
    }
  }

  Future<void> pickAttachment() async {
    final picked = await FilePicker.platform.pickFiles(
        type: FileType.custom,
        allowedExtensions: const ['pdf', 'jpg', 'jpeg'],
        withData: true,
        allowMultiple: false);
    if (!mounted || picked == null) return;
    final file = picked.files.single;
    if (file.size > 1000000 || file.bytes == null) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('Select one PDF or JPG smaller than 1 MB.')));
      return;
    }
    setState(() => attachment = file);
  }

  Widget input(String label, TextEditingController controller,
          {int lines = 1,
          bool required = false,
          TextInputType? keyboardType,
          String? Function(String?)? validator}) =>
      TextFormField(
        controller: controller,
        maxLines: lines,
        keyboardType: keyboardType,
        maxLength: lines > 1 ? 3000 : null,
        decoration: InputDecoration(
            labelText: label, border: const OutlineInputBorder()),
        validator: validator ??
            (required
                ? (text) =>
                    text?.trim().isEmpty == true ? '$label is required' : null
                : null),
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
                    label: const Text('Log in'))
              ],
            ),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        Center(
            child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1000),
          child:
              Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            if (widget.showSections)
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
                          style:
                              TextStyle(color: Colors.white70, fontSize: 16)),
                    ]),
              ),
            if (widget.showSections) const SizedBox(height: 16),
            if (widget.showSections)
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
                      0 => Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                  'Built around the people who grow and move sugarcane.',
                                  style: theme.textTheme.headlineSmall),
                              const SizedBox(height: 12),
                              const Text(
                                  'We connect growers, transparent weighment, purchases, payments and factory operations in one place.'),
                              const SizedBox(height: 20),
                              Wrap(spacing: 12, runSpacing: 12, children: [
                                FilledButton.icon(
                                    onPressed: () =>
                                        setState(() => section = 1),
                                    icon: const Icon(Icons.grid_view_outlined),
                                    label: const Text('Explore services')),
                                OutlinedButton.icon(
                                    onPressed: () =>
                                        requestFor('Formal Quotation'),
                                    icon: const Icon(
                                        Icons.request_quote_outlined),
                                    label: const Text('Request quotation')),
                              ]),
                            ]),
                      1 => Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const Text('Services',
                                  style: TextStyle(
                                      fontSize: 22,
                                      fontWeight: FontWeight.bold)),
                              const SizedBox(height: 12),
                              LayoutBuilder(builder: (context, constraints) {
                                final columns = constraints.maxWidth >= 720
                                    ? 3
                                    : constraints.maxWidth >= 440
                                        ? 2
                                        : 1;
                                final width = (constraints.maxWidth -
                                        (columns - 1) * 12) /
                                    columns;
                                return Wrap(
                                    spacing: 12,
                                    runSpacing: 12,
                                    children: [
                                      for (final service in services)
                                        SizedBox(
                                            width: width,
                                            child: Card(
                                                child: InkWell(
                                              onTap: () =>
                                                  requestFor(service.$1),
                                              borderRadius:
                                                  BorderRadius.circular(12),
                                              child: Padding(
                                                  padding:
                                                      const EdgeInsets.all(18),
                                                  child: Column(
                                                      crossAxisAlignment:
                                                          CrossAxisAlignment
                                                              .start,
                                                      children: [
                                                        Icon(service.$2,
                                                            size: 30,
                                                            color: theme
                                                                .colorScheme
                                                                .primary),
                                                        const SizedBox(
                                                            height: 14),
                                                        Text(service.$1,
                                                            style: theme
                                                                .textTheme
                                                                .titleMedium),
                                                        const SizedBox(
                                                            height: 8),
                                                        Text(service.$3),
                                                        const SizedBox(
                                                            height: 12),
                                                        Text('Start inquiry  →',
                                                            style: TextStyle(
                                                                color: theme
                                                                    .colorScheme
                                                                    .primary,
                                                                fontWeight:
                                                                    FontWeight
                                                                        .w600)),
                                                      ])),
                                            ))),
                                    ]);
                              }),
                            ]),
                      2 => Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text('Contact',
                                  style: theme.textTheme.headlineSmall),
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
                              if (value('address').isNotEmpty)
                                ListTile(
                                    leading:
                                        const Icon(Icons.location_on_outlined),
                                    title: Text(value('address')),
                                    subtitle: const Text('Open in Maps'),
                                    onTap: () =>
                                        openAddress(value('address'))),
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
                                  '$name is a sugar mill serving growers and supporting transparent Sugarcane weighing, purchases and payments.',
                                  style: theme.textTheme.titleMedium),
                              const SizedBox(height: 12),
                              const Text(
                                  'From Sugarcane procurement to finished products, we bring grower services, accurate weighment and traceable business operations together in one dependable system.'),
                              const SizedBox(height: 20),
                              Text('Our products',
                                  style: theme.textTheme.titleMedium?.copyWith(
                                      fontWeight: FontWeight.bold)),
                              const SizedBox(height: 10),
                              const Wrap(spacing: 8, runSpacing: 8, children: [
                                Chip(
                                    avatar: Icon(Icons.inventory_2_outlined,
                                        size: 18),
                                    label: Text('Sugar')),
                                Chip(
                                    avatar:
                                        Icon(Icons.hexagon_outlined, size: 18),
                                    label: Text('Jaggery (Gur)')),
                                Chip(
                                    avatar:
                                        Icon(Icons.grain_outlined, size: 18),
                                    label: Text('Khandsari')),
                                Chip(
                                    avatar:
                                        Icon(Icons.water_drop_outlined, size: 18),
                                    label: Text('Molasses')),
                                Chip(
                                    avatar: Icon(Icons.energy_savings_leaf_outlined,
                                        size: 18),
                                    label: Text('Bagasse')),
                              ]),
                              const SizedBox(height: 20),
                              Wrap(spacing: 12, runSpacing: 12, children: [
                                aboutFeature(
                                    context,
                                    Icons.agriculture_outlined,
                                    'Grower partnership',
                                    'Organized Sugarcane procurement, grower support and clear payment information.'),
                                aboutFeature(
                                    context,
                                    Icons.scale_outlined,
                                    'Transparent operations',
                                    'Digitized weighment, traceable records and dependable transaction workflows.'),
                                aboutFeature(
                                    context,
                                    Icons.factory_outlined,
                                    'Sugar mill products',
                                    'Sugar and traditional products, together with useful by-products such as molasses and bagasse.'),
                              ]),
                              if (value('address').isNotEmpty) ...[
                                const SizedBox(height: 20),
                                ListTile(
                                  contentPadding: EdgeInsets.zero,
                                  leading:
                                      const Icon(Icons.location_on_outlined),
                                  title: Text(value('address')),
                                  subtitle: const Text('View factory location'),
                                  onTap: () => openAddress(value('address')),
                                ),
                              ],
                              const Divider(height: 24),
                              Text('Application version $version',
                                  style: theme.textTheme.bodySmall),
                            ]),
                      _ => Form(
                          key: form,
                          child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                Row(children: [
                                  if (serviceType != 'Formal Quotation') ...[
                                    IconButton(
                                      tooltip: 'Back to Services',
                                      onPressed: () =>
                                          setState(() => section = 1),
                                      icon: const Icon(Icons.arrow_back),
                                    ),
                                    const SizedBox(width: 4),
                                  ],
                                  Expanded(
                                    child: Text(
                                        serviceType == 'Formal Quotation'
                                            ? 'Request a Quotation'
                                            : serviceType,
                                        style: theme.textTheme.headlineSmall),
                                  ),
                                ]),
                                const SizedBox(height: 8),
                                const Text(
                                    'No account is required. Your request is emailed to the factory.'),
                                const SizedBox(height: 12),
                                DropdownButtonFormField<String>(
                                  key: ValueKey(serviceType),
                                  initialValue: serviceType,
                                  decoration: const InputDecoration(
                                      labelText: 'Inquiry category'),
                                  items: [
                                    ...services.map((s) => DropdownMenuItem(
                                        value: s.$1, child: Text(s.$1))),
                                    const DropdownMenuItem(
                                        value: 'Formal Quotation',
                                        child: Text('Formal Quotation')),
                                  ],
                                  onChanged: (value) => setState(() =>
                                      serviceType =
                                          value ?? 'Formal Quotation'),
                                ),
                                for (final field in <Widget>[
                                  input('Full name / contact person', this.name,
                                      required: true),
                                  if (serviceType == 'Vendor & Supplier' ||
                                      serviceType == 'B2B Sugar Supply' ||
                                      serviceType == 'Formal Quotation' ||
                                      serviceType == 'General Inquiry')
                                    input('Company / organization', business,
                                        required:
                                            serviceType != 'General Inquiry'),
                                  input('Mobile (India)', mobile,
                                      keyboardType: TextInputType.phone,
                                      validator: (text) => RegExp(
                                                  r'^[6-9][0-9]{9}$')
                                              .hasMatch(text?.trim() ?? '')
                                          ? null
                                          : 'Enter a valid 10-digit mobile number'),
                                  if (serviceType != 'Vendor & Supplier' &&
                                      serviceType != 'General Inquiry')
                                    input('Email address', email,
                                        keyboardType:
                                            TextInputType.emailAddress,
                                        required: serviceType ==
                                                'B2B Sugar Supply' ||
                                            serviceType == 'Formal Quotation' ||
                                            serviceType == 'Careers'),
                                  if (serviceType == 'Farmer & Sugarcane')
                                    input('Village', village, required: true),
                                  if (serviceType == 'Careers')
                                    input('Position applied for', position,
                                        required: true),
                                  if (serviceType == 'B2B Sugar Supply' ||
                                      serviceType == 'Formal Quotation')
                                    input('Product / service category',
                                        productCategory,
                                        required: true),
                                  input('Requirement / message', requirement,
                                      lines: 5, required: true),
                                ]) ...[const SizedBox(height: 12), field],
                                const SizedBox(height: 12),
                                OutlinedButton.icon(
                                  onPressed: pickAttachment,
                                  icon: const Icon(Icons.attach_file),
                                  label: Text(
                                      attachment == null
                                          ? 'Attachment (optional) · PDF or JPG · max 1 MB'
                                          : attachment!.name,
                                      overflow: TextOverflow.ellipsis),
                                ),
                                if (attachment != null)
                                  TextButton.icon(
                                      onPressed: () =>
                                          setState(() => attachment = null),
                                      icon: const Icon(Icons.close),
                                      label: const Text('Remove attachment')),
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
