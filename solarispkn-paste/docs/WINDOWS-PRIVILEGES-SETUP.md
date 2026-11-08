# Windows installation tiers and privileged capabilities

**Experimental Windows preview: 0.3.0-alpha.2**.

| Capability | Standard (HKCU/AppData) | Advanced (HKLM/Program Files) |
|---|---|---|
| Installer | No UAC | Explicit UAC for all-users install |
| Explorer integration | Per-user | Machine-wide; optional explicit elevated paste |
| BLAKE3, FileStream, safe adaptive copying | Yes | Yes |
| OVERLAPPED, IOCP, CopyFile2, NO_BUFFERING | May run without elevation if files are accessible | Same APIs; not inherently faster because of UAC |
| Hardware inventory | Available properties for user token | May see extra properties depending on controller/ACL |
| Protected target directories | Normal ACL rules | Separate, consented UAC elevation |
| SeManageVolumePrivilege / SetFileValidData | Generally unavailable | Can be present after elevation, but **no privileged speed backend is implemented** |
| SeBackupPrivilege / SeRestorePrivilege | Generally unavailable | May be present but **not used by current engine** |
| Signed elevated service | Not present | **Not implemented yet** |
| Unvalidated experimental I/O options | Hidden / disabled as an interface product choice | Exposed only as opt-in, not necessarily privileged |
| Kernel driver | Not present | Not present: separate Kernel Lab research |

The original architecture envisaged **Standard / Advanced service / Kernel Lab**. The new installer implements per-user vs per-machine placement and UAC-backed protected-path copy. The planned minimal-privilege **signed Advanced service is still pending**. This distinction must remain visible to users.

The app now includes a read-only Windows process-token diagnostic in Settings showing actual elevation and SeManageVolumePrivilege, SeBackupPrivilege and SeRestorePrivilege availability. **Possession of a privilege is not the same as an enabled privilege.**

**Important:** Microsoft's SetFileValidData can skip zeroing allocated file data and needs enabled SeManageVolumePrivilege. It can expose stale data and Microsoft warns that it is rarely useful for ordinary copying. It is NOT activated. All accelerated variants must pass objective throughput and integrity tests before enabling. Kernel access itself is not a blanket speed improvement.

The EXE is self-contained (includes .NET runtime), but unsigned by a software publisher certificate. SmartScreen may warn. The installed older alpha.8 executable remains untouched until the user chooses to install this preview. Uninstallation removes shell/registry integration but keeps copies and backups.

References:
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfilevaliddata
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/privileges
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilea
- https://learn.microsoft.com/en-us/windows/win32/secbp/running-with-administrator-privileges/
