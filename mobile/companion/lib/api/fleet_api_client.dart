import 'dart:convert';

import 'package:http/http.dart' as http;

class PendingEnrollment {
  const PendingEnrollment({
    required this.pendingEnrollmentId,
    required this.bootstrapToken,
    required this.expiresAt,
  });

  static final _uuid = RegExp(
    r'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$',
    caseSensitive: false,
  );
  static final _token = RegExp(r'^[A-Za-z0-9_-]{43}$');

  final String pendingEnrollmentId;
  final String bootstrapToken;
  final DateTime expiresAt;

  factory PendingEnrollment.fromJson(Object? json) {
    if (json is! Map<String, Object?> ||
        json['schemaVersion'] != 1 ||
        json['pendingEnrollmentId'] is! String ||
        json['bootstrapToken'] is! String ||
        json['expiresAt'] is! String) {
      throw const FormatException('Unexpected pending enrollment response');
    }
    final id = json['pendingEnrollmentId']! as String;
    final token = json['bootstrapToken']! as String;
    final expiresAt = DateTime.parse(json['expiresAt']! as String).toUtc();
    if (!_uuid.hasMatch(id) || !_token.hasMatch(token)) {
      throw const FormatException('Unexpected pending enrollment response');
    }
    return PendingEnrollment(
      pendingEnrollmentId: id,
      bootstrapToken: token,
      expiresAt: expiresAt,
    );
  }

  @override
  String toString() => 'PendingEnrollment($pendingEnrollmentId, [redacted])';
}

class FleetApiException implements Exception {
  const FleetApiException(this.message, {this.statusCode});

  final String message;
  final int? statusCode;

  @override
  String toString() => 'FleetApiException($statusCode): $message';
}

class FleetApiClient {
  FleetApiClient({
    required this.baseUrl,
    required this.accessToken,
    http.Client? httpClient,
  }) : _http = httpClient ?? http.Client();

  final Uri baseUrl;
  final Future<String> Function() accessToken;
  final http.Client _http;

  Future<PendingEnrollment> createPendingEnrollment(Duration lifetime) async {
    final seconds = lifetime.inSeconds;
    if (seconds < 60 || seconds > 900) {
      throw ArgumentError.value(lifetime, 'lifetime', 'Must be 60-900 seconds');
    }
    final token = await accessToken();
    final response = await _http
        .post(
          baseUrl.replace(path: '/api/v1/admin/pending-enrollments'),
          headers: {
            'Authorization': 'Bearer $token',
            'Content-Type': 'application/json; charset=utf-8',
            'Accept': 'application/json',
          },
          body: jsonEncode({'schemaVersion': 1, 'lifetimeSeconds': seconds}),
        )
        .timeout(const Duration(seconds: 20));
    if (response.statusCode == 201) {
      return PendingEnrollment.fromJson(jsonDecode(response.body));
    }
    throw FleetApiException(switch (response.statusCode) {
      401 => 'The API rejected your sign-in. Sign out and sign in again.',
      403 => 'Your account is not assigned the Enrollments.Create role for the fleet API.',
      429 => 'Too many enrollment requests. Wait a moment and try again.',
      503 => 'Enrollment is temporarily unavailable. Try again shortly.',
      _ => 'Enrollment request failed (HTTP ${response.statusCode}).',
    }, statusCode: response.statusCode);
  }
}
