# AutoInvoice: Web test succeeds, Worker reports Keyset does not exist

## What was confirmed

`InvoiceProviderSettingService.TestLoginAsync` reads and decrypts the **stored** password before calling Viettel. A successful Web test therefore confirms that the Web process can read that setting; it does not confirm that a separate Windows Worker can open the same encryption key.

The user confirmed that Worker and Web run on the same production server. The current workspace is a separate development machine with no running GaoApp Worker/IIS process. Its checks do not establish production service-account access.

The shared Infrastructure registration already uses `ApplicationName=GaoApp`, machine-scope DPAPI on Windows, and a PFX loaded with `EphemeralKeySet` when configured. The development machine's two existing DPAPI keys were successfully read using the Worker executable in check mode. No production key, password, certificate, invoice or service was changed during this work. The production Worker's effective configuration/identity has not been verified.

`Keyset does not exist` is a Windows key access failure. Its remedy depends on the deployed host: a different key ring, a DPAPI key originating from another machine/account, or the wrong/inaccessible certificate private key. Re-entering the correct Viettel password on Web does not correct these differences.

## Changes

- Credential failures now distinguish Windows key access errors from an unconfirmed missing key. Messages identify the processing machine and direct operators to Worker key configuration instead of automatically asking for another Viettel password.
- The Worker no longer generates keys in the shared key ring. Web manages key creation/rotation. A misconfigured Worker must not create replacement keys using a different certificate/DPAPI fallback while it is trying to decrypt Web credentials.
- The persisted error code remains `InvoiceProvider.CredentialKeyUnavailable`. The Worker still blocks issuance while decryption fails and retries on the next eligible cycle. Existing recovery clears only eligible credential incidents after local credential validation succeeds.
- Worker commands `--check-keys` and `--check-credentials` never start hosted services. They disable key generation, do not create a key directory/probe, do not write heartbeat/queue/provider records, do not migrate legacy plaintext, and never call Viettel. Credential checking reads all active Viettel settings and reports setting/store IDs and success/failure without showing passwords or ciphertext.
- `scripts/auto-invoice/Sync-GaoAppAutoInvoiceWorkerProtection.ps1` copies only `DataProtection` values into the external Worker host file, by default `C:\GaoAppData\DeployConfig\worker.appsettings.Production.json`. The IIS source includes Web JSON settings, machine/pool environment and `web.config` overrides; a private host-environment file is also supported. It preserves other settings from the existing host file (or bootstraps from the old Worker JSON), backs up existing host files with their ACL, refuses to write while the service is running, and supports `-WhatIf`. Published JSON is unchanged.
- With `-RepairFilePermissions`, the script verifies the installed service executable matches `WorkerDirectory`, obtains its actual account, preserves an ACL snapshot, and adds that account's Modify access to the key directory / Read access to retained XML keys, original PFX and external host file. Existing ACL entries, owner and audit settings remain intact. It never creates/replaces/re-encrypts keys, changes the service account, starts/stops a service, or publishes an invoice. Copy `WorkerProtection.Common.ps1` beside the synchronization script when transferring it to the server.
- Worker Debug/publish now share external host-file loading, with environment/CLI precedence retained. Publish uses a secret-free template; the installer updates the existing service path without deleting its identity/recovery/startup. See [Visual Studio, publish and upgrades](AUTO-INVOICE-VISUAL-STUDIO-PUBLISH-20261010.md).

## Repair on the machine hosting the affected Worker

Use the existing Web key ring and certificate. Do not delete keys, copy development DPAPI keys over production keys, or create a replacement PFX.

1. Check which machine/account runs Web and which runs the Worker. If they are on different machines, machine-scope DPAPI keys are not portable. Run the Worker on the original Web machine or use a reviewed migration of the existing keys to the same PFX. Copying XML alone or configuring a new PFX cannot unwrap older DPAPI keys.
2. If both hosts share the same machine but Worker protection configuration differs, preview synchronization from the actual GaoApp Web application pool in an elevated PowerShell 7 session. Use the real Worker installation directory and pool name:

   ```powershell
   .\scripts\auto-invoice\Sync-GaoAppAutoInvoiceWorkerProtection.ps1 `
     -AppPoolName 'GaoAppPool' -RepairFilePermissions -WhatIf
   ```

   The script resolves the Worker directory from the existing `GaoApp.AutoInvoiceWorker` service, and resolves the Web physical path when the pool belongs to exactly one application. If your service name differs, supply `-ServiceName`. If the pool is shared, supply `-WebDirectory` for the intended Web application. `-WorkerDirectory` remains available for an explicit path, which is verified against the installed service before permission changes. For an explicit private host-environment JSON, use `-HostEnvironmentPath` instead of `-AppPoolName`. Never use the placeholder example as real configuration. Pool/service names in the example are not proof of the production topology.

3. Stop the affected Worker through the normal server procedure, then run the same synchronization without `-WhatIf`. The optional permission repair operates only on the verified service account and protection paths. For file-based PFX the code imports private keys into memory; it does not require installing the PFX in a Windows certificate store. Restrict file access to the actual application/service accounts and administrators.
4. Deploy the updated Worker, pin its Production/external configuration using the updated Install script, then run checks **under the identity and environment used by that Worker**, while issuance remains stopped. Running these as an administrator alone does not prove service-account access:

   ```powershell
   & 'C:\GaoApp\AutoInvoiceWorker\GaoApp.AutoInvoiceWorker.exe' --environment Production --check-keys
   & 'C:\GaoApp\AutoInvoiceWorker\GaoApp.AutoInvoiceWorker.exe' --environment Production --check-credentials
   ```

   Exit code 0 means the requested check passed; empty key rings/no active provider settings/failures return 1. `--check-keys` checks every retained key, so an inaccessible unused historical key can also fail this check. `--check-credentials` verifies the passwords actually needed by active settings. No invoice is issued by either command.
5. Once active credentials decrypt successfully, restart the Worker through the normal server procedure. Its existing next cycle clears the host credential error and resumes the configured queue. Re-enter a provider password only if the required original key is confirmed irrecoverable.

## Verification

- Worker build: successful, 0 warnings/errors.
- Initial targeted credential/key tests: 11 passed. Extended same-server/certificate selection: 10 passed, including the new unchanged-password/key-ring recovery case.
- Existing AutoInvoice/protector regression selection: 86 passed.
- Actual Worker `--check-keys` on the development machine: both existing keys decrypted, exit 0; no DB/issuance work was started.
- Actual Worker check mode with synthetic empty/missing key directories: exit 1, no new keys or directory created.
- Synthetic synchronization script test: preview leaves files unchanged, external settings share Web protection while published JSON stays unchanged, unrelated SQL/other settings remain intact across upgrades, backups retain original content, secrets are omitted from output, and a running service blocks mutation.
- Extended script tests: Web JSON/environment/`web.config` precedence, relative key-path resolution, service executable/account matching, ACL-only preview, preserved ACL snapshot, and access to existing keys with disabled inheritance.
- Certificate regression: a Worker with the wrong PFX cannot decrypt; switching it to the original Web PFX restores the existing stored invoice password without replacing keys or re-entering/re-saving the password.
- Final configuration/credential/key regression selection: 18 passed, including actual Generic Host argument-provider ordering.
- Actual Debug Development check: both local keys and the saved password for setting #2/store #1 decrypted, exit 0. Published Visual Studio win-x64 profile also completed the read-only check from System32 using an external Production host file and the Development connection held only in memory; existing key hashes stayed unchanged.
- Synthetic installer tests: upgrade preserves identity/startup/recovery and custom host-file path; preview and validation failure perform no service mutation; new install uses a virtual account and remains stopped/Manual.

Microsoft references: [DPAPI and X.509 key protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest?view=aspnetcore-8.0), [certificate decryptor implementation](https://github.com/dotnet/aspnetcore/blob/v8.0.0/src/DataProtection/DataProtection/src/XmlEncryption/EncryptedXmlDecryptor.cs).
