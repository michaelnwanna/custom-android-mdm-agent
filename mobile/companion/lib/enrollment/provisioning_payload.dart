import 'dart:convert';

import '../api/fleet_api_client.dart';
import '../app_config.dart';

/// Builds the Android Enterprise QR provisioning JSON for the fleet agent.
///
/// The admin extras must match `ProvisioningExtras` in the Android agent.
String buildProvisioningPayload({
  required AppConfig config,
  required PendingEnrollment enrollment,
  String? wifiSsid,
  String? wifiPassword,
}) {
  final bootstrap = jsonEncode({
    'schemaVersion': 1,
    'endpoint': config.enrollmentEndpoint.toString(),
    'pendingEnrollmentId': enrollment.pendingEnrollmentId,
    'bootstrapToken': enrollment.bootstrapToken,
  });
  final ssid = wifiSsid?.trim() ?? '';
  final password = wifiPassword ?? '';
  return jsonEncode({
    'android.app.extra.PROVISIONING_DEVICE_ADMIN_COMPONENT_NAME':
        'dev.fleet.agent/dev.fleet.agent.FleetAdminReceiver',
    'android.app.extra.PROVISIONING_DEVICE_ADMIN_PACKAGE_DOWNLOAD_LOCATION':
        config.agentApkUrl.trim(),
    'android.app.extra.PROVISIONING_DEVICE_ADMIN_SIGNATURE_CHECKSUM': config
        .agentSignatureChecksum
        .trim(),
    'android.app.extra.PROVISIONING_LEAVE_ALL_SYSTEM_APPS_ENABLED': true,
    if (ssid.isNotEmpty) ...{
      'android.app.extra.PROVISIONING_WIFI_SSID': ssid,
      'android.app.extra.PROVISIONING_WIFI_SECURITY_TYPE': password.isEmpty
          ? 'NONE'
          : 'WPA',
      if (password.isNotEmpty)
        'android.app.extra.PROVISIONING_WIFI_PASSWORD': password,
    },
    'android.app.extra.PROVISIONING_ADMIN_EXTRAS_BUNDLE': {
      'dev.fleet.agent.ENROLLMENT_BOOTSTRAP': bootstrap,
      'dev.fleet.agent.SERVER_ROOT_CA': config.serverRootCa.trim(),
      'dev.fleet.agent.DEVICE_ROOT_CA': config.deviceRootCa.trim(),
    },
  });
}
