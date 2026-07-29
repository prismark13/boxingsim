# Shipping BoxingSim through the Microsoft Store

## Why the Store rather than a download

An unsigned executable handed to somebody is met by SmartScreen — *"Windows protected your PC"* — and on a
clean Windows 11 install with Smart App Control enabled it can be **refused outright with no way through**.
That is not hypothetical: it is what happens on this development machine, and it is why v0.5.1 shipped with
a folder zip alongside the single-file exe.

The ways out, and what they cost:

| | cost | fixes SmartScreen | fixes Smart App Control |
|---|---|---|---|
| Unsigned download | free | no | no |
| EV code-signing certificate | $220–580/yr | yes | yes |
| Azure Trusted Signing | ~$120/yr | yes | yes |
| **Microsoft Store** | **free** | **yes** | **yes** |

Microsoft signs Store packages, so there is nothing to click through and nothing for Smart App Control to
object to. Individual developer registration stopped costing $19 in September 2025.

Trusted Signing was set up and abandoned: its individual-developer route rejects with *"you do not meet
requirements to create a Verified ID"*, and the Azure resources were deleted rather than left billing.

## Building the package

```powershell
.\packaging\pack.ps1              # unsigned .msix, which is what Partner Center wants
.\packaging\pack.ps1 -SelfSign    # also signs it locally so it can be installed and tested here
```

Output lands in `out-msix\BoxingSim.msix`, about 65 MB.

`makeappx.exe` and `signtool.exe` come from the `Microsoft.Windows.SDK.BuildTools` NuGet package rather
than a multi-gigabyte Windows SDK install. If they are missing, restore them with any throwaway project
that references `Microsoft.Windows.SDK.BuildTools`.

### Testing the package locally

A Store submission takes the package **unsigned** — Microsoft applies its own signature. `-SelfSign` exists
only so the thing can be installed here first, because a package that does not install is worthless and
v0.5.1 taught us not to assume.

Installing a self-signed package needs its certificate trusted at machine level, which needs an elevated
prompt:

```powershell
# in an ADMIN PowerShell, after running pack.ps1 -SelfSign
Import-Certificate -FilePath out-msix\test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage out-msix\BoxingSim.msix
```

Without elevation you can still confirm the payload is sound by unpacking it and running the executable
inside:

```powershell
makeappx unpack /p out-msix\BoxingSim.msix /d some\folder /o
.\some\folder\BoxingSim.exe
```

## Submitting

1. **Register** at [Partner Center](https://partner.microsoft.com/dashboard) — free for individuals, in
   about 200 markets, no card needed. There is an identity check; it is an ordinary one, not the Verified
   ID/AU10TIX gate that Trusted Signing failed on.
2. **Reserve the name** "BoxingSim" (or whatever it ends up called) under Apps and games → New product.
3. **Copy the identity values Partner Center issues** into `AppxManifest.xml`, replacing the three
   placeholders:
   - `Identity/@Name` → the Package/Identity/Name it gives you
   - `Identity/@Publisher` → the Package/Identity/Publisher (`CN=…`)
   - `Properties/PublisherDisplayName` → your publisher display name

   A mismatch here is the commonest reason a first submission is rejected.
4. **Rebuild** with `pack.ps1` and upload `out-msix\BoxingSim.msix` to the submission's Packages step.
5. Fill in the listing: description, at least one screenshot (1366×768 or larger), age rating
   questionnaire, and privacy — the app collects nothing and makes no network calls, which makes that
   section short.
6. Submit. First certification usually takes a few days.

## Notes on the manifest

- `runFullTrust` is the only capability. The sim needs no network, camera or location; it reads its roster
  from resources embedded in the executable and writes one save file to the user's app-data folder.
- The tile assets in `src/BoxingSim.Desktop/assets/store` are **generated** from the same bell geometry the
  app draws, by the logo generator, so the Store tile and the in-app mark cannot drift apart.
- `Version` must increase with every submission. The fourth part must be `0` — the Store reserves it.
