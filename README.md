# MiniPaint

Local Windows 2D image editor — C# / .NET 10, Avalonia UI, SkiaSharp.

## Requirements

- .NET SDK 10.x (`dotnet --list-sdks`)
- Avalonia project templates (optional, for scaffolding): `dotnet new install Avalonia.Templates`
- Avalonia + SkiaSharp restore via NuGet (no separate system installer)

## Build & run

```powershell
cd d:\MiniPaint
dotnet restore MiniPaint.slnx
dotnet build MiniPaint.slnx
dotnet test
dotnet run --project src\MiniPaint.App
# or: MiniPaint.bat
```

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

That refreshes `publish\` and regenerates `MiniPaint.lnk` (AppUserModelID `ErkMkd.MiniPaint`, embedded icon). `MiniPaint.bat` launches the published exe when present, otherwise falls back to Debug.

## Logs (crashes)

Session logs: `%LocalAppData%\MiniPaint\logs\`  
Last fatal dump: `%LocalAppData%\MiniPaint\logs\crash-last.txt`  

In the app: **Help → Open Log Folder**.

## Features

- Multi-document tabs (PNG/JPEG open; `.minipaint` project)
- Layers: add/delete/duplicate/rename, reorder, visibility, opacity, blend modes, move
- **Image → Image Size…** (Ctrl+Alt+I): scale all layer pixels (Skia Mitchell cubic / bicubic)
- **Image → Canvas Size…** (Ctrl+Alt+C): crop/extend workspace without scaling; 9-point anchor
- Cursor-centered zoom, pan (middle mouse / Space+drag), nearest-neighbor at high zoom
- Transparency checkerboard, fit-to-window, 100%
- Rectangular selection with marching ants; copy/cut/paste across documents as new layers
- Undo/redo, Ctrl+C/V/X/A/D/Z/Y
- Export merged PNG; native `.minipaint` ZIP project (layers preserved)

## Solution layout

- `src/MiniPaint.Core` — document model, composition, selection, clipboard, history, project I/O
- `src/MiniPaint.App` — Avalonia dark editor UI
- `tests/MiniPaint.Core.Tests` — pixel ops, alpha composition, coordinates, serialization
