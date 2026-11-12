# Task 2 report: Auth
Worktree: /Users/ron/Documents/Development/custom-golf-stats/.claude/worktrees/agent-a27cfcadf96f0c4f3, branch wave2-task-2 (from 16e7cdf).

## Built
- AccountSignInManager (replaces SignInManager<Account>): CanSignInAsync false and ValidateSecurityStampAsync null when DisabledAt != null, so Identity's login and refresh return 401.
- POST /auth/external (AuthExternalSignIn): validates provider (apple|google) and idToken (400 keyed by field), validates via IExternalTokenValidator (null -> 401), finds by login, else by email (links only if provider says email verified; unverified match -> 401), else creates (email-less accounts get a sanitised user name). Disabled -> 401. Issues tokens via SignInManager with bearer scheme, same response as /auth/login.
- ExternalTokenValidator (real): JsonWebTokenHandler + ConfigurationManager<OpenIdConnectConfiguration>; issuers, audiences from Auth:Apple:ClientIds / Auth:Google:ClientIds; lifetime uses TimeProvider (LifetimeValidator; TokenValidationParameters has no TimeProvider property in 8.15). Any failure -> null.
- Packages (Directory.Packages.props lines appended + Api csproj refs): Microsoft.IdentityModel.JsonWebTokens 8.15.0, Microsoft.IdentityModel.Protocols.OpenIdConnect 8.15.0.
- Identity's other routes (confirmEmail, forgotPassword, manage/*) left default and documented in AuthFeature XML doc; /auth/manage/info kept for HarnessTests. Test harness files were not changed. Identity routes were not given explicit names (MapIdentityApi doesn't expose per-route naming).

## Evidence
RED: 9 of 11 AuthEndpointsTests failed before AuthFeature implementation. GREEN: full suite 161 passed, 0 failed, no warnings.

## Concerns
- Access tokens already issued to a later-disabled account keep working until they expire (1h); only login/refresh/external are blocked.
- First/last name from /auth/external only applied on account creation.
- Real Apple/Google network validation not exercised.
- Api csproj (shared) got two PackageReference lines; merge may need a trivial resolve.

## Interfaces for later tasks
ExternalTokenValidator, ExternalAuthOptions, AccountSignInManager (Features/Auth); no new test helpers.
