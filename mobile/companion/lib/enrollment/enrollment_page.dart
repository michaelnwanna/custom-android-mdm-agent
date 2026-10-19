import 'dart:async';

import 'package:flutter/material.dart';
import 'package:qr_flutter/qr_flutter.dart';

import '../api/fleet_api_client.dart';
import '../app_config.dart';
import 'provisioning_payload.dart';

typedef CreatePendingEnrollment = Future<PendingEnrollment> Function(
  Duration lifetime,
);

class EnrollmentPage extends StatefulWidget {
  const EnrollmentPage({
    required this.config,
    required this.createPendingEnrollment,
    this.now = DateTime.now,
    super.key,
  });

  final AppConfig config;
  final CreatePendingEnrollment createPendingEnrollment;
  final DateTime Function() now;

  @override
  State<EnrollmentPage> createState() => _EnrollmentPageState();
}

class _EnrollmentPageState extends State<EnrollmentPage> {
  final _ssid = TextEditingController();
  final _password = TextEditingController();
  Duration _lifetime = const Duration(minutes: 10);
  bool _busy = false;
  String? _error;
  String? _payload;
  DateTime? _expiresAt;
  Timer? _timer;

  @override
  void dispose() {
    _timer?.cancel();
    _ssid.dispose();
    _password.dispose();
    super.dispose();
  }

  Duration get _remaining {
    final expiresAt = _expiresAt;
    if (expiresAt == null) return Duration.zero;
    final remaining = expiresAt.difference(widget.now().toUtc());
    return remaining.isNegative ? Duration.zero : remaining;
  }

  Future<void> _generate() async {
    setState(() {
      _busy = true;
      _error = null;
      _payload = null;
    });
    try {
      final enrollment = await widget.createPendingEnrollment(_lifetime);
      if (!mounted) return;
      setState(() {
        _payload = buildProvisioningPayload(
          config: widget.config,
          enrollment: enrollment,
          wifiSsid: _ssid.text,
          wifiPassword: _password.text,
        );
        _expiresAt = enrollment.expiresAt;
        _busy = false;
      });
      _timer?.cancel();
      _timer = Timer.periodic(const Duration(seconds: 1), (_) => _tick());
    } on FleetApiException catch (error) {
      _fail(error.message);
    } catch (error, stackTrace) {
      debugPrint('Pending enrollment failed: $error\n$stackTrace');
      _fail('Could not create an enrollment. Check your connection.');
    }
  }

  void _fail(String message) {
    if (!mounted) return;
    setState(() {
      _error = message;
      _busy = false;
    });
  }

  void _tick() {
    if (!mounted) return;
    setState(() {
      if (_remaining == Duration.zero) _clear();
    });
  }

  void _clear() {
    _timer?.cancel();
    _timer = null;
    _payload = null;
    _expiresAt = null;
  }

  @override
  Widget build(BuildContext context) {
    final payload = _payload;
    return Scaffold(
      appBar: AppBar(title: const Text('Enroll a device')),
      body: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          if (!widget.config.isQrEnrollmentConfigured)
            const Text(
              'QR enrollment is not configured. Set AGENT_APK_URL, '
              'AGENT_SIGNATURE_CHECKSUM, SERVER_ROOT_CA and DEVICE_ROOT_CA '
              'and use an HTTPS API_BASE_URL (see README).',
            )
          else if (payload != null) ...[
            Center(
              child: Container(
                color: Colors.white,
                padding: const EdgeInsets.all(12),
                child: QrImageView(
                  data: payload,
                  errorCorrectionLevel: QrErrorCorrectLevel.L,
                  size: 320,
                  semanticsLabel: 'Device enrollment QR code',
                  errorStateBuilder: (_, _) => const Text(
                    'The enrollment data is too large for a QR code. '
                    'Use P-256 (EC) root certificates.',
                  ),
                ),
              ),
            ),
            const SizedBox(height: 16),
            Text(
              'Expires in ${_format(_remaining)}',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            const Text(
              'On a factory-reset Android device, tap the welcome screen six '
              'times and scan this code. It can be used once.',
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 16),
            OutlinedButton.icon(
              onPressed: () => setState(_clear),
              icon: const Icon(Icons.close),
              label: const Text('Hide code'),
            ),
          ] else ...[
            const Text(
              'Generates a single-use Android Enterprise QR code that installs '
              'the fleet agent as Device Owner and enrolls the device.',
            ),
            const SizedBox(height: 16),
            DropdownButtonFormField<Duration>(
              initialValue: _lifetime,
              decoration: const InputDecoration(labelText: 'Code lifetime'),
              items: const [
                DropdownMenuItem(
                  value: Duration(minutes: 5),
                  child: Text('5 minutes'),
                ),
                DropdownMenuItem(
                  value: Duration(minutes: 10),
                  child: Text('10 minutes'),
                ),
                DropdownMenuItem(
                  value: Duration(minutes: 15),
                  child: Text('15 minutes'),
                ),
              ],
              onChanged: _busy
                  ? null
                  : (value) => setState(() => _lifetime = value ?? _lifetime),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _ssid,
              enabled: !_busy,
              decoration: const InputDecoration(
                labelText: 'Wi-Fi network (optional)',
              ),
            ),
            TextField(
              controller: _password,
              enabled: !_busy,
              obscureText: true,
              decoration: const InputDecoration(
                labelText: 'Wi-Fi password (optional)',
              ),
            ),
            const SizedBox(height: 24),
            FilledButton.icon(
              onPressed: _busy ? null : _generate,
              icon: _busy
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.qr_code_2),
              label: const Text('Generate enrollment code'),
            ),
          ],
          if (_error case final error?) ...[
            const SizedBox(height: 16),
            Text(
              error,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ],
        ],
      ),
    );
  }

  static String _format(Duration duration) {
    final minutes = duration.inMinutes;
    final seconds = (duration.inSeconds % 60).toString().padLeft(2, '0');
    return '$minutes:$seconds';
  }
}
