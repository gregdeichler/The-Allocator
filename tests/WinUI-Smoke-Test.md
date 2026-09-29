# WinUI smoke and accessibility test

Run this checklist on an interactive elevated Windows desktop after CI publishes `artifacts/winui`. Record the OS build, display scale, contrast theme, package used, and result for each run.

## Launch and shell

- Launch `TheAllocator.exe` and accept UAC. Confirm the custom title bar, machine identity, administrator label, and diagnostics menu render.
- Resize to 1024×720, 1366×768, and 1920×1080. Confirm the stage rail changes to the compact horizontal layout and no action is clipped.
- Close and relaunch. Confirm the previous valid window bounds are restored.
- Repeat with Windows 10 22H2 and Windows 11. Confirm Windows 10 uses a solid backdrop without losing contrast.

## Backup workflow

- Use keyboard only to choose **Set up backup**, select a profile, toggle printers, browse for a destination, and open Review.
- Confirm missing profiles, a cancelled picker, an invalid destination, and missing 7-Zip files produce inline notices without terminating the app.
- Confirm every readiness result includes an icon, text status, title, and detail.
- Begin a disposable backup. Confirm navigation and window close are blocked during transfer, current activity is announced, and raw 7-Zip lines do not become page headings.
- Confirm Finish opens the backup folder and log and that the generated package matches the established sidecar layout.

## Restore workflow

- Select a valid 1.1 package and confirm the source user, computer, Windows version, creation time, and saved printers appear.
- Exercise existing-account, manual local-account, and manual domain-account paths. Confirm cross-user restore without an existing profile is blocked.
- Confirm a corrupt or missing package remains on Setup with an inline corrective error.
- Complete a disposable restore. Confirm Finish offers a reboot, cancelling the dialog leaves the app open, and accepting it invokes the reboot action.
- After reboot, sign in as the restored account and verify the desktop loads and portable content is present.

## Accessibility

- Complete both workflows with Narrator and keyboard only. Confirm names, roles, values, focus order, live transfer updates, and dialog focus.
- Repeat at 200% text size and display scaling with Magnifier.
- Repeat in Aquatic, Desert, Dusk, and Night sky contrast themes. Confirm no hard-coded surface obscures content and status does not depend on color.
- Run Accessibility Insights FastPass on Home, Setup, Review, Transfer, Finish, and the reboot dialog; treat critical failures as release blockers.
