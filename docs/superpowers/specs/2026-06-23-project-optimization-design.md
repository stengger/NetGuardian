# NetGuardian Project Optimization Design

Date: 2026-06-23

## Goal

Improve NetGuardian's maintainability, test confidence, and day-to-day usability without changing the core network recovery behavior.

This optimization has two tracks:

1. Engineering reliability optimization.
2. UI and experience optimization.

The work should leave the existing monitor, Wi-Fi validation, recovery policy, DNS tools, tray behavior, and startup behavior functionally compatible with the current app.

## Current Facts

- The solution builds with .NET 8 and WPF.
- `src/NetGuardian.Core` already separates Wi-Fi, connectivity, network tools, settings, and models.
- `src/NetGuardian.App/MainWindow.xaml.cs` is large and coordinates UI events, monitoring, recovery, DNS, tools, tray behavior, and startup tasks.
- Existing tests are lightweight console tests with no external test framework.
- The current test suite covers parsing, settings round trip, URL normalization, and monitor policy bounds.
- The working tree has untracked helper scripts under `scripts/` that look like temporary XAML or symbol-query utilities.

## Scope

### Engineering Reliability

1. Repository hygiene
   - Decide how to handle the untracked helper scripts in `scripts/`.
   - Keep useful repeatable tools only if they have clear names and a documented purpose.
   - Remove or ignore one-off rewrite/query tools that are not part of the supported workflow.

2. Focused test additions
   - Add tests that do not require administrator permission, live Wi-Fi, real network access, or UI automation.
   - Prefer tests around pure logic:
     - Localization key parity between Chinese and English resources.
     - Settings load/save normalization and invalid profile cleanup.
     - Monitor policy boundary behavior.
     - URL or command argument helper logic if extracted.

3. Small boundary extraction
   - Extract only low-risk logic from `MainWindow.xaml.cs`.
   - Good candidates are pure formatting, localization validation, command result handling, or settings/profile normalization.
   - Avoid extracting the full recovery workflow in this pass unless the existing structure makes a small, tested extraction natural.

### UI And Experience

1. Information hierarchy
   - Improve labels, section grouping, and status messages where they reduce confusion.
   - Keep the first screen focused on current state, next check, failure streak, and recent events.
   - Preserve existing navigation and feature set.

2. Interaction polish
   - Make destructive or connection-disrupting actions clearly confirm before running.
   - Keep buttons enabled/disabled consistently during async operations.
   - Improve empty states and selected-row feedback for Wi-Fi and tools pages.

3. Layout stability
   - Avoid large visual redesigns.
   - Adjust spacing, column widths, text wrapping, or minimum sizes only where current controls can become cramped or unclear.
   - Do not introduce decorative visual elements or a landing page.

4. Copy consistency
   - Keep Chinese as the default language and English as a complete secondary language.
   - Any added UI text must be present in both languages.
   - Prefer concise operational wording over explanatory paragraphs in the app UI.

## Out Of Scope

- Adding high-risk recovery actions such as WLAN service restart, adapter restart, or network stack reset.
- Replacing WPF, WPF-UI, or the current project structure.
- Adding a full MVVM rewrite.
- Adding external test frameworks unless the current console test approach blocks the work.
- Changing saved settings format in a breaking way.
- Reworking icons, branding, or creating a marketing-style first screen.

## Architecture Approach

Keep the current app architecture but reduce pressure around the largest file through narrow seams.

Preferred extraction pattern:

- Move pure or near-pure logic into `NetGuardian.Core` when it is domain logic.
- Move WPF-specific helpers into `NetGuardian.App` only when they depend on controls or WPF types.
- Keep interfaces small and only introduce them when a test needs to replace a system boundary.

Examples:

- `Localizer` can expose supported languages or all keys so tests can assert parity without reflection.
- Settings/profile cleanup should remain in `SettingsStore` or model-level normalization.
- UI text additions should flow through `Localizer.T(...)`.

## Testing Strategy

Continue using the existing lightweight console test project unless a specific change requires a stronger test harness.

Required verification after implementation:

1. Run the test console and confirm all tests pass.
2. Run the solution build and confirm 0 errors.
3. Inspect `git diff` to confirm changes are limited to the optimization scope.

If UI layout files change, also perform a quick manual app launch check when feasible. If launching is blocked by environment or elevation, report that limitation explicitly.

## Implementation Order

1. Add or improve tests for localization/settings/policy behavior.
2. Make the smallest production changes needed to pass those tests.
3. Clean up or classify untracked helper scripts.
4. Apply UI/experience polish in small batches, with localization updates for every visible string.
5. Run verification.

## Risks And Mitigations

- Risk: UI changes accidentally alter network behavior.
  - Mitigation: keep UI changes limited to presentation, copy, layout, and state feedback.

- Risk: extracting from `MainWindow.xaml.cs` creates churn.
  - Mitigation: only extract pure logic with clear tests; avoid broad MVVM conversion.

- Risk: localization becomes inconsistent.
  - Mitigation: add key parity tests before adding UI strings.

- Risk: helper script cleanup removes useful local tools.
  - Mitigation: inspect each untracked script and either document, ignore, or remove based on clear purpose.

## Acceptance Criteria

- The project builds successfully.
- The test console passes.
- Any new visible UI string exists in both supported languages.
- No core recovery behavior is intentionally changed.
- Untracked helper scripts are no longer ambiguous project debris.
- UI/experience changes are incremental and aligned with the existing WPF-UI style.
