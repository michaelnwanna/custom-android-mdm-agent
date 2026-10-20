import 'package:flutter/material.dart';

import 'api/fleet_api_client.dart';
import 'app_config.dart';
import 'auth/auth_service.dart';
import 'enrollment/enrollment_page.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  const config = AppConfig.fromEnvironment();
  final authService = config.isComplete
      ? EntraCompanionAuthService(config)
      : null;
  runApp(MdmCompanionApp(config: config, authService: authService));
}

class MdmCompanionApp extends StatelessWidget {
  const MdmCompanionApp({
    required this.config,
    required this.authService,
    super.key,
  });

  final AppConfig config;
  final CompanionAuthService? authService;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'MDM Companion',
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF2457C5),
          brightness: Brightness.light,
        ),
        useMaterial3: true,
      ),
      home: CompanionHomePage(config: config, authService: authService),
    );
  }
}

class CompanionHomePage extends StatefulWidget {
  const CompanionHomePage({
    required this.config,
    required this.authService,
    super.key,
  });

  final AppConfig config;
  final CompanionAuthService? authService;

  @override
  State<CompanionHomePage> createState() => _CompanionHomePageState();
}

class _CompanionHomePageState extends State<CompanionHomePage> {
  UserSession? _user;
  String? _error;
  bool _isLoading = true;

  @override
  void initState() {
    super.initState();
    if (widget.config.isComplete && widget.authService != null) {
      _restoreSession();
    } else {
      _isLoading = false;
    }
  }

  Future<void> _restoreSession() async {
    try {
      final user = await widget.authService!.restoreSession();
      if (!mounted) return;
      setState(() {
        _user = user;
        _error = null;
        _isLoading = false;
      });
    } catch (error, stackTrace) {
      debugPrint('Failed to restore Microsoft session: $error\n$stackTrace');
      if (!mounted) return;
      setState(() {
        _error = 'Could not restore your session. Sign in again to continue.';
        _isLoading = false;
      });
    }
  }

  Future<void> _signIn() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      final user = await widget.authService!.signIn();
      if (!mounted) return;
      setState(() {
        _user = user;
        _isLoading = false;
      });
    } catch (error, stackTrace) {
      debugPrint('Microsoft sign-in failed: $error\n$stackTrace');
      if (!mounted) return;
      setState(() {
        _error =
            'Sign-in failed. Check your connection and Entra ID configuration.';
        _isLoading = false;
      });
    }
  }

  Future<void> _signOut() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      await widget.authService!.signOut();
      if (!mounted) return;
      setState(() {
        _user = null;
        _isLoading = false;
      });
    } catch (error, stackTrace) {
      debugPrint('Microsoft sign-out failed: $error\n$stackTrace');
      if (!mounted) return;
      setState(() {
        _error = 'Sign-out failed. Please try again.';
        _isLoading = false;
      });
    }
  }

  void _openEnrollment() {
    final api = FleetApiClient(
      baseUrl: Uri.parse(widget.config.apiBaseUrl.trim()),
      accessToken: widget.authService!.accessToken,
    );
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => EnrollmentPage(
          config: widget.config,
          createPendingEnrollment: api.createPendingEnrollment,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.config.isComplete || widget.authService == null) {
      return const _ConfigurationRequiredPage();
    }
    if (_isLoading) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }
    if (_user case final user?) {
      return _SignedInPage(
        user: user,
        onSignOut: _signOut,
        onEnroll: _openEnrollment,
        error: _error,
      );
    }
    return _SignInPage(onSignIn: _signIn, error: _error);
  }
}

class _ConfigurationRequiredPage extends StatelessWidget {
  const _ConfigurationRequiredPage();

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      body: SafeArea(
        child: Center(
          child: Padding(
            padding: EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.settings_outlined, size: 48),
                SizedBox(height: 16),
                Text(
                  'Configuration required',
                  style: TextStyle(fontSize: 22, fontWeight: FontWeight.bold),
                ),
                SizedBox(height: 8),
                Text(
                  'Set the Entra tenant ID, client ID, API scope, and HTTPS API URL '
                  'using the run instructions in mobile/companion/README.md.',
                  textAlign: TextAlign.center,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _SignInPage extends StatelessWidget {
  const _SignInPage({required this.onSignIn, required this.error});

  final VoidCallback onSignIn;
  final String? error;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(Icons.phonelink_lock, size: 64),
                  const SizedBox(height: 24),
                  Text(
                    'MDM Companion',
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.headlineMedium,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Sign in to securely manage your device fleet.',
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.bodyLarge,
                  ),
                  if (error != null) ...[
                    const SizedBox(height: 24),
                    _ErrorMessage(message: error!),
                  ],
                  const SizedBox(height: 32),
                  FilledButton.icon(
                    onPressed: onSignIn,
                    icon: const Icon(Icons.login),
                    label: const Text('Continue with Microsoft'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _SignedInPage extends StatelessWidget {
  const _SignedInPage({
    required this.user,
    required this.onSignOut,
    required this.onEnroll,
    required this.error,
  });

  final UserSession user;
  final VoidCallback onSignOut;
  final VoidCallback onEnroll;
  final String? error;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('MDM Companion'),
        actions: [
          IconButton(
            tooltip: 'Sign out',
            onPressed: onSignOut,
            icon: const Icon(Icons.logout),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          Text(
            'Welcome, ${user.displayName}',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          Text(user.email ?? 'Signed in with Microsoft Entra ID'),
          if (error != null) ...[
            const SizedBox(height: 16),
            _ErrorMessage(message: error!),
          ],
          const SizedBox(height: 24),
          Card(
            child: ListTile(
              leading: const Icon(Icons.qr_code_2),
              title: const Text('Enroll a device'),
              subtitle: const Text(
                'Show a single-use Android Enterprise QR code.',
              ),
              trailing: const Icon(Icons.chevron_right),
              onTap: onEnroll,
            ),
          ),
          const Card(
            child: ListTile(
              leading: Icon(Icons.notifications_outlined),
              title: Text('Fleet alerts'),
              subtitle: Text(
                'Push notifications will be added in a later increment.',
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ErrorMessage extends StatelessWidget {
  const _ErrorMessage({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      liveRegion: true,
      child: Text(
        message,
        style: TextStyle(color: Theme.of(context).colorScheme.error),
        textAlign: TextAlign.center,
      ),
    );
  }
}
