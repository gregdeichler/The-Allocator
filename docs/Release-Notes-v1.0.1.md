# The Allocator 1.0.1

Focused restore hardening release.

## Included In This Release

- source operating system is now recorded in backup metadata
- restore logs now include both source and target operating systems
- legacy source restores are treated more safely during restore
- Windows 7 and other pre-Windows-10 sources now skip full profile hive restore
- legacy restores also skip Windows shell state under `AppData\Local\Microsoft\Windows`

## Why This Release Exists

Testing showed that a full same-user profile transplant is not equally safe across all Windows versions. Modern Windows-to-Windows restores and older Windows-to-Windows restores need different assumptions. This release adds the first OS-aware restore rule set without changing the validated modern restore path more than necessary.

## Testing Focus

1. Repeat a known-good same-user Windows 10/11 style restore and confirm behavior is unchanged.
2. Test a Windows 7 source backup restored to Windows 11 and confirm login behavior improves.
3. Review restore logs to confirm source and target OS values are being recorded correctly.
