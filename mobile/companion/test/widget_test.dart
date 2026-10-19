import 'package:flutter_test/flutter_test.dart';
import 'package:mdm_companion/app_config.dart';
import 'package:mdm_companion/auth/auth_service.dart';
import 'package:mdm_companion/main.dart';

const _tenantId = '11111111-1111-1111-1111-111111111111';
const _clientId = '22222222-2222-2222-2222-222222222222';
const _scope = 'api://33333333-3333-3333-3333-333333333333/Enrollments.Create';
const _hash = 'ga0RGNYHvNM5d0SLGQfpQWAPGJ8=';

AppConfig _config({
  String tenant = _tenantId,
  String client = _clientId,
  String scope = _scope,
  String apiBaseUrl = 'https://api.example.com',
  String hash = _hash,
}) => AppConfig(
  entraTenantId: tenant,
  entraClientId: client,
  apiScope: scope,
  apiBaseUrl: apiBaseUrl,
  androidSignatureHash: hash,
);

void main() {
  final config = _config();

  testWidgets('shows setup guidance when Entra configuration is missing', (
    tester,
  ) async {
    await tester.pumpWidget(
      MdmCompanionApp(
        config: _config(tenant: '', client: '', scope: '', apiBaseUrl: ''),
        authService: null,
      ),
    );

    expect(find.text('Configuration required'), findsOneWidget);
    expect(find.text('Continue with Microsoft'), findsNothing);
  });

  testWidgets('signs in and signs out through the authentication service', (
    tester,
  ) async {
    final authService = FakeCompanionAuthService();
    await tester.pumpWidget(
      MdmCompanionApp(config: config, authService: authService),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Continue with Microsoft'));
    await tester.pumpAndSettle();

    expect(authService.signInCalls, 1);
    expect(find.text('Welcome, Fleet Admin'), findsOneWidget);
    expect(find.text('admin@example.com'), findsOneWidget);

    await tester.tap(find.byTooltip('Sign out'));
    await tester.pumpAndSettle();

    expect(authService.signOutCalls, 1);
    expect(find.text('Continue with Microsoft'), findsOneWidget);
  });

  testWidgets('stays on the sign-in page when the user cancels', (
    tester,
  ) async {
    final authService = FakeCompanionAuthService()..cancelSignIn = true;
    await tester.pumpWidget(
      MdmCompanionApp(config: config, authService: authService),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Continue with Microsoft'));
    await tester.pumpAndSettle();

    expect(find.text('Continue with Microsoft'), findsOneWidget);
    expect(find.textContaining('Sign-in failed'), findsNothing);
  });

  testWidgets('restores an existing authenticated session', (tester) async {
    final authService = FakeCompanionAuthService()
      ..restoredSession = const UserSession(
        displayName: 'Returning Admin',
        email: 'returning@example.com',
      );
    await tester.pumpWidget(
      MdmCompanionApp(config: config, authService: authService),
    );
    await tester.pumpAndSettle();

    expect(find.text('Welcome, Returning Admin'), findsOneWidget);
    expect(find.text('Continue with Microsoft'), findsNothing);
  });

  testWidgets('shows a visible message when sign-in fails', (tester) async {
    final authService = FakeCompanionAuthService()
      ..signInError = StateError('Entra unavailable');
    await tester.pumpWidget(
      MdmCompanionApp(config: config, authService: authService),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Continue with Microsoft'));
    await tester.pumpAndSettle();

    expect(
      find.text(
        'Sign-in failed. Check your connection and Entra ID configuration.',
      ),
      findsOneWidget,
    );
  });

  test('accepts a complete single-tenant Entra configuration', () {
    expect(config.isCompleteFor(android: true), isTrue);
    expect(
      _config(tenant: 'contoso.onmicrosoft.com').isCompleteFor(android: true),
      isTrue,
    );
    expect(
      _config(apiBaseUrl: 'http://10.0.2.2:5000').isCompleteFor(android: true),
      isTrue,
    );
    expect(_config(hash: '').isCompleteFor(android: false), isTrue);
    expect(config.authority, 'https://login.microsoftonline.com/$_tenantId');
    expect(
      config.androidRedirectUri,
      'msauth://com.michaelnwanna.mdm_companion/ga0RGNYHvNM5d0SLGQfpQWAPGJ8%3D',
    );
  });

  test('rejects incomplete or unsafe Entra configuration', () {
    for (final invalid in [
      _config(tenant: 'common'),
      _config(tenant: 'organizations'),
      _config(client: 'not-a-guid'),
      _config(scope: 'Enrollments.Create'),
      _config(scope: 'api://app-id/Enrollments.Create offline_access'),
      _config(apiBaseUrl: 'http://api.example.com'),
      _config(apiBaseUrl: 'https://'),
      _config(hash: ''),
    ]) {
      expect(invalid.isCompleteFor(android: true), isFalse);
    }
  });
}

class FakeCompanionAuthService implements CompanionAuthService {
  int signInCalls = 0;
  int signOutCalls = 0;
  bool cancelSignIn = false;
  Object? signInError;
  UserSession? restoredSession;

  @override
  Future<UserSession?> restoreSession() async => restoredSession;

  @override
  Future<UserSession?> signIn() async {
    signInCalls++;
    if (signInError case final error?) throw error;
    if (cancelSignIn) return null;
    return const UserSession(
      displayName: 'Fleet Admin',
      email: 'admin@example.com',
    );
  }

  @override
  Future<void> signOut() async {
    signOutCalls++;
  }

  @override
  Future<String> accessToken() async => 'test-access-token';
}
