# WinUI technician console architecture

The production UI is `TheAllocator.WinUI`, an unpackaged WinUI 3 application. `TheAllocator.Core` owns the existing models, services, backup format, restore behavior, and the UI-neutral workflow contracts. The original root WPF project references the same core and remains available as a rollback build during the 2.0 pilot.

## State flow

`WorkflowCoordinator` is the canonical source for the active computer role, stage, highest completed stage, and transfer navigation lock. `MainViewModel` translates technician choices into the existing `AllocatorSession`; views never parse manifests or invoke migration services directly.

Both workflows use the same sequence:

1. Setup gathers the role-specific choices.
2. Review translates validation into structured `PreflightCheck` items.
3. Transfer receives typed `OperationProgress` updates from adapters around the proven services.
4. Finish exposes the result, log, and safe next action.

The service interfaces preserve the existing public string-progress methods for WPF compatibility. Explicit interface implementations map service messages to stable phases for WinUI without placing 7-Zip output in the primary status surface.

## Deployment

The WinUI project sets `WindowsPackageType=None`, bundles Windows App SDK and .NET dependencies, publishes for x64, and uses the existing `requireAdministrator` manifest. Assets and portable 7-Zip files retain their established output paths. `Build-Release.ps1` publishes the WinUI project into the existing installer payload, so detection, shortcuts, uninstall identity, and installation location do not change.

## Accessibility and fallback behavior

The normal theme is light. All application colors are semantic theme resources, with a HighContrast dictionary mapped to Windows system colors. Mica is the window backdrop where supported and falls back to a solid surface on Windows 10. The 1024×720 minimum layout keeps actions in a sticky footer and long content in scrollable regions; native controls supply keyboard, text scaling, and UI Automation behavior.
