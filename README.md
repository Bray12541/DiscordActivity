# Discord Activity

A Windows tray app that detects the foreground process and publishes a matching
Discord Rich Presence through the local Discord desktop client's IPC connection.

## Setup

1. Create an application at <https://discord.com/developers/applications>.
2. Copy its Application ID from **General Information**.
3. Run `DiscordActivity.exe`, paste the ID, edit mappings, and click **Save**.
4. Keep the Discord desktop app running with activity sharing enabled.

This build includes mappings detected from the installed Steam, Epic, Xbox,
Microsoft Store, and EA game libraries on this PC. The optional **Window title
contains** field disambiguates shared process names such as Minecraft's `javaw`.

## Features

- Automatically rescans local Steam, Epic, Xbox, EA, and registered game installs
- Captures a running game with **Add Current Game**
- Records separate active and process-running time, session count, and last-played time
- Assigns public Steam cover-art URLs automatically
- Supports manual Discord Developer Portal image keys and external artwork URLs
- Never publishes or counts processes listed under **Privacy exclusions**
- Pauses publishing instantly from the tray
- Clears presence after configurable keyboard/mouse idle time
- Detects configured games while they are running in the background
- Lets each mapping continue in the background or opt out of statistics
- Shows the current game and session timer in the tray menu
- Reads Discord RPC replies and reports rejected activity fields
- Exports and restores configuration and statistics in one JSON backup
- Removes missing auto-discovered games during rescans
- Previews external artwork in the mapping editor
- Includes dark mode and automatic update checks
- Updates directly from the latest public GitHub Release
- Writes rolling crash/error logs and reports recovery after an unexpected exit

Optional artwork can also be uploaded in the application's Rich Presence assets.
Enter the asset's key in a mapping's **Manual image key** cell; it takes priority
over automatically discovered artwork.

Configuration and statistics are stored under `%APPDATA%\DiscordActivity`.

## Automatic updates

Enter a public repository as `owner/repository` under **Backup & updates**.
Releases must include `DiscordActivity.exe` and `DiscordActivity.sha256`. The
included `github-release-workflow.yml` builds and publishes both whenever a `v*`
tag is pushed. See `GITHUB_RELEASES.md` for setup instructions.

A custom HTTPS manifest remains available as a fallback. Its schema is shown in
`update-manifest.example.json`. The app refuses unverified downloads and asks
before installation.

## Crash logs

Daily logs are stored in `%APPDATA%\DiscordActivity\Logs` and retained for 30
days. The app records startup, normal shutdown, update errors, scanner failures,
Discord RPC failures, unobserved background-task errors, and unhandled crashes.
After a crash, the next launch offers to open the relevant log folder.

## Build

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```
