import 'package:flutter/foundation.dart';

class AppConfig {
  const AppConfig({
    required this.entraTenantId,
    required this.entraClientId,
    required this.apiScope,
    required this.apiBaseUrl,
    required this.androidSignatureHash,
    this.agentApkUrl = '',
    this.agentSignatureChecksum = '',
    this.serverRootCa = '',
    this.deviceRootCa = '',
  });

  const AppConfig.fromEnvironment()
    : entraTenantId = const String.fromEnvironment('ENTRA_TENANT_ID'),
      entraClientId = const String.fromEnvironment('ENTRA_CLIENT_ID'),
      apiScope = const String.fromEnvironment('ENTRA_API_SCOPE'),
      apiBaseUrl = const String.fromEnvironment('API_BASE_URL'),
      androidSignatureHash = const String.fromEnvironment(
        'ENTRA_ANDROID_SIGNATURE_HASH',
      ),
      agentApkUrl = const String.fromEnvironment('AGENT_APK_URL'),
      agentSignatureChecksum = const String.fromEnvironment(
        'AGENT_SIGNATURE_CHECKSUM',
      ),
      serverRootCa = const String.fromEnvironment('SERVER_ROOT_CA'),
      deviceRootCa = const String.fromEnvironment('DEVICE_ROOT_CA');

  // Must match applicationId in android/app/build.gradle.kts.
  static const androidPackageName = 'com.michaelnwanna.mdm_companion';

  static final _guid = RegExp(
    r'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$',
    caseSensitive: false,
  );
  static final _tenantDomain = RegExp(
    r'^[a-z0-9-]+(\.[a-z0-9-]+)+$',
    caseSensitive: false,
  );
  static final _signatureHash = RegExp(r'^[A-Za-z0-9+/]{27}=$');

  final String entraTenantId;
  final String entraClientId;
  final String apiScope;
  final String apiBaseUrl;
  final String androidSignatureHash;

  /// HTTPS download URL of the agent APK used by Android Enterprise QR provisioning.
  final String agentApkUrl;

  /// Base64url SHA-256 of the agent signing certificate (no padding).
  final String agentSignatureChecksum;

  /// Base64 DER CA certificate the agent trusts for the HTTPS server.
  final String serverRootCa;

  /// Base64 DER root the agent trusts for issued device certificates.
  final String deviceRootCa;

  static final _base64Url43 = RegExp(r'^[A-Za-z0-9_-]{43}$');
  static final _base64Der = RegExp(r'^[A-Za-z0-9+/]+={0,2}$');

  String get authority =>
      'https://login.microsoftonline.com/${entraTenantId.trim()}';

  String get androidRedirectUri =>
      'msauth://$androidPackageName/'
      '${Uri.encodeComponent(androidSignatureHash.trim())}';

  Uri get enrollmentEndpoint =>
      Uri.parse(apiBaseUrl.trim()).replace(path: '/api/v1/enrollments');

  /// Whether QR enrollment can be offered. The device endpoint must be HTTPS.
  bool get isQrEnrollmentConfigured {
    final apk = Uri.tryParse(agentApkUrl.trim());
    return _isSecureApiBaseUrl(apiBaseUrl) &&
        Uri.parse(apiBaseUrl.trim()).scheme == 'https' &&
        apk != null &&
        apk.scheme == 'https' &&
        apk.host.isNotEmpty &&
        apk.userInfo.isEmpty &&
        _base64Url43.hasMatch(agentSignatureChecksum.trim()) &&
        _isDer(serverRootCa) &&
        _isDer(deviceRootCa);
  }

  static bool _isDer(String value) {
    final text = value.trim();
    return text.length >= 100 &&
        text.length <= 4096 &&
        text.length % 4 == 0 &&
        _base64Der.hasMatch(text);
  }

  bool get isComplete =>
      isCompleteFor(android: defaultTargetPlatform == TargetPlatform.android);

  bool isCompleteFor({required bool android}) =>
      _isTenant(entraTenantId) &&
      _guid.hasMatch(entraClientId.trim()) &&
      _isApiScope(apiScope) &&
      _isSecureApiBaseUrl(apiBaseUrl) &&
      (!android || _signatureHash.hasMatch(androidSignatureHash.trim()));

  static bool _isTenant(String value) {
    final tenant = value.trim();
    return _guid.hasMatch(tenant) || _tenantDomain.hasMatch(tenant);
  }

  static bool _isApiScope(String value) {
    final scope = value.trim();
    final uri = Uri.tryParse(scope);
    return uri != null &&
        !scope.contains(RegExp(r'\s')) &&
        (uri.scheme == 'api' || uri.scheme == 'https') &&
        uri.host.isNotEmpty &&
        uri.pathSegments.length == 1 &&
        uri.pathSegments.single.isNotEmpty &&
        !uri.hasQuery &&
        !uri.hasFragment;
  }

  static bool _isSecureApiBaseUrl(String value) {
    final uri = Uri.tryParse(value.trim());
    if (uri == null ||
        !uri.hasAuthority ||
        uri.host.isEmpty ||
        uri.userInfo.isNotEmpty ||
        uri.hasQuery ||
        uri.hasFragment) {
      return false;
    }
    if (uri.scheme == 'https') return true;
    return uri.scheme == 'http' &&
        (uri.host == 'localhost' ||
            uri.host == '127.0.0.1' ||
            uri.host == '10.0.2.2');
  }
}
