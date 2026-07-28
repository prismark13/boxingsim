# Shipping BoxingSim

One command, one file, no installer and no runtime for the user to fetch:

```
dotnet publish src/BoxingSim.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out
```

`out/BoxingSim.exe` — about 67 MB, and that is the whole delivery. Verified by copying that single file into an
empty folder and running it: it starts, loads the full roster, and plays a fight with the real crowd recordings.

Everything travels inside the executable. The roster and the nine audio layers are embedded as resources *and*
carried by the self-extracting bundle, so there is no `assets` or `data` folder to lose and no silent fallback
to synthesised sound. Release builds emit no debug symbols alongside.

## Sending it to a tester

Two builds, and which you want depends on whether the tester will install anything.

```
# standalone - needs nothing on their machine. 67 MB, 61 MB zipped.
dotnet publish src/BoxingSim.Desktop -c Release -r win-x64 --self-contained true  -p:PublishSingleFile=true -o out

# small - needs the .NET 10 Desktop Runtime installed first. 10 MB, 7.7 MB zipped.
dotnet publish src/BoxingSim.Desktop -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o out
```

**You cannot email either one as an attachment.** Gmail, Outlook and every other major provider refuse `.exe`
outright, and they look *inside* `.zip` files too, so zipping it does not help. This is about the file type, not
the size — the 7.7 MB zip is rejected exactly like the 61 MB one.

What actually works:

- **A link.** Put the standalone build on OneDrive, Google Drive or WeTransfer and email the link. Best option:
  the tester downloads one file, double-clicks it, and needs nothing installed. Nothing to explain.
- **A GitHub Release.** Attach the exe to a release and send that link. Versioned and tidy, but note that a
  release on a public repository is public — anyone with the URL can download it.
- **A password-protected zip**, attached. Encryption stops the provider scanning inside, so it goes through.
  Needs 7-Zip or similar; `Compress-Archive` cannot set a password. Send the password separately.

If you use the small build, the tester needs the **.NET 10 Desktop Runtime** first
(`https://dotnet.microsoft.com/download/dotnet/10.0`, "Desktop Runtime", x64). That is a second download and an
installer, so it is usually less trouble to send the standalone one over a link.

## The one real obstacle: it is unsigned

Windows will treat it as an unknown application.

- **SmartScreen** shows "Windows protected your PC" on first run. A user has to click More info → Run anyway.
- **Smart App Control**, which is on by default on new Windows 11 installs, may refuse to run it at all, with
  no obvious way for the user to override.

Nothing in the code fixes that; it is a matter of code signing.

- An **OV certificate** (~£200–400/yr) stops the "unknown publisher" wording, but SmartScreen reputation still
  builds up over downloads.
- An **EV certificate** carries SmartScreen reputation immediately and satisfies Smart App Control. Dearer, and
  usually needs a hardware token or cloud signing service.
- **Unsigned** is fine for people you hand it to directly and can tell to click through. It is not fine for
  strangers downloading from a link.

## Retired: the web build

`src/BoxingSim.App` is a Blazor WASM version that development has moved on from.

The public site at `prismark13.github.io/boxingsim` has been taken down and the Pages configuration removed.
Note that a Pages site on a public repository cannot be made *private* — private Pages needs GitHub Enterprise
Cloud — so removing it is the only way to make it non-public. To bring it back: re-enable Pages in the
repository settings and run the (now manual-only) deploy workflow.

**Do not delete the project.** The fighter roster lives at `src/BoxingSim.App/wwwroot/data/fighters.json` and
the desktop app embeds it from there, so removing the project would take the roster with it. If the project is
ever to go properly, move that file somewhere neutral first and repoint both csproj references.
