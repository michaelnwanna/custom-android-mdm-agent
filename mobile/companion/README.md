# MDM Companion

Flutter companion app for fleet administrators (roadmap Phase 2.13). It signs administrators in with **Microsoft Entra ID** through native MSAL ([`msal_auth`](https://pub.dev/packages/msal_auth)), which provides a token cache, silent refresh, and support for Microsoft Authenticator/Company Portal brokers and Conditional Access.

Current scope: sign-in, session restore, and sign-out. QR enrollment is the next step (see [QR enrollment plan](#qr-enrollment-plan)).

## 1. Entra tenant setup

Create **two** app registrations in your tenant (Entra admin center → App registrations).

### API app (represents the fleet backend)

1. New registration, name it something like `Fleet MDM API`, single tenant. Note its **Application (client) ID** (`<API_CLIENT_ID>`).
2. **Expose an API**: set the Application ID URI (default `api://<API_CLIENT_ID>`) and add the delegated scope `Enrollments.Create` (admins only consent).
3. **App roles**: create role `Enrollments.Create`, allowed member types *Users/Groups*.
4. **Manifest**: set `"requestedAccessTokenVersion": 2` (in the `api` section).
5. **Enterprise applications** → `Fleet MDM API` → Properties: set *Assignment required* to **Yes**. Under *Users and groups*, assign the `Enrollments.Create` role to the admins allowed to enroll devices.

### Mobile app (this companion, public client)

1. New registration, name it `Fleet MDM Companion`, single tenant. Note its **Application (client) ID** (`<MOBILE_CLIENT_ID>`).
2. **Authentication** → *Add a platform*:
   - **Android**: package name `com.michaelnwanna.mdm_companion`, signature hash from step 2 below.
   - **iOS / macOS**: bundle ID `com.michaelnwanna.mdmCompanion` (redirect `msauth.com.michaelnwanna.mdmCompanion://auth`).
3. **API permissions** → *Add* → *My APIs* → `Fleet MDM API` → delegated `Enrollments.Create`, then **Grant admin consent**.

## 2. Android signature hash

Entra binds the Android redirect URI to the APK signing certificate. For local debug builds:

```bash
keytool -exportcert -alias androiddebugkey -keystore ~/.android/debug.keystore \
  -storepass android | openssl sha1 -binary | openssl base64
```

Register this value in the Android platform. Release keys (and Play App Signing) need their own registered hash.

## 3. Backend configuration

Run the API with the Entra token profile (see [administrator enrollment](../../docs/administrator-enrollment.md)):

```bash
Administrator__Authority=https://login.microsoftonline.com/<TENANT_ID>/v2.0
Administrator__Audience=<API_CLIENT_ID>
Administrator__TokenProfile=Entra
```

The backend requires the delegated scope `scp=Enrollments.Create` **and** the assigned app role `roles=Enrollments.Create`.

## 4. Run the app

[`tool/flutter_entra.sh`](tool/flutter_entra.sh) passes the required `--dart-define` values. For Android commands it also computes the debug-keystore signature hash and injects it into the manifest:

```bash
export ENTRA_TENANT_ID=<TENANT_ID or contoso.onmicrosoft.com>
export ENTRA_CLIENT_ID=<MOBILE_CLIENT_ID>
export ENTRA_API_SCOPE=api://<API_CLIENT_ID>/Enrollments.Create
export API_BASE_URL=https://<your-api-host>   # http allowed only for localhost/127.0.0.1/10.0.2.2

tool/flutter_entra.sh run                      # connected device or emulator
tool/flutter_entra.sh build apk --debug
tool/flutter_entra.sh build ios --simulator --no-codesign
```

To use a different keystore, set `ANDROID_KEYSTORE`, `ANDROID_KEY_ALIAS`, `ANDROID_KEYSTORE_PASSWORD`, or set `ENTRA_ANDROID_SIGNATURE_HASH` directly.

The app shows **Configuration required** when values are missing or unsafe. Multi-tenant authorities (`common`, `organizations`, `consumers`) are rejected because the backend trusts a single tenant issuer.

Notes:
- iOS requires 16.0+ (MSAL). Device builds need a development team because the app uses the keychain group `com.microsoft.adalcache` ([Runner.entitlements](ios/Runner/Runner.entitlements)).
- If Microsoft Authenticator is installed, sign-in goes through it (broker), which enables SSO and device-based Conditional Access.

## QR enrollment plan

1. Admin signs in to the companion (Entra token with `Enrollments.Create`).
2. Companion calls `POST /api/v1/admin/pending-enrollments` (`lifetimeSeconds` 60–900) and receives a single-use `pendingEnrollmentId` and `bootstrapToken`.
3. Companion renders an **Android Enterprise provisioning QR** containing the agent's device-admin component, APK download URL and SHA-256 checksum, optional Wi-Fi, and `PROVISIONING_ADMIN_EXTRAS_BUNDLE` with the bootstrap JSON (`schemaVersion`, `endpoint`, `pendingEnrollmentId`, `bootstrapToken`).
4. On a factory-reset device, tap the welcome screen six times, scan the QR, and Android installs the agent as Device Owner.
5. The agent reads the admin extras, generates its key and CSR, and redeems the bootstrap token at `/api/v1/enrollments`.

Still to build: QR rendering with a countdown and screenshot protection (the QR carries a short-lived secret); agent intake of the provisioning extras (it currently reads a staged lab file); trust-root distribution to the device; and hosting the signed agent APK with its checksum.

## Tests

```bash
flutter analyze
flutter test
```
