import 'package:msal_auth/msal_auth.dart';

import '../app_config.dart';

class UserSession {
  const UserSession({required this.displayName, required this.email});

  final String displayName;
  final String? email;
}

abstract interface class CompanionAuthService {
  Future<UserSession?> restoreSession();

  /// Returns null when the user cancels the Microsoft sign-in prompt.
  Future<UserSession?> signIn();

  Future<void> signOut();

  /// Returns an API access token, prompting only when the cache cannot refresh.
  Future<String> accessToken();
}

class EntraCompanionAuthService implements CompanionAuthService {
  EntraCompanionAuthService(this._config)
    : _client = SingleAccountPca.create(
        clientId: _config.entraClientId.trim(),
        androidConfig: AndroidConfig(
          configFilePath: 'assets/msal_config.json',
          redirectUri: _config.androidRedirectUri,
        ),
        appleConfig: AppleConfig(
          authority: _config.authority,
          broker: Broker.msAuthenticator,
        ),
      );

  final AppConfig _config;
  final Future<SingleAccountPca> _client;

  List<String> get _scopes => [_config.apiScope.trim()];

  @override
  Future<UserSession?> restoreSession() async {
    final client = await _client;
    try {
      final result = await client.acquireTokenSilent(
        scopes: _scopes,
        authority: _config.authority,
      );
      return _toSession(result.account);
    } on MsalNoCurrentAccountException {
      return null;
    } on MsalUiRequiredException {
      return null;
    }
  }

  @override
  Future<UserSession?> signIn() async {
    final client = await _client;
    try {
      final result = await client.acquireToken(
        scopes: _scopes,
        prompt: Prompt.selectAccount,
        authority: _config.authority,
      );
      return _toSession(result.account);
    } on MsalUserCancelException {
      return null;
    }
  }

  @override
  Future<void> signOut() async {
    final client = await _client;
    await client.signOut();
  }

  @override
  Future<String> accessToken() async {
    final client = await _client;
    try {
      final result = await client.acquireTokenSilent(
        scopes: _scopes,
        authority: _config.authority,
      );
      return result.accessToken;
    } on MsalUiRequiredException {
      final result = await client.acquireToken(
        scopes: _scopes,
        authority: _config.authority,
      );
      return result.accessToken;
    }
  }

  UserSession _toSession(Account account) => UserSession(
    displayName: account.name ?? account.username ?? 'Administrator',
    email: account.username,
  );
}
