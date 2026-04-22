# StreamPlayer

A WPF (.NET 9) media player. Paste any URL supported by yt-dlp, hit Play — video or audio streams inside the app window with full transport controls, playlist support, and track identification.

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | 9.0+ | |
| [yt-dlp](https://github.com/yt-dlp/yt-dlp/releases) | latest | Must be on `PATH` |
| [Node.js](https://nodejs.org/) | any LTS | Required by yt-dlp's JS challenge solver |

yt-dlp handles stream resolution and metadata. The bundled VLC lua scripts are outdated for modern sites — yt-dlp is the reliable path.

## Build & Run

```bash
dotnet build
dotnet run --project StreamPlayer/StreamPlayer
```

## Features

- **Any yt-dlp supported URL** — YouTube, Vimeo, SoundCloud, Twitch, and thousands of other sites
- **YouTube playlist support** — lazy enumeration via `--flat-playlist`; auto-advances at end of each track; playlist popup with Prev/Next navigation and jump-to-entry
- **Thumbnail display** — shown in the video panel while buffering or stopped, replaced by live video once playing
- **Metadata display** — title, channel, thumbnail, and duration appear before VLC begins rendering
- **Embedded VLC player** — video renders inside the app window via LibVLCSharp.WPF
- **Transport controls** — ⏮ rewind 10 s · ▶/⏸ pause-resume · ⏹ stop · ⏭ fast-forward 10 s
- **Seek bar** — drag to seek; chapter tick marks shown for videos that have chapters
- **Chapter tracking** — current chapter name displayed live next to the time counter
- **Volume control** — slider + mute toggle; setting persisted across launches
- **History** — last 20 played URLs; click to reload, ✕ to remove individual entries
- **ACRCloud track identification** — captures 8 s of system audio and identifies the playing track
- **Settings** — video quality (Low 480p / High 1080p) and audio-only mode toggles via ⚙ button; takes effect on next load
- **Stream quality display** — actual codec, resolution, fps, and audio bitrate shown in the info bar after load (e.g. `480p · VP9 · 30 fps | 128 kbps · Opus · 48 kHz`)
- **Power saving friendly** — releases display and system wake locks when not playing
- **Buffering feedback** — status bar shows buffering percentage during network hiccups

## Layout

```
┌──────────────────────────────────────────────────────────────┐
│  [⏱ History ▾]  URL: [______________________________] [Play] │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│               Video / Thumbnail                              │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│  [thumbnail]  Title                       Channel · dur      │
│               480p · VP9 · 30 fps | 128 kbps · Opus · 48 kHz│
├──────────────────────────────────────────────────────────────┤
│  ⏮ ▶/⏸ ⏹ ⏭  [══|══●══|══]  § Chapter  0:12/10:23  🔊 🎵 ☰ ⚙ │
└──────────────────────────────────────────────────────────────┘
```

## NuGet packages

| Package | Version | Purpose |
|---|---|---|
| `Prism.DryIoc` | 9.* | MVVM framework + DI |
| `LibVLCSharp.WPF` | 3.9.6 | WPF VideoView control |
| `VideoLAN.LibVLC.Windows` | 3.0.23 | Native libvlc binaries |
| `NAudio` | 2.2.1 | WASAPI loopback capture for ACRCloud |

## Notes

- **yt-dlp must be on PATH** — install via `winget install yt-dlp.yt-dlp` or from the [releases page](https://github.com/yt-dlp/yt-dlp/releases)
- **DASH streams** — video and audio are served as separate tracks; yt-dlp resolves both and VLC stitches them via `:input-slave`
- **Cookies** — if YouTube bot-detection triggers, export `cookies.txt` (via "Get cookies.txt LOCALLY" browser extension) to `%APPDATA%\StreamPlayer\cookies.txt`
- **ACRCloud** — requires a free account at [acrcloud.com](https://www.acrcloud.com/); create an *Audio & Video Recognition* project to obtain credentials
- **Bandwidth** — measured at typical YouTube streams: audio-only ≈ 0.3 Mbps · low quality (480p) ≈ 1.0 Mbps · high quality (1080p) ≈ 2.8 Mbps
