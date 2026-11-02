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
# or: MiniPaint.bat / src\MiniPaint.App\bin\Debug\net10.0\win-x64\MiniPaint.App.exe
```

## Logs (crashes)

Session logs: `%LocalAppData%\MiniPaint\logs\`  
Last fatal dump: `%LocalAppData%\MiniPaint\logs\crash-last.txt`  

In the app: **Help → Open Log Folder**.

## Features

- Multi-document tabs (PNG/JPEG open; `.minipaint` project)
- Layers: add/delete/duplicate/rename, reorder, visibility, opacity, move
- Cursor-centered zoom, pan (middle mouse / Space+drag), nearest-neighbor at high zoom
- Transparency checkerboard, fit-to-window, 100%
- Rectangular selection with marching ants; copy/cut/paste across documents as new layers
- Undo/redo, Ctrl+C/V/X/A/D/Z/Y
- Export merged PNG; native `.minipaint` ZIP project (layers preserved)

## Solution layout

- `src/MiniPaint.Core` — document model, composition, selection, clipboard, history, project I/O
- `src/MiniPaint.App` — Avalonia dark editor UI
- `tests/MiniPaint.Core.Tests` — pixel ops, alpha composition, coordinates, serialization
