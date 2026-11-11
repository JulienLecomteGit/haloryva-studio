# Targeted recipe for the preserved preview.3 binary

This job downloads the original artifact from run 42931395281 and checks SHA256
4170BEFEC193F5049051D73744CFCAD6D300240CE840E791E2C469A7CECAB13C.
Only the test driver is compiled. The application is neither rebuilt nor changed.

Scope: remaining eight drawing tools; text accepted/cancelled; Ctrl+Z/Y and
Ctrl+Alt+Backspace / Ctrl+Alt+Shift+Backspace; undo/redo order and new-history branch;
real PNG export via the existing controller action and native SaveFileDialog;
transparency, visible content, Unicode path, unchanged image after undo/redo,
cancelled export and empty-scene information.

Gestures use SendInput on a PID-verified foreground target. Text input is restricted
to the actual owned TextPrompt. Native file dialogs are driven by UI Automation
on a worker thread, restricted to this test process, a fresh filename and the
isolated test profile. See Microsoft guidance:
https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-threading-issues

The test selects tool settings through the same existing configuration/Changed
path; it does not test clicking each tool selector in the Studio. Scene input,
history shortcuts, text typing and saving execute the original binary's logic.
Screenshots and PNG exports are retained as evidence. No additional software is installed.

Runtime guards require a GitHub-hosted interactive Windows machine and the driver
under RUNNER_TEMP. No local execution, signing, installation, release, main update,
legacy 195 suite, or repeat of prior AltGr/short-click/MSIX suites.

Prepared on 2026-11-11. Results belong in the root BILAN once actually executed.
