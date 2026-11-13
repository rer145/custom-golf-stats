# TestFlight from GitHub Actions

The `ios-testflight` job in `.github/workflows/mobile.yml` builds a signed Release IPA of the app on a GitHub macOS runner
and uploads it to TestFlight. It runs on pushes to `master` that touch the app, and on a manual run. It does nothing (and
stays green) until the secrets below exist, so pull requests and forks never try to sign.

This guide is the one-time setup. Do the steps in order; each one needs the one before.

| Step | Where | Time |
| --- | --- | --- |
| 1. Enroll in the Apple Developer Program | developer.apple.com | 1–2 days (individual) or 1–3 weeks (organization) |
| 2. Register the App ID | Certificates, Identifiers & Profiles | 2 min |
| 3. Create the App Store Connect app record | App Store Connect | 5 min |
| 4. Create the App Store Connect API key | App Store Connect, Users and Access | 2 min |
| 5. Create the distribution certificate (.p12) | Terminal + Certificates portal | 10 min |
| 6. Add the GitHub secrets and variable | GitHub repository settings | 5 min |
| 7. Run the workflow | GitHub Actions | 25–40 min |
| 8. Add internal testers | App Store Connect, TestFlight | 5 min |

Nothing here needs the Xcode GUI. Every step works from a browser, plus `openssl` in a terminal (macOS, Linux or WSL).

## How the job signs

- **App Store Connect API key** (`ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_KEY_P8`). The job uses it for two things:
  - It finds or creates the App Store provisioning profile (`.github/scripts/asc_profile.rb`, Ruby standard library only).
  - It uploads the IPA (`xcrun altool --upload-app --apiKey … --apiIssuer …`, with the key in `~/.appstoreconnect/private_keys`).
- **Distribution certificate** (`IOS_DIST_CERT_P12`, `IOS_DIST_CERT_PASSWORD`). The job imports it into a temporary keychain
  that it deletes at the end of the run.
- **Provisioning profile.** No profile is stored anywhere. Each run asks the API for an active App Store profile for
  `com.customgolfstats.app` that contains the certificate. If there is none, the run creates one. This covers the first
  run, a renewed certificate, and a profile Apple invalidated after a capability change.
- **Build number.** `CFBundleVersion` is the GitHub run number (`-p:ApplicationVersion=${{ github.run_number }}`), so every
  upload is unique and increasing. The marketing version (`CFBundleShortVersionString`, 1.0) is
  `ApplicationDisplayVersion` in `src/CustomGolfStats.App/CustomGolfStats.App.csproj`. Bump it there for a new release.
- **API address.** It comes from the `API_BASE_URL` variable. A Release build fails without one (CGS0001).
- **Export compliance.** `ITSAppUsesNonExemptEncryption` is `false` in `Platforms/iOS/Info.plist`, so App Store Connect
  doesn't hold each build for the encryption questions.

### Why this approach and not fastlane match

There are two common ways to sign on a CI runner:

- **(a) Certificate as a secret (used here).** The `.p12` and its password are GitHub secrets. The runner imports them
  into a throwaway keychain, and the API key fetches or creates the profile.
  - Pros: nothing to install, no extra repository, five secrets in total.
  - Con: when the certificate expires (yearly), you redo step 5 and replace two secrets.
- **(b) fastlane match.** match keeps the certificate and profiles encrypted in a private git repository (or S3 / Google
  Cloud Storage). `fastlane match appstore --readonly` installs them on the runner.
  - Pro: it suits teams and several machines that share one identity.
  - Cons: it needs a Gemfile and a Fastfile, a second private repository with its deploy key, and a `MATCH_PASSWORD`.

For one developer and one app, (a) has fewer moving parts. To switch to (b) later:
1. Run `fastlane match init` and `fastlane match appstore` once on a machine with the API key.
2. In the job, replace the certificate and profile steps with `bundle exec fastlane match appstore --readonly` (with
   `MATCH_PASSWORD` and the storage credentials as secrets).
3. Pass the profile match installed as `-p:CodesignProvision`.

## 1. Enroll in the Apple Developer Program

Go to <https://developer.apple.com/programs/enroll/>. You can also enroll in the Apple Developer app on an iPhone, which is
usually faster for individuals. Use an Apple Account with two-factor authentication on. The fee is US$99 a year.

**Individual or organization?**

| | Individual | Organization |
| --- | --- | --- |
| Seller name on the App Store | Your legal name | The company's legal name |
| Needs | Government ID | A legal entity, a D-U-N-S number (free, can take up to 2 weeks), a public website, and authority to sign for the company |
| Approval | Usually 1–2 days | Often 1–3 weeks |
| Team members (App Store Connect users, internal testers) | Yes | Yes |
| Developer portal access for other people | No; only you sign | Yes, by role |
| Later change | Apple Support can convert an individual account into an organization one; the apps move with it | — |

For a solo project, individual is the quick route, and it doesn't stop you adding internal testers. Choose organization if
the app should be sold under a company name, or if other people will need to manage certificates.

When enrollment finishes, note your **Team ID** (Membership details on developer.apple.com). You don't need it for any
secret, but it appears in the certificate name and in error messages.

## 2. Register the App ID (bundle id)

1. Open <https://developer.apple.com/account/resources/identifiers/list> (Certificates, Identifiers & Profiles > Identifiers).
2. Click **+** > **App IDs** > **App** > Continue.
3. Set **Description** to `Custom Golf Stats` and choose **Bundle ID: Explicit** `com.customgolfstats.app`.
4. Leave every capability off. The Keychain access group the app uses needs no capability. Continue > Register.

When Sign in with Apple arrives (see the roadmap), turn that capability on here. Apple then invalidates the existing
profile, and the next CI run creates a new one by itself.

## 3. Create the App Store Connect app record

1. Open <https://appstoreconnect.apple.com/apps> > **+** > **New App**.
2. Fill in:
   - **Platforms:** iOS.
   - **Name:** `Custom Golf Stats`. It must be unique on the store; if it's taken, add a word. You can change it later.
   - **Primary language:** English (U.S.).
   - **Bundle ID:** `com.customgolfstats.app`, which appears once step 2 is done.
   - **SKU:** `customgolfstats-ios`. It is private and can't be changed.
   - **User access:** Full Access.
3. Click Create. TestFlight builds attach to this record.

Fill in **App Privacy** (the privacy labels) on the app's page now or before the first external test. It must agree with
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

A **privacy policy URL** is required before external testing and the App Store. Internal TestFlight doesn't need one.

## 4. Create the App Store Connect API key

1. Open <https://appstoreconnect.apple.com/access/integrations/api> (Users and Access > Integrations > App Store Connect
   API). The Account Holder may first need to click **Request Access** and accept the terms.
2. Under **Team Keys**, click **+** (Generate API Key).
3. Set **Name** to `GitHub Actions` and **Access** to **App Manager**. Click Generate.
4. Copy the **Issuer ID**, the UUID above the key list. It is the same for every key in the team.
5. Copy the key's **Key ID**, 10 characters.
6. Click **Download** and keep `AuthKey_<KEYID>.p8` somewhere safe, such as your password manager. Apple lets you download it
   only once. If you lose it, revoke the key and create a new one.

App Manager is enough to upload builds and to manage profiles. If the profile step ever fails with HTTP 403, you have two
fixes:
- give the key **Admin** access; or
- create one App Store profile by hand: Profiles > **+** > App Store Connect > `com.customgolfstats.app` > the certificate
  from step 5. The job reuses an existing profile and doesn't need to create one.

## 5. Create the distribution certificate (.p12) without Xcode

Run these in an empty folder. Use `openssl version` to check your version: macOS's built-in `/usr/bin/openssl` is LibreSSL,
while Homebrew and Linux usually have OpenSSL 3.

```sh
# 1. A private key and a certificate signing request (use your Apple Account email and name).
openssl genrsa -out dist.key 2048
openssl req -new -key dist.key -out dist.csr -subj "/emailAddress=you@example.com/CN=Your Name/C=US"
```

2. Open <https://developer.apple.com/account/resources/certificates/list> > **+** > **Apple Distribution** > Continue.
3. Upload `dist.csr`, then Download. You get `distribution.cer`.

```sh
# 4. Turn the certificate into PEM, add Apple's intermediate certificate, and bundle everything with the key into a .p12.
openssl x509 -inform DER -in distribution.cer -out dist.pem
curl -fsSLo wwdr.cer https://www.apple.com/certificateauthority/AppleWWDRCAG3.cer
openssl x509 -inform DER -in wwdr.cer -out wwdr.pem

# Choose a strong password. You will store it as IOS_DIST_CERT_PASSWORD.
# OpenSSL 3.x: keep -legacy (macOS `security import` rejects OpenSSL 3's default .p12 encryption).
# LibreSSL (macOS /usr/bin/openssl): remove -legacy, which LibreSSL doesn't know.
openssl pkcs12 -export -legacy -inkey dist.key -in dist.pem -certfile wwdr.pem \
  -name "Apple Distribution" -out dist.p12

# 5. Check it: this should print "Apple Distribution: <Your Name> (<TEAMID>)".
openssl pkcs12 -in dist.p12 -nokeys -legacy | openssl x509 -noout -subject   # LibreSSL: remove -legacy

# 6. Base64 for the GitHub secret (one line).
base64 -i dist.p12 | tr -d '\n' > dist.p12.b64   # macOS
# base64 -w0 dist.p12 > dist.p12.b64             # Linux
```

Keep `dist.p12` and its password in your password manager, then delete `dist.key`, `dist.csr`, `dist.pem` and the `.b64`
file. The certificate is valid for one year, and a team can hold only a few Apple Distribution certificates at a time.
When it expires:
1. Repeat this step.
2. Replace the two `IOS_DIST_CERT_*` secrets.
3. Revoke the old certificate in the portal.

The next run creates a matching profile.

## 6. Add the GitHub secrets and variable

The job uses the `production` environment. The deploy tasks may already have created it; if not, create it:
1. Open the repository's **Settings > Environments > New environment** and name it `production`.
2. Under **Deployment branches and tags**, choose **Selected branches and tags** and add `master`, so nothing else can use
   these secrets.

Add these **environment secrets** to `production`. The names must match exactly.

| Secret | Value |
| --- | --- |
| `ASC_KEY_ID` | The Key ID from step 4 (10 characters) |
| `ASC_ISSUER_ID` | The Issuer ID from step 4 (a UUID) |
| `ASC_KEY_P8` | The **base64** of `AuthKey_<KEYID>.p8`, on one line |
| `IOS_DIST_CERT_P12` | The base64 of `dist.p12`, on one line (the contents of `dist.p12.b64`) |
| `IOS_DIST_CERT_PASSWORD` | The `.p12` password from step 5 |

Add one **variable**: `API_BASE_URL`, the production API address with a trailing slash, for example
`https://api.example.com/`. Make it a repository variable (Settings > Secrets and variables > Actions > Variables), because
the unsigned `ios` and `android` jobs read it too and they don't use the environment. A `production` environment variable
with the same name would override it for this job only.

The same with the GitHub CLI, run from the repository folder:

```sh
gh secret set ASC_KEY_ID --env production --body "ABCDE12345"
gh secret set ASC_ISSUER_ID --env production --body "00000000-0000-0000-0000-000000000000"
base64 -i AuthKey_ABCDE12345.p8 | tr -d '\n' | gh secret set ASC_KEY_P8 --env production
gh secret set IOS_DIST_CERT_P12 --env production < dist.p12.b64
gh secret set IOS_DIST_CERT_PASSWORD --env production       # prompts; nothing lands in shell history
gh variable set API_BASE_URL --body "https://api.example.com/"
```

The job checks the five secrets before doing anything:
- **None set:** a "TestFlight upload skipped" notice, and the job is green.
- **Some set:** the run fails and lists the missing names.
- **All set, no `API_BASE_URL`:** the run fails.

## 7. Run the workflow

Either push a change under `src/CustomGolfStats.App/**` (or the other paths in the workflow's `on:` list) to `master`, or
open **Actions > Mobile release builds > Run workflow** and choose `master`.

Open the **ios-testflight** job:
1. "Fetch or create the App Store provisioning profile" says either `Reusing App Store profile …` or
   `Created App Store profile …`.
2. "Upload to TestFlight" ends with `No errors uploading`, and a notice gives the build number.
3. The IPA is attached to the run as `ios-testflight-<run number>` for 14 days. If the upload failed, you can upload this
   file by hand with Apple's Transporter app, without rebuilding.

App Store Connect processes the build for 5–30 minutes. Apple emails you when it's ready, and it appears under the app's
**TestFlight** tab.

Don't use **Re-run jobs** for a new upload. A re-run keeps the run number, so App Store Connect rejects the duplicate
build number. Start a new run instead.

## 8. Add internal testers

Internal testers are App Store Connect users on your team, up to 100 people. Builds reach them without Beta App Review.

1. Each tester needs an App Store Connect user. Go to **Users and Access > +**, enter their name and email, and give them a
   role such as **Customer Support** or **Marketing**; any role can test. They accept the email invitation. You, as the
   Account Holder, are already a user.
2. Open the app > **TestFlight > Internal Testing > +** to create a group, for example `Team`. Turn on **Enable automatic
   distribution** so every new build goes to the group.
3. Add the testers to the group. If the group existed before the build, select the build and add it to the group.
4. Testers install **TestFlight** from the App Store and accept the invitation from the email. Each build lasts 90 days.

**External testers** (anyone with an email or a public link, up to 10,000) need more first:
- the **Test Information** page filled in: a feedback email, a description, and a sign-in account for the reviewer;
- a privacy policy URL;
- a Beta App Review of the first build of each version, which usually takes about a day.

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `TestFlight secrets incomplete: Missing … ` | One or more of the five secrets is missing from `production`. Check the names in step 6. |
| `ASC_KEY_P8 invalid` | The secret holds the raw `.p8` and not its base64, or the base64 is wrapped across lines. Redo it with `base64 -i … \| tr -d '\n'`. |
| `security: SecKeychainItemImport: MAC verification failed` | Wrong `IOS_DIST_CERT_PASSWORD`, or an OpenSSL 3 `.p12` made without `-legacy` (step 5). |
| `No distribution identity` | The `.p12` lacks the private key, or holds a Development certificate. Redo step 5 with an Apple Distribution certificate. |
| `no iOS App ID for com.customgolfstats.app` | Step 2 isn't done. |
| `no distribution certificate with serial …` | The certificate was revoked or belongs to another team. Redo step 5. |
| HTTP 401 from the profile step | The Key ID, the Issuer ID or the `.p8` don't belong together, or the key was revoked. |
| HTTP 403 from the profile step | The key's role can't create profiles. See the end of step 4. |
| altool: `The bundle version must be higher than the previously uploaded version` | A re-run reused a run number. Start a new run. |
| altool: `No suitable application records were found` | Step 3 isn't done, or the record has another bundle id. |
| Xcode / workload mismatch errors | The `setup-xcode` version and `--version` on `dotnet workload install` move together. See the comments in the `ios` job. |
