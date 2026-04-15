# The Allocator 1.0.0

Initial technician test release.

## Included In This Release

- guided old-computer backup workflow
- guided new-computer restore workflow
- per-user backup packaging
- sidecar metadata and printer definition files
- same-user overwrite restore path
- restore logging and backup logging
- reboot action at the end of restore
- bundled 7-Zip support files

## Known Limitations

- printer restore is still under active validation across more driver and device combinations
- merge restore should only be used when a healthy target profile already exists
- cross-user overwrite restore is intentionally blocked

## Recommended Test Path

1. Create a backup on the source machine.
2. Reimage or prepare the destination machine.
3. Sign in with a separate administrator account.
4. Restore the same user with `Overwrite`.
5. Reboot when prompted.
6. Sign into the restored account.
7. Validate profile contents and printer behavior.
