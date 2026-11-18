# TestFlight from GitHub Actions

The `ios-testflight` job in `.github/workflows/mobile.yml` builds a signed Release IPA of the app on a GitHub macOS runner
and uploads it to TestFlight. It runs on:
- pushes to `master` that touch the app;
- manual runs on `master`.

Until every secret and variable below exists, the job finishes green, uploads nothing, and lists the missing names in a
notice. Pull requests and forks never try to sign.

**Bundle id:** `xyz.rsquaredsolutions.customgolfstats`. It is `ApplicationId` in
`src/CustomGolfStats.App/CustomGolfStats.App.csproj`, which is the source of truth; iOS and Android share it.

## Where things stand

Done:
- Apple Developer Program enrollment.
- An App Store Connect API key, stored as the repository secret `APPLE_API_KEY`.
- The bundle id, also stored as the repository secret `APPLE_BUNDLE_ID`. This secret is optional (see "What the job uses").

Left to do, in order:

| Step | Where | Time |
| --- | --- | --- |
| 1. Set the `ASC_KEY_ID`, `ASC_ISSUER_ID` and `API_BASE_URL` variables | App Store Connect + GitHub | 5 min |
| 2. Check the API key's role | App Store Connect | 1 min |
| 3. Register the bundle id and create the App Store Connect app record, if not done yet | developer.apple.com + App Store Connect | 10 min |
| 4. Create the Apple Distribution certificate and set its two secrets | Xcode + Keychain Access on your Mac | 10 min |
| 5. Run the workflow | GitHub Actions | 25–40 min |
| 6. Add internal testers | App Store Connect, TestFlight | 5 min |

The commands below use the GitHub CLI (`gh`), run from the repository folder after `gh auth login`.

## 1. Set the variables

The Key ID and Issuer ID aren't secret; they only work together with the `.p8` key. They go in repository **variables**:
**Settings > Secrets and variables > Actions > Variables**.

Find them in App Store Connect under **Users and Access > Integrations > App Store Connect API**
(<https://appstoreconnect.apple.com/access/integrations/api>), on the **Team Keys** tab:
- **Issuer ID:** the UUID above the key list. It is the same for every key in the team.
- **Key ID:** the 10-character id in your key's row. It also appears in the downloaded file name, `AuthKey_<KEYID>.p8`.

```sh
gh variable set ASC_KEY_ID --body "ABCDE12345"
gh variable set ASC_ISSUER_ID --body "00000000-0000-0000-0000-000000000000"
gh variable set API_BASE_URL --body "https://api.example.com/"   # the production API, with the trailing slash
```

The unsigned `ios` and `android` jobs read `API_BASE_URL` too, and fall back to a placeholder when it isn't set.

## 2. Check the API key's role

On the same page, the key's **Access** column must say **App Manager** or **Admin**:
- **App Manager** is enough to upload builds and, as far as Apple documents it, to manage provisioning profiles.
- **Admin** always works.

You can't change a key's role. If it is anything else (Developer, for example):
1. Generate a new key with App Manager access.
2. Replace the `APPLE_API_KEY` secret with it.
3. Update `ASC_KEY_ID`.
4. Revoke the old key.

`APPLE_API_KEY` can hold the `.p8` file's text exactly as downloaded (multi-line) or its base64. The job accepts both. To
replace it:

```sh
gh secret set APPLE_API_KEY < ~/Downloads/AuthKey_ABCDE12345.p8
```

## 3. Register the bundle id and create the app record (skip what's done)

**App ID.**
1. Open <https://developer.apple.com/account/resources/identifiers/list> and click **+** > **App IDs** > **App** > Continue.
2. Set **Description** to `Custom Golf Stats` and choose **Bundle ID: Explicit** `xyz.rsquaredsolutions.customgolfstats`.
3. Leave every capability off. The Keychain access group the app uses needs no capability. Continue > Register.

When Sign in with Apple arrives, turn that capability on here. Apple then invalidates the existing profile, and the next run
creates a new one.

**App Store Connect record.**
1. Open <https://appstoreconnect.apple.com/apps> > **+** > **New App**.
2. Fill in:
   - **Platforms:** iOS.
   - **Name:** `Custom Golf Stats`. It must be unique on the store; you can change it later.
   - **Primary language:** English (U.S.).
   - **Bundle ID:** `xyz.rsquaredsolutions.customgolfstats`.
   - **SKU:** `customgolfstats-ios`. It is private and permanent.
   - **User access:** Full Access.
3. Click Create.

**Agreements.** The Account Holder must accept the latest agreements under **Business** in App Store Connect, and again
whenever Apple updates them. Until then, uploads and API calls fail (see Troubleshooting).

## 4. Create the Apple Distribution certificate and set its secrets

The easiest way is on your Mac with Xcode:

1. **Xcode > Settings > Accounts.** Add your Apple Account if it isn't listed, select your team, and click **Manage
   Certificates…**.
2. Click **+** > **Apple Distribution**. Xcode creates the certificate and keeps its private key in your login keychain.
3. Open **Keychain Access**. Choose the **login** keychain, then **My Certificates**.
4. Find **Apple Distribution: <your name> (<TEAMID>)**. Expand it to check that a private key sits under it.
5. Right-click the certificate > **Export "Apple Distribution: …"**. Choose the format **Personal Information Exchange
   (.p12)**, save it as `dist.p12`, and set a strong password. macOS may ask for your login password to allow the export.
6. Store the `.p12` and its password in your password manager. Then set the secrets:

```sh
base64 -i ~/Desktop/dist.p12 | gh secret set IOS_DIST_CERT_P12
gh secret set IOS_DIST_CERT_PASSWORD        # prompts for the .p12 password; nothing lands in shell history
rm ~/Desktop/dist.p12                       # once it's safely in the password manager
```

The certificate is valid for one year, and a team can hold only a few Apple Distribution certificates. When it expires:
1. Repeat this step.
2. Replace the two secrets.
3. Revoke the old certificate at <https://developer.apple.com/account/resources/certificates/list>.

The next run creates a matching profile.

No Xcode? See "Certificate with openssl" at the end.

## 5. Run the workflow

Either push a change under `src/CustomGolfStats.App/**` (or the other paths in the workflow's `on:` list) to `master`, or
open **Actions > Mobile release builds > Run workflow** and choose `master`.

Open the **ios-testflight** job:
1. The first step either says "TestFlight upload skipped … Missing: …", or carries on.
2. "Fetch or create the App Store provisioning profile" says either `Reusing App Store profile …` or
   `Created App Store profile …`.
3. "Upload to TestFlight" runs altool. If it succeeds, the build has been handed over to Apple. It hasn't been accepted yet:
   App Store Connect then processes it for 5–30 minutes, and emails you if it rejects the build. When it's ready, it
   appears under the app's **TestFlight** tab.
4. The IPA is attached to the run as `ios-testflight-<run>.<attempt>` for 7 days. If the upload step failed, you can upload
   this file by hand with Apple's Transporter app. Anyone with read access to the repository can download it. It holds
   the signed app and the API address, but no secrets.

**Build numbers.** The build number (`CFBundleVersion`) is `<run number>.<attempt>`, for example `57.1`, and a re-run
becomes `57.2`, so **Re-run jobs** works. The run number starts again at 1 if the workflow file is renamed or recreated. If
that happens, bump `ApplicationDisplayVersion` (the visible version, 1.0) in the csproj first; otherwise Apple rejects the
lower build numbers.

## 6. Add internal testers

Internal testers are App Store Connect users on your team, up to 100 people. Builds reach them without Beta App Review.

1. Each tester needs an App Store Connect user. Go to **Users and Access > +**, enter their name and email, and give them a
   role such as **Customer Support** or **Marketing**; any role can test. They accept the email invitation. You, as the
   Account Holder, are already a user.
2. Open the app > **TestFlight > Internal Testing > +** to create a group, for example `Team`. Turn on **Enable automatic
   distribution**.
3. Add the testers to the group. If the group existed before the build, add the build to the group.
4. Testers install **TestFlight** from the App Store and accept the invitation. Each build lasts 90 days.

**External testers** (anyone, by email or a public link) need more first:
- the **Test Information** page filled in: a feedback email, a description, and a sign-in account for the reviewer;
- a privacy policy URL;
- a Beta App Review of each version's first build, which usually takes about a day.

## What the job uses

| Name | Kind | Value |
| --- | --- | --- |
| `APPLE_API_KEY` | Repository secret | The `AuthKey_<KEYID>.p8` text, or its base64 |
| `IOS_DIST_CERT_P12` | Repository secret | The base64 of the Apple Distribution `.p12` |
| `IOS_DIST_CERT_PASSWORD` | Repository secret | The `.p12` password |
| `APPLE_BUNDLE_ID` | Repository secret, **optional** | If set, the job checks that it equals the csproj `ApplicationId` and stops if it doesn't. |
| `ASC_KEY_ID` | Repository variable | The API key's Key ID |
| `ASC_ISSUER_ID` | Repository variable | The team's Issuer ID |
| `API_BASE_URL` | Repository variable | The production API address, with a trailing slash |

Because `APPLE_BUNDLE_ID` is a secret, GitHub shows the bundle id as `***` wherever it appears in the logs. This is
harmless. If you'd rather read it in the logs, delete the secret.

How it signs:
- **The API key.** The job writes it to `~/.appstoreconnect/private_keys/AuthKey_<id>.p8` and uses it for two things:
  - `.github/scripts/asc_profile.rb` (Ruby standard library only) finds or creates the App Store provisioning profile;
  - `xcrun altool --upload-app --apiKey … --apiIssuer …` uploads the IPA.
- **The certificate.** The `.p12` goes into a temporary keychain under `$RUNNER_TEMP`. The keychain is deleted at the end
  of the run, even after a failure.
- **The profile.** No profile is stored anywhere. Each run looks for an active App Store profile for the bundle id that
  contains the certificate, and creates one if there is none.
  - The certificate is matched by content: the SHA-1 of the certificate Apple holds against the one in the `.p12`.
  - On a mismatch, the log lists the team's distribution certificates (names, serials and expiry dates, none secret).
- **Entitlements.** `Platforms/iOS/Entitlements.plist` (the Keychain access group `$(AppIdentifierPrefix)$(CFBundleIdentifier)`)
  is applied to every iOS build through `CodesignEntitlements`, which Uno.Sdk sets.
- **Export compliance.** `ITSAppUsesNonExemptEncryption` is `false` in `Platforms/iOS/Info.plist`, so builds don't wait on
  the encryption questions.

### Why this approach and not fastlane match

- **(a) Certificate as a secret (used here).** The `.p12` and its password are repository secrets, and the API key fetches
  or creates the profile.
  - Pros: nothing to install, no extra repository, four required secrets and variables beyond the API key.
  - Con: you redo step 4 yearly.
- **(b) fastlane match.** match keeps the certificate and profiles encrypted in a private git repository (or S3 / Google
  Cloud Storage), and `fastlane match appstore --readonly` installs them on the runner.
  - Pro: it suits teams and several machines.
  - Cons: it needs a Gemfile and a Fastfile, a second private repository with a deploy key, and `MATCH_PASSWORD`.

For one developer and one app, (a) has fewer moving parts. To switch to (b) later, replace the certificate and profile steps
with `bundle exec fastlane match appstore --readonly`, and pass the profile it installs as `-p:CodesignProvision`.

## App Privacy labels

Under the app's **App Privacy** page in App Store Connect, declare the same data as
`src/CustomGolfStats.App/Platforms/iOS/PrivacyInfo.xcprivacy`. Every item is **linked to the user**, **not used for
tracking**, and has the purpose **App Functionality**.

| App Store Connect category | Data type | What it is |
| --- | --- | --- |
| Contact Info | Email Address | Sign-in email |
| Contact Info | Name | First and last name on the profile |
| Identifiers | User ID | Account id; GHIN number |
| User Content | Other User Content | Rounds, holes, shots, handicap entries, home course, added courses |
| Location | Coarse Location | One approximate reading for weather |
| Health & Fitness | Fitness | Steps and walking distance for a round |
| Other Data | Other Data Types | Gender, used only to choose tee ratings |

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| Notice `TestFlight upload skipped … Missing: …` | Set the named secrets or variables (step 1, step 4). |
| `Bundle id mismatch` | The `APPLE_BUNDLE_ID` secret differs from the csproj `ApplicationId`. Fix the secret, or delete it. |
| `APPLE_API_KEY invalid` | The secret is neither the `.p8` text nor its base64. Set it again from the file (step 2). |
| `security: SecKeychainItemImport: MAC verification failed` | Wrong `IOS_DIST_CERT_PASSWORD`, or an OpenSSL 3 `.p12` made without `-legacy`. |
| `No distribution identity` | The `.p12` lacks the private key, or holds a Development certificate. Export again, with the key, from My Certificates (step 4). |
| `no iOS App ID for …` | Step 3 (App ID) isn't done. |
| `none matches IOS_DIST_CERT_P12` | The `.p12` certificate was revoked or belongs to another team. Compare it with the list printed above the error, then redo step 4. |
| HTTP 401 from the profile step | `ASC_KEY_ID`, `ASC_ISSUER_ID` and `APPLE_API_KEY` don't belong together, or the key was revoked. |
| HTTP 403 from the profile step, or altool errors that mention agreements | The Account Holder must accept the latest agreements in App Store Connect (Business). Otherwise the key's role is too low (step 2). As a stopgap, create one App Store profile by hand under Profiles > **+**; the job reuses it. |
| altool: `The bundle version must be higher than the previously uploaded version` | The run number restarted (renamed workflow). Bump `ApplicationDisplayVersion`. |
| altool: `No suitable application records were found` | The App Store Connect record (step 3) is missing or has another bundle id. |
| Xcode / workload mismatch errors | The `setup-xcode` version and `--version` on `dotnet workload install` move together. See the comments in the `ios` job. |

## Certificate with openssl (no Xcode)

This works on any machine.

```sh
openssl genrsa -out dist.key 2048
openssl req -new -key dist.key -out dist.csr -subj "/emailAddress=you@example.com/CN=Your Name/C=US"
```

Upload `dist.csr` at Certificates > **+** > **Apple Distribution**, and download `distribution.cer`. Then:

```sh
openssl x509 -inform DER -in distribution.cer -out dist.pem
curl -fsSLo wwdr.cer https://www.apple.com/certificateauthority/AppleWWDRCAG3.cer
openssl x509 -inform DER -in wwdr.cer -out wwdr.pem
# OpenSSL 3.x needs -legacy (macOS `security import` rejects its default .p12 encryption). LibreSSL (macOS
# /usr/bin/openssl) doesn't know -legacy: leave it out there.
openssl pkcs12 -export -legacy -inkey dist.key -in dist.pem -certfile wwdr.pem -name "Apple Distribution" -out dist.p12
```

Then set the secrets as in step 4, and delete the loose key files.
