# MiniPaint

<p align="center">
  <img src="src/MiniPaint.App/Assets/SplashScreen.png" alt="MiniPaint splash screen" width="640" />
</p>

Local Windows 2D image editor — C# / .NET 10, Avalonia UI, SkiaSharp.

**Développé par Cursor, orchestré par Eric Kernin.**

## Requirements

- .NET SDK 10.x (`dotnet --list-sdks`)
- Avalonia project templates (optional, for scaffolding): `dotnet new install Avalonia.Templates`
- Avalonia + SkiaSharp restore via NuGet (no separate system installer)

## Build & run

```powershell
cd d:\MiniPaint
.\scripts\Get-Models.ps1          # AI models for Object Selection + Content-Aware Fill (~250 MB, once; build-debug.bat does it)
dotnet restore MiniPaint.slnx
dotnet build MiniPaint.slnx
dotnet test
dotnet run --project src\MiniPaint.App
# or: build-debug.bat run
```

The model files go to `models\MobileSAM\` and `models\LaMa\` (kept out of git, SHA-256 checked). Without them
everything works except **Object Selection** and **Content-Aware Fill**, which are disabled.

## Publish & pin to the Windows taskbar

Debug `bin\` builds and `.bat` launchers do **not** pin reliably. Use the Release self-contained publish:

```powershell
cd d:\MiniPaint
.\publish.bat
# same as: powershell -File .\scripts\Publish-MiniPaint.ps1
```

| What | Path |
|------|------|
| **Pin this** | `d:\MiniPaint\MiniPaint.lnk` (right-click → Pin to taskbar) |
| Or pin the exe | `d:\MiniPaint\publish\MiniPaint.exe` |
| Published folder | `d:\MiniPaint\publish\` (self-contained win-x64) |

After each meaningful build you want on the taskbar:

```powershell
.\publish.bat
```

That **deletes** `publish\`, downloads the AI models if missing, increments `build-number.txt`, publishes the version written in `version.txt` (then bumps its patch for the next publish: 1.1.0 → 1.1.1), regenerates `MiniPaint.lnk`, then by default **commits/pushes** both counters to `main` and creates a **GitHub Release** (`v<version>` + zip under `dist\`). Edit `version.txt` to start a new minor or major version; the build counter remains the 4th part of the file version.

```powershell
.\publish.bat                 # full: publish + push + Release
.\publish.bat -SkipGit        # local publish only
.\publish.bat -SkipRelease    # publish + push counter, no Release zip
```

The version appears in **Help → About**. `MiniPaint.bat` launches the published exe when present, otherwise falls back to Debug.

## Logs (crashes)

Session logs: `%LocalAppData%\MiniPaint\logs\`  
Last fatal dump: `%LocalAppData%\MiniPaint\logs\crash-last.txt`  

In the app: **Help → Open Log Folder**.

## Features

- **Documents** — multi-tab workspace; open PNG/JPEG/IFF-ILBM; native `.minipaint` ZIP projects (layers, groups, layer masks and filters preserved); New / Save / Close; drag-and-drop files onto the window to open
- **Export** — merged PNG or JPG (quality 1–100; transparency flattened onto white); IFF-ILBM when the document is in an indexed-family mode (Indexed / ECS / AGA)
- **Export All Images** — every open tab into one folder as PNG or JPG (one JPG quality for all); file names from tab names, numbered instead of overwriting unless asked
- **Layers** — add / delete / duplicate / rename, reorder, visibility, opacity, Photoshop-style blend modes, Move tool; context menu **New Image from Layer Content** (trim to visible bounds)
- **Groups** — folders of layers, nestable: **New Group** (folder button or Layer menu), **Group Layer** (Ctrl+G) puts the active layer in a new group, **Ungroup** (Ctrl+Shift+G); open / close with the arrow; ↑ / ↓ move layers in and out of open groups (a closed group is passed as one entry); with a group selected, + adds the new layer inside it; a group has its own visibility, opacity, blend mode and filters, and its content is composed on its own before blending with what is below
- **Filters** (Filter menu) — non-destructive, on the active layer or group: **Brightness/Contrast**, **Hue/Saturation** (HSV: hue shift, saturation, value) and **Color Balance** (cyan–red, magenta–green, yellow–blue for shadows / midtones / highlights, preserve luminosity); live preview while editing; each layer or group keeps a list of filters (Filters under the layers: switch off, edit ✎, remove ✕), marked fx in the layers list; every change is one undo step
- **Layer masks** — Layer menu or layers context menu: **Add Layer Mask** (shows everything, or only the selection when there is one), **Apply Layer Mask** (bakes it into the layer's transparency), **Delete Layer Mask**; a mask badge marks masked layers; masks follow Free Transform, Image Size and duplicate, and are saved in `.minipaint` projects
- **Mask Brush** (B) — paints the active layer's mask with a round brush: **Hide** (black) or **Reveal** (white), X swaps; size in pixels ([ and ]), hardness from soft to hard (Shift+[ and Shift+]), opacity (one stroke never goes past it, the next one goes further); a layer without a mask gets one on the first stroke; one undo step per stroke
- **Selection** — mask-based (any shape, soft edges) with marching ants along the outline; rectangular marquee, **Polygonal Lasso** (L: click corners; close on the first corner, by double-click or Enter; Backspace removes the last corner, Esc cancels; Shift snaps a side to 45°; optional anti-alias) and **Magic Wand** (W: tolerance, contiguous, anti-alias, sample visible image or current layer — exact palette colors in Indexed / ECS / AGA); New / Add / Subtract / Intersect from the options bar or Shift / Alt / Shift+Alt; **Select** menu: All, Deselect, Inverse (Ctrl+Shift+I), **Color Range** (pick colors from a preview, Shift / Alt to add / remove, fuzziness for soft edges, live black & white preview); move the outline (V) or lift & drag selected pixels with Move (M); copy / cut / paste as new layers across documents; selection changes are undoable
- **Object Selection** (Q) — AI selection that runs locally (MobileSAM via ONNX Runtime on the CPU, no network): click an object or drag a box around it; Shift / Alt / Shift+Alt combine; optional soft edges; the image is analyzed once in the background (~0.5 s), then each click answers in a few tens of ms
- **Content-Aware Fill** (Edit menu, Shift+Backspace) — rebuilds the selected area from its surroundings, by AI that runs locally (LaMa via ONNX Runtime on the CPU, no network), onto a **new layer** just above the active one: the original stays intact underneath, and the Mask Brush on the new layer brings parts of it back; the model sees the visible layers up to the active one; select an object with Q, then fill to erase it; the hole is grown by a few pixels and faded into the untouched image; the model loads on first use (~2.5 s), then each fill takes about 2 s; one undo step, the selection is kept; the model works at 512 px, so a large selection on a big image comes out softer than its surroundings
- **Transform Selection** (Ctrl+Alt+T) — resize/move the marquee only (Shift aspect, Alt from center)
- **Free Transform** (Ctrl+T) — scale / rotate / move a layer; with an active selection, lifts the selection content (like Move), transforms the float, then pastes back and updates the marquee; Enter apply · Esc cancel
- **Image Mode** (Ctrl+Shift+M) — live color constraint: **RGBA**, **Indexed**, **ECS** (12-bit / 4096 palette, optional Half-Brite), **AGA** (2–256 colors); quantizers Median Cut, Octree, Wu, K-Means++, NeuQuant; dither None / Floyd–Steinberg / Bayer 4×4 & 8×8 / Atkinson / Stucki / Blue Noise; Preview toggle; OK commits undoably
- **Palette editor** (Ctrl+Shift+P) — edit indexed palettes; ECS uses 0–15 nibbles with read-only EHB mirrors
- **Image Size** (Ctrl+Alt+I) / **Canvas Size** (Ctrl+Alt+C) — scale pixels (Skia) or crop/extend with 9-point anchor
- **Resize All Images** — scale every open tab by percentage, to fit within W×H (optionally never enlarging) or to an exact size, with a before → after preview; one undo step per tab
- **View** — cursor-centered zoom, pan (middle mouse / Space+drag), nearest-neighbor at high zoom, checkerboard, Fit (Ctrl+0), 100% (Ctrl+1)
- **History** — undo / redo (Ctrl+Z / Ctrl+Y), including selection changes; Ctrl+Z during a transform cancels it
- **App** — startup splash with version; About credits; session logs under `%LocalAppData%\MiniPaint\logs\` (Help → Open Log Folder); Release publish via `publish.bat` (self-contained win-x64, taskbar pin, optional GitHub Release)

## Solution layout

- `src/MiniPaint.Core` — document model (layer tree), composition, filters, selection, transform, color modes, clipboard, history, PNG / JPG / IFF / project I/O, batch resize & export
- `src/MiniPaint.Segmentation` — Object Selection: MobileSAM pre/post-processing and ONNX Runtime inference
- `src/MiniPaint.Inpainting` — Content-Aware Fill: LaMa context crop, hole mask, resize / blend weights and ONNX Runtime inference
- `src/MiniPaint.App` — Avalonia editor UI, splash, toolbox icons, dialogs
- `tests/MiniPaint.Core.Tests` — pixels, composition, layer groups, filters, transform, selection, layer masks and mask brush, color modes, serialization
- `tests/MiniPaint.Segmentation.Tests` — prompt / mask processing; end-to-end model tests when the model is downloaded
- `tests/MiniPaint.Inpainting.Tests` — fill geometry, masks and weights with a stand-in model; end-to-end LaMa tests when the model is downloaded

## Credits

Développé par Cursor, orchestré par Eric Kernin.

## License

MIT — Copyright (c) 2026 Éric Kernin / ErkMkd. See [LICENSE](LICENSE).

Object Selection uses MobileSAM (Apache-2.0) and Segment Anything (Apache-2.0), Content-Aware Fill uses LaMa (Apache-2.0), both with ONNX Runtime (MIT); see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), also shipped next to `MiniPaint.exe`.
