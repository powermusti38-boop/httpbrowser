# Project workflow

- The upstream repository is `powermusti38-boop/httpbrowser` on GitHub.
- HTTP playback is enabled directly in every browser window. Do not bring back a separate compatibility window unless requested.
- Produce distributable Windows builds with `.github/workflows/windows-build.yml` on GitHub Actions. Push the authorized change, inspect the workflow result, and link the downloadable artifact. Local targeted checks are allowed when useful, but GitHub is the normal compilation and packaging service.
- Never commit local browser profiles, settings, credentials, session URLs, diagnostic output, build directories, or generated media fixtures. `verification/`, `diagnostics/`, `Tests/fixtures/`, `bin/`, `obj/`, and `release/` stay local.
- This is a browser-only application. Do not reintroduce external player integration, injected video inspection scripts, or media fixture tooling unless requested.
- Keep HTTPS certificate validation, SmartScreen, and the existing handling of external protocols and automatic popups intact.
