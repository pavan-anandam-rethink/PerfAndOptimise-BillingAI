# Inbound sample files

Synthetic X12 5010 responses for local runs and tests. Every name, id, and amount is made up; there is no PHI.

| File | Transaction | Use |
| --- | --- | --- |
| `AV_999_20261104_0001.x12` | 999 implementation acknowledgment (accepted) | Happy path, smallest file |
| `AV_277CA_20261104_0002.x12` | 277CA claim acknowledgment | Claim-level status |
| `AV_835_20261104_0003.x12` | 835 remittance | Payment file |
| `AV_TA1_20261104_0004.x12` | TA1 interchange acknowledgment | Envelope-only file |

## Running a local download

1. Start an SFTP server (for example `atmoz/sftp`) with a `ReceiveFiles` folder and copy these files into it.
2. Point `Clearinghouses:Availity:*` at Key Vault secrets in your dev vault (legacy rule: a value without `=` is a Key Vault
   secret name, so a plain host name such as `localhost` is looked up in the vault, not used as is).
3. Post the clearing house id with `src/Rethink.BH.Billing.ClearingHouse.Inbound.Api/inbound-download.http`.

## Scenarios worth covering

- All files accepted and queued: `200`, files removed from SFTP.
- Billing returns `false` or a non-success status for one file: `500` with `{"failed":1,"total":4}`, the file stays on SFTP.
- Billing returns `{"success":true,"queued":false}`: the file is stored and registered, but stays on SFTP.
- The SFTP folder is empty: `204`.
- Unknown clearing house id (for example `99`): `400` with `Invalid clearing house ID.`
- A large file (generate one by repeating the 835 `CLP` loop) to observe memory: the service holds one pooled copy per file in flight.
