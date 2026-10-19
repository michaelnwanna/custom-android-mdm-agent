import 'package:flutter/foundation.dart';

class AppConfig {
  const AppConfig({
    required this.entraTenantId,
    required this.entraClientId,
    required this.apiScope,
    required this.apiBaseUrl,
    required this.androidSignatureHash,
  });

  const AppConfig.fromEnvironment()
    : entraTenantId = const String.fromEnvironment('ENTRA_TENANT_ID'),
      entraClientId = const String.fromEnvironment('ENTRA_CLIENT_ID'),
      apiScope = const String.fromEnvironment('ENTRA_API_SCOPE'),
      apiBaseUrl = const String.fromEnvironment('API_BASE_URL'),
      androidSignatureHash = const String.fromEnvironment(
        'ENTRA_ANDROID_SIGNATURE_HASH',
      );

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

  String get authority =>
      'https://login.microsoftonline.com/${entraTenantId.trim()}';

  String get androidRedirectUri =>
      'msauth://$androidPackageName/'
      '${Uri.encodeComponent(androidSignatureHash.trim())}';

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
