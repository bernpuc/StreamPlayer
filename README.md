# StreamPlayer

A WPF (.NET 9) YouTube player. Paste a YouTube URL, hit Play — video streams inside the app window with full transport controls and video metadata display.

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | 9.0+ | |
| [yt-dlp](https://github.com/yt-dlp/yt-dlp/releases) | latest | Must be on `PATH` |

yt-dlp handles YouTube stream resolution. The bundled VLC lua scripts inside `VideoLAN.LibVLC.Windows` are outdated for YouTube — yt-dlp is the reliable path.

## Build & Run

```bash
dotnet build
dotnet run --project StreamPlayer/StreamPlayer
```

## Features

- **YouTube URL validation** — accepts `youtube.com`, `www.youtube.com`, `youtu.be`, `m.youtube.com`; rejects everything else with an inline message
- **Metadata display** — title, channel, thumbnail (poster image), and duration appear as soon as yt-dlp resolves the URL, before VLC begins rendering
- **Embedded VLC player** — video renders inside the app window via LibVLCSharp.WPF
- **Transport controls** — ⏮ rewind 10 s · ▶/⏸ pause-resume · ⏹ stop · ⏭ fast-forward 10 s
- **Seek bar** — drag to seek; chapter tick marks shown for videos that have chapters
- **Chapter tracking** — current chapter name displayed live next to the time counter
- **Buffering feedback** — status bar shows buffering percentage during network hiccups

## Layout

```
┌─────────────────────────────────────────────────────┐
│  URL: [________________________________]  [▶ Play]  │
├─────────────────────────────────────────────────────┤
│                                                     │
│                    Video                            │
│                                                     │
├──────────────────────────────────────────────────── ┤
│  [thumbnail]  Title                  Channel · dur  │
├─────────────────────────────────────────────────────┤
│  ⏮ ▶/⏸ ⏹ ⏭  [═══|══●═══|══]  § Chapter  0:12/10:23 │
└─────────────────────────────────────────────────────┘
```

## NuGet packages

| Package | Version | Purpose |
|---|---|---|
| `Prism.DryIoc` | 9.0.537 | MVVM framework + DI |
| `LibVLCSharp.WPF` | 3.9.6 | WPF VideoView control |
| `VideoLAN.LibVLC.Windows` | 3.0.23 | Native libvlc binaries |

## Known limitations

- **YouTube only** — the URL validator rejects non-YouTube links by design
- **yt-dlp must be on PATH** — the app does not bundle it; install via `winget install yt-dlp.yt-dlp` or from the [releases page](https://github.com/yt-dlp/yt-dlp/releases)
- **DASH streams** — YouTube serves video and audio as separate tracks; yt-dlp resolves both and VLC stitches them together via `:input-slave`
- **Playlist URLs** — `&list=...` parameters are ignored (`--no-playlist`); only the individual video plays
