import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:mdm_companion/api/fleet_api_client.dart';
import 'package:mdm_companion/app_config.dart';
import 'package:mdm_companion/enrollment/enrollment_page.dart';
import 'package:mdm_companion/enrollment/provisioning_payload.dart';
import 'package:qr_flutter/qr_flutter.dart';

const _pendingId = '44444444-4444-4444-4444-444444444444';
final _token = 'A' * 43;
final _serverRoot = 'S' * 600;
final _deviceRoot = 'D' * 600;

AppConfig _config({String apiBaseUrl = 'https://fleet.example.com'}) =>
    AppConfig(
      entraTenantId: '11111111-1111-1111-1111-111111111111',
      entraClientId: '22222222-2222-2222-2222-222222222222',
      apiScope: 'api://33333333-3333-3333-3333-333333333333/Enrollments.Create',
      apiBaseUrl: apiBaseUrl,
      androidSignatureHash: 'ga0RGNYHvNM5d0SLGQfpQWAPGJ8=',
      agentApkUrl: 'https://downloads.example.com/fleet-agent.apk',
      agentSignatureChecksum: 'B' * 43,
      serverRootCa: _serverRoot,
      deviceRootCa: _deviceRoot,
    );

Map<String, Object?> _response(DateTime expiresAt) => {
  'schemaVersion': 1,
  'pendingEnrollmentId': _pendingId,
  'bootstrapToken': _token,
  'issuedAt': DateTime.utc(2030).toIso8601String(),
  'expiresAt': expiresAt.toIso8601String(),
};

void main() {
  group('FleetApiClient', () {
    test('creates a pending enrollment with the bearer token', () async {
      late http.Request sent;
      final client = FleetApiClient(
        baseUrl: Uri.parse('https://fleet.example.com'),
        accessToken: () async => 'access-token',
        httpClient: MockClient((request) async {
          sent = request;
          return http.Response(
            jsonEncode(_response(DateTime.utc(2030, 1, 1, 0, 10))),
            201,
          );
        }),
      );

      final enrollment = await client.createPendingEnrollment(
        const Duration(minutes: 10),
      );

      expect(
        sent.url.toString(),
        'https://fleet.example.com/api/v1/admin/pending-enrollments',
      );
      expect(sent.headers['Authorization'], 'Bearer access-token');
      expect(sent.headers['Content-Type'], startsWith('application/json'));
      expect(jsonDecode(sent.body), {
        'schemaVersion': 1,
        'lifetimeSeconds': 600,
      });
      expect(enrollment.pendingEnrollmentId, _pendingId);
      expect(enrollment.toString(), isNot(contains(_token)));
    });

    test('maps authorization failures to readable errors', () async {
      final client = FleetApiClient(
        baseUrl: Uri.parse('https://fleet.example.com'),
        accessToken: () async => 'access-token',
        httpClient: MockClient((_) async => http.Response('', 403)),
      );

      await expectLater(
        client.createPendingEnrollment(const Duration(minutes: 5)),
        throwsA(
          isA<FleetApiException>()
              .having((e) => e.statusCode, 'statusCode', 403)
              .having((e) => e.message, 'message', contains('Enrollments')),
        ),
      );
      expect(
        () => client.createPendingEnrollment(const Duration(minutes: 30)),
        throwsArgumentError,
      );
    });

    test('rejects malformed responses', () {
      for (final body in [
        {..._response(DateTime.utc(2030)), 'schemaVersion': 2},
        {..._response(DateTime.utc(2030)), 'bootstrapToken': 'short'},
        {..._response(DateTime.utc(2030)), 'pendingEnrollmentId': 'nope'},
      ]) {
        expect(() => PendingEnrollment.fromJson(body), throwsFormatException);
      }
    });
  });

  test('builds an Android Enterprise payload matching the agent extras', () {
    final payload = jsonDecode(
      buildProvisioningPayload(
        config: _config(),
        enrollment: PendingEnrollment(
          pendingEnrollmentId: _pendingId,
          bootstrapToken: _token,
          expiresAt: DateTime.utc(2030),
        ),
        wifiSsid: 'Lab',
        wifiPassword: 'secret',
      ),
    ) as Map<String, Object?>;

    expect(
      payload['android.app.extra.PROVISIONING_DEVICE_ADMIN_COMPONENT_NAME'],
      'dev.fleet.agent/dev.fleet.agent.FleetAdminReceiver',
    );
    expect(payload['android.app.extra.PROVISIONING_WIFI_SECURITY_TYPE'], 'WPA');
    final extras =
        payload['android.app.extra.PROVISIONING_ADMIN_EXTRAS_BUNDLE']!
            as Map<String, Object?>;
    expect(extras.keys, {
      'dev.fleet.agent.ENROLLMENT_BOOTSTRAP',
      'dev.fleet.agent.SERVER_ROOT_CA',
      'dev.fleet.agent.DEVICE_ROOT_CA',
    });
    expect(
      jsonDecode(extras['dev.fleet.agent.ENROLLMENT_BOOTSTRAP']! as String),
      {
        'schemaVersion': 1,
        'endpoint': 'https://fleet.example.com/api/v1/enrollments',
        'pendingEnrollmentId': _pendingId,
        'bootstrapToken': _token,
      },
    );
  });

  test('QR enrollment requires HTTPS and complete agent configuration', () {
    expect(_config().isQrEnrollmentConfigured, isTrue);
    expect(
      _config(apiBaseUrl: 'http://10.0.2.2:5000').isQrEnrollmentConfigured,
      isFalse,
    );
  });

  testWidgets('shows the QR code and hides it when the code expires', (
    tester,
  ) async {
    var now = DateTime.utc(2030, 1, 1);
    Duration? requested;
    await tester.pumpWidget(
      MaterialApp(
        home: EnrollmentPage(
          config: _config(),
          now: () => now,
          createPendingEnrollment: (lifetime) async {
            requested = lifetime;
            return PendingEnrollment(
              pendingEnrollmentId: _pendingId,
              bootstrapToken: _token,
              expiresAt: now.add(lifetime),
            );
          },
        ),
      ),
    );

    await tester.tap(find.text('Generate enrollment code'));
    await tester.pump();

    expect(requested, const Duration(minutes: 10));
    expect(find.byType(QrImageView), findsOneWidget);
    expect(find.text('Expires in 10:00'), findsOneWidget);

    now = now.add(const Duration(minutes: 10));
    await tester.pump(const Duration(seconds: 1));

    expect(find.byType(QrImageView), findsNothing);
    expect(find.text('Generate enrollment code'), findsOneWidget);
  });

  testWidgets('explains missing QR configuration', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: EnrollmentPage(
          config: _config(apiBaseUrl: 'http://localhost:5000'),
          createPendingEnrollment: (_) => throw UnimplementedError(),
        ),
      ),
    );

    expect(find.textContaining('not configured'), findsOneWidget);
    expect(find.text('Generate enrollment code'), findsNothing);
  });
}
