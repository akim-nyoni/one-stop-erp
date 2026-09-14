# Licensing (vendor workflow)

Two components (brief §11):

- **`One4All.Licensing.Core`** — shared primitives referenced by the ERP (validation only, embeds the **public** key) and by the Generator (signing). Ed25519 signatures (BouncyCastle) over a canonical-JSON payload; compact token `payload_base64url.signature_base64url`.
- **`One4All.Licensing.Generator`** — vendor-only console tool that holds the **private** key (passphrase-protected: PBKDF2-SHA256 → AES-256-GCM). **Never shipped to customers.**

## Commands

```bash
# 1. One-time: create a signing key pair (kid supports rotation)
dotnet run --project src/One4All.Licensing.Generator -- keygen --kid key-2026-01 --out ./keys --passphrase "<vendor secret>"
#   -> keys/key-2026-01.key.json  (encrypted private key — keep secret, never commit)
#   -> keys/key-2026-01.pub.txt   (public key — embed in the ERP's trusted-key set)

# 2. Per customer: edit a payload template (see sample-licence-template.json) and sign it
dotnet run --project src/One4All.Licensing.Generator -- issue \
  --key ./keys/key-2026-01.key.json --passphrase "<vendor secret>" \
  --template docs/licensing/sample-licence-template.json --out customer.lic --kid key-2026-01

# 3. Verify a licence (what the ERP does at startup, offline)
dotnet run --project src/One4All.Licensing.Generator -- verify --lic customer.lic --pub ./keys/key-2026-01.pub.txt --kid key-2026-01

# Machine fingerprint of the current PC (reference gatherer)
dotnet run --project src/One4All.Licensing.Generator -- machineid
```

## What the validator enforces (`LicenseValidator`)

- Ed25519 **signature** against a trusted `kid` → tampering rejected.
- **Product** string must match the ERP's expected product.
- **Clock-rollback** detection via a monotonic high-water mark → `TamperDetected`.
- **Validity window** with grace: `Valid` → `Grace` (usable, warn) → `Expired` (hard-stop, blocked) or `ReadOnly` (soft-stop, view/print only). Customer data is never destroyed.
- **Machine binding** with fuzzy ≥3-of-4 component match (tolerates one hardware change).
- **Per-module** licensing/expiry (`IsModuleLicensed`) and **limits** (`GetLimit`/`GetModuleLimit`); `CORE` is always licensed. A locked module is hidden and its API returns 403.

Keys and `*.lic` files are gitignored.
