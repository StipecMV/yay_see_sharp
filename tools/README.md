# Screenshot tooling

## `ScreenshotDriver/`

A small console app that regenerates the README screenshots (`docs/screenshots/*.png`) against the
**current** UI. It boots the real compiled application (Avalonia on X11, Demo backend), navigates
each screen (Dashboard → Search → Search+detail → Installed → Settings) and saves each frame as a
PNG via `RenderTargetBitmap` — the same renderer the app itself uses, so the images reflect the
actual layout, theming and element sizes.

It is **not** part of the solution: it's a dev-only tool and never runs in CI or tests.

## Usage

```bash
./tools/generate-screenshots.sh                 # starts Xvfb if needed, then runs the driver
./tools/generate-screenshots.sh --theme dark    # dark theme
./tools/generate-screenshots.sh --lang sk       # Slovak UI
./tools/generate-screenshots.sh --size 1920x1080
```

Options (passed through to the driver):

| Option | Meaning | Default |
|---|---|---|
| `--out <dir>` | output directory | `<repo>/docs/screenshots` |
| `--size WxH` | window size | `1280x800` |
| `--theme L\|D\|S` | Light / Dark / System theme | `System` |
| `--lang en\|sk` | UI language | `en` |

If `DISPLAY` is already set the script uses that session as-is; otherwise it starts a private
Xvfb display (requires `xvfb` installed, e.g. `sudo apt install xvfb`) and tears it down after.

## Manual run

```bash
export DISPLAY=:99          # some X server / Xvfb
dotnet run --project tools/ScreenshotDriver --configuration Debug
```
