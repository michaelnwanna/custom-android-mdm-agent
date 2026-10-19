# MDM Companion

Flutter companion app for fleet administrators (roadmap Phase 2.13). It signs administrators in with **Microsoft Entra ID** through native MSAL ([`msal_auth`](https://pub.dev/packages/msal_auth)), which provides a token cache, silent refresh, and support for Microsoft Authenticator/Company Portal brokers and Conditional Access.

Current scope: sign-in, session restore, sign-out, and [QR device enrollment](#qr-device-enrollment).

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

## QR device enrollment

**Enroll a device** creates a single-use pending enrollment (`POST /api/v1/admin/pending-enrollments`, 5/10/15 min) and shows an Android Enterprise provisioning QR code with a countdown. The code is hidden when it expires. On Android the app sets `FLAG_SECURE`, which blocks screenshots and recents previews. iOS cannot block screenshots, so treat the screen as sensitive.

On a **factory-reset** Android device (API 30+), tap the welcome screen six times, connect to Wi-Fi if it wasn't in the QR, and scan. Android downloads the agent, verifies its signing certificate, makes it Device Owner and opens its `ADMIN_POLICY_COMPLIANCE` screen. The agent stages the extras and enrolls automatically (see [Android enrollment](../../docs/android-enrollment.md#qr-provisioning-intake)).

QR payload: device-admin component `dev.fleet.agent/dev.fleet.agent.FleetAdminReceiver`, APK URL, signature checksum, optional Wi-Fi, and admin extras:

| Extra | Value |
|---|---|
| `dev.fleet.agent.ENROLLMENT_BOOTSTRAP` | Bootstrap JSON: `schemaVersion`, `endpoint` (`API_BASE_URL` + `/api/v1/enrollments`), `pendingEnrollmentId`, `bootstrapToken` |
| `dev.fleet.agent.SERVER_ROOT_CA` | Base64 DER CA the agent trusts for the HTTPS server |
| `dev.fleet.agent.DEVICE_ROOT_CA` | Base64 DER root for issued device certificates |

### QR configuration

The feature needs an HTTPS `API_BASE_URL` plus these extra environment variables for [`tool/flutter_entra.sh`](tool/flutter_entra.sh):

```bash
# Agent APK built without testOnly (QR provisioning cannot install testOnly apps):
#   cd android && ./gradlew :agent:assembleRelease -PagentTestOnly=false   (sign it), then host it over HTTPS
export AGENT_APK_URL=https://downloads.example.com/fleet-agent.apk

# Base64url SHA-256 of the agent signing certificate (debug keystore shown)
export AGENT_SIGNATURE_CHECKSUM=$(keytool -exportcert -alias androiddebugkey \
  -keystore ~/.android/debug.keystore -storepass android \
  | openssl dgst -binary -sha256 | openssl base64 | tr '+/' '-_' | tr -d '=')

# Trust roots as single-line base64 DER (use P-256/EC roots so the QR stays scannable)
export SERVER_ROOT_CA=$(openssl x509 -in server-ca.pem -outform DER | base64 | tr -d '\n')
export DEVICE_ROOT_CA=$(openssl x509 -in device-ca.pem -outform DER | base64 | tr -d '\n')
```

The QR carries a live bootstrap secret: show it only to the person enrolling, and let it expire if it is not used. The roots and endpoint come from the admin's trusted app configuration, not from the server response.

## Tests

```bash
flutter analyze
flutter test
```
