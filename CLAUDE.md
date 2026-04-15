# StreamPlayer — Developer Notes

## Stack

- **WPF / .NET 9** (`net9.0-windows`)
- **Prism 9 + DryIoc** — MVVM framework and DI container
- **LibVLCSharp.WPF 3.9.6** — embedded VLC video control
- **VideoLAN.LibVLC.Windows 3.0.23** — native libvlc binaries (auto-copied to output)
- **yt-dlp** (external CLI, must be on PATH) — stream URL + metadata resolution

## Project layout

```
StreamPlayer/
  StreamPlayer.slnx
  StreamPlayer/
    App.xaml / App.xaml.cs          Prism bootstrap, global converter resources
    MainWindow.xaml                 Single window; 4-row layout
    MainWindow.xaml.cs              Minimal code-behind: MediaPlayer wiring, seek drag, thumbnail/ticks
    Models/
      VideoInfo.cs                  VideoInfo + ChapterInfo records (immutable)
    Converters/
      MsToTimeConverter.cs          long ms → "m:ss" / "h:mm:ss" for XAML bindings
    Services/
      Interfaces/
        IPlayerService.cs           MediaPlayer property + PlayAsync(url) → VideoInfo
      PlayerService.cs              yt-dlp invocation, JSON parsing, LibVLC playback
    ViewModels/
      MainWindowViewModel.cs        All UI state, commands, VLC event subscriptions
```

## Architecture

**DI wiring** (`App.xaml.cs`):
- `IPlayerService` → `PlayerService` registered as singleton
- `MainWindow` resolved by Prism's `CreateShell()`; DryIoc injects `MainWindowViewModel` automatically (concrete type, resolvable deps)

**Data flow for playback:**
1. User enters URL → `Url` setter → `ValidateUrl()` → enables/disables `PlayCommand`
2. `PlayCommand` → `ExecutePlayAsync()` → `IPlayerService.PlayAsync(url)`
3. `PlayerService` runs one `yt-dlp --dump-json` call, parses JSON for both stream URLs and metadata
4. `Media` is created from the video URL; audio URL added via `:input-slave` option if DASH
5. `MediaPlayer.Play(media)` returns immediately; VLC fires `Playing` event asynchronously
6. `PlayAsync` returns `VideoInfo` → ViewModel stores it, seeds `_duration`, notifies XAML
7. Code-behind `PropertyChanged` handler sets `ThumbnailImage.Source` and `SeekSlider.Ticks`

## Critical: VLC event threading

All `MediaPlayer` events (`Playing`, `Paused`, `TimeChanged`, etc.) fire on **VLC's internal thread**, not the UI thread. Every handler must marshal to the dispatcher. Use `BeginInvoke` (async), **never** `Invoke` (sync) — sync dispatch can deadlock when VLC fires events during a seek operation.

```csharp
// Correct
private static void Dispatch(Action a) => Application.Current.Dispatcher.BeginInvoke(a);

// Wrong — will deadlock on seek/FF/RW
Application.Current.Dispatcher.Invoke(a);
```

## yt-dlp integration

Single call: `yt-dlp -f "bestvideo[height<=1080]+bestaudio/best" --dump-json --no-playlist "{url}"`

Stream URLs are extracted from the JSON response:
- `requested_formats[].url` — present when format uses `+` (DASH: two separate streams)
- `url` — top-level, present for single progressive streams

When two URLs are returned, the second is passed to VLC as `:input-slave={audioUrl}` so VLC combines them.

**Metadata fields used:** `title`, `channel` (fallback `uploader`), `thumbnail`, `duration` (seconds → ms), `chapters[].{title, start_time, end_time}`.

## Seek bar / chapter ticks

The WPF `Slider` (`SeekSlider`) uses the native `Ticks` / `TickPlacement` properties for chapter marks. Tick values are `ChapterStartMs / TotalDurationMs` (range 0.0–1.0, matching `Minimum=0 Maximum=1`). Set from code-behind in `ApplyVideoInfo()` when `VideoInfo` changes.

Drag-to-seek suppresses `TimeChanged` updates via `_isSeeking` flag:
- `Thumb.DragStartedEvent` → `OnSeekStarted()` sets flag
- `Thumb.DragCompletedEvent` → `OnSeekCompleted(value)` clears flag, sets `MediaPlayer.Time`

## WPF airspace limitation

`LibVLCSharp.WPF.VideoView` wraps a `WindowsFormsHost` internally. `WindowsFormsHost` always renders above WPF content — you cannot overlay WPF elements (e.g. a thumbnail `Image`) on top of it. The info bar (Row 2, below the video) avoids this by placing the thumbnail outside the video area.

## Adding new features

**New command:** add `DelegateCommand` property in `MainWindowViewModel`, implement execute/canExecute, bind in XAML. Follow the existing `RewindCommand` / `FastForwardCommand` pattern.

**New metadata field:** add a property to `VideoInfo` record (`Models/VideoInfo.cs`), extract from the yt-dlp JSON in `PlayerService.ParseYtDlpJson`, expose in ViewModel, bind in XAML.

**New service:** define interface in `Services/Interfaces/`, implement in `Services/`, register in `App.xaml.cs` `RegisterTypes`.

## Build

```bash
dotnet build                  # debug
dotnet build -c Release       # release
dotnet run --project StreamPlayer/StreamPlayer
```
