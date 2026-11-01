# Lyricod for Windows

## English | [简体中文](README.zh-CN.md)

Lyricod displays synced lyrics on Windows, with word-by-word highlighting, desktop lyrics, a lyric island at the top of the screen, and a separate lyrics window.

It works with Spotify, QQ Music, NetEase Cloud Music, Apple Music, and supported browser music players. Translations, pronunciation, timing, and appearance can be customized.

[Download Lyricod](https://github.com/Indarkdust/Lyricod-App/releases)

![Lyricod Glass lyric island showing synchronized lyrics](assets/island-glass.gif)

## Ways to view lyrics

### Lyricod Island

The island sits at the top of the screen by default and highlights lyrics word by word as the song plays. It adjusts its width to the current line and can show translations, pronunciation, or the next line. Lyric colours can follow the album artwork or use a colour you choose.

![Lyricod Island highlighting lyrics as music plays](assets/island.gif)

Hovering over the island can expand it into a mini player or hide it temporarily so you can use what's behind it. You can also combine both actions: the island hides when the pointer moves onto it. To expand the mini player, move across the island horizontally, then down (or up, for a floating island). The island can fade out after playback pauses.

![Expanding the lyric island into a mini player](assets/island-gesture.gif)

<details>
<summary>How to expand the mini player with the mouse</summary>

![Move sideways across the island, then move down](assets/island-gesture-guide.png)

</details>

To move the island somewhere else, switch to Floating mode in Settings > Lyricod Island. The floating island has rounded corners on all sides and can be dragged anywhere once unlocked. Near screen edges, it expands inward to keep the lyrics and mini player on screen.

Lyricod Glass gives the island a transparent background. On Windows 11, the default Liquid effect adds a slight blur, edge refraction and highlights. You can also choose Clear or Frosted, and adjust transparency, tint, blur and refraction.

On supported Windows 11 versions, the live Liquid Glass island appears in screenshots and recordings, including NVIDIA Instant Replay. On older versions, the island may be hidden from capture to prevent visual feedback. You can temporarily show it using **Show in screenshots**.

### Desktop lyrics

Display one or two lines above other windows. When locked, clicks pass through the lyrics. Unlock them to drag the lyrics to a different position.

![Desktop lyrics with word-by-word highlighting](assets/desktop-lyrics.gif)

### Lyrics window

View the whole song in a separate window, with optional album artwork in the background. Lyrics scroll with playback, and clicking a line seeks to that point in the song. Press F11 for full screen.

![Separate lyrics window with album art and synchronized lines](assets/lyrics-window.png)

Lyricod also runs in the system tray. Open its tray panel to control playback, toggle lyric views, choose lyrics, or adjust the current song's timing. Global shortcuts are available for common actions.

<img src="assets/tray-panel.en.png" alt="Lyricod tray panel with playback controls and lyric settings" width="360">

## Supported players

Lyricod reads playback information through Windows media controls. If a player appears there, Lyricod can usually follow it. This includes Spotify, QQ Music, NetEase Cloud Music, Apple Music, and some browser music players.

When multiple players are open, Lyricod follows the one currently playing by default. You can also select a player in Settings > General.

Some players do not report an accurate playback position. In that case, Lyricod keeps time itself, which may cause lyrics to drift after pausing or seeking. See the NetEase Cloud Music section below for details.

For videos in browsers, Lyricod only displays lyrics when it can match the content to a song. You can disable lyric searches for browsers and other apps in Settings > Lyrics.

## Lyrics sources

Lyricod searches NetEase Cloud Music, QQ Music, and [LRCLIB](https://lrclib.net). Depending on the song and source, results may include word-by-word or line-synced lyrics, translations, and pronunciation. Lyrics are cached on your PC for later playback.

If Lyricod finds the wrong song, open Choose Lyrics to compare search results and preview the lyrics before selecting one. Your choice and any timing adjustment are saved for that song, even if you clear the lyrics cache.

![Choose Lyrics dialog comparing search results and previewing lyrics](assets/choose-lyrics.en.png)

You can adjust lyric timing from the tray panel or Settings > Lyrics.

## Download and install

Download the version for your PC from [Releases](https://github.com/Indarkdust/Lyricod-App/releases):

- **x64**: for most Windows PCs with Intel or AMD processors.
- **ARM64**: for Windows PCs with Arm processors.
- **Installer (setup.exe)**: installs for the current user without administrator access.
- **Portable (zip)**: extract the archive and run `Lyricod.exe`. Keep the `app` folder next to it.

Lyricod runs in the Windows system tray after launch. If no window appears, look for its icon in the notification area.

The app is currently unsigned, so Windows may display a warning the first time it runs. Check where you downloaded the file before deciding whether to proceed.

Lyricod checks for updates once a day. You can disable this in Settings > About. Both installed and portable versions support in-app updates, or you can download a new version manually.

Settings and cached lyrics are stored in `%LOCALAPPDATA%\Lyricod` and are kept during updates. Uninstalling the app does not remove this folder; delete it manually if you want to remove the data as well.

## NetEase Cloud Music lyrics out of sync?

NetEase Cloud Music generally reports which song is playing but does not provide Windows with its playback position. Lyricod therefore tracks time itself, so lyrics may drift after you pause or seek.

For more accurate synchronization, you can use **InfLink-rs**, a third-party plugin that runs through **BetterNCM**. Both are maintained by other developers and change how NetEase Cloud Music runs. Install them only if you are comfortable with that.

The easiest way is through Lyricod: open Settings > General > NetEase Cloud Music sync. Lyricod downloads [a small helper](https://github.com/darkdust-c/Lyricod-CM-Helper) to install BetterNCM and InfLink-rs, and verifies the downloaded files before installation. This option supports 64-bit NetEase Cloud Music 3.x. If you'd rather install them manually:

1. Quit NetEase Cloud Music. Download and run `betterncm_installer.exe` from [BetterNCM Installer releases](https://github.com/std-microblock/BetterNCM-Installer/releases/latest).
2. Open NetEase Cloud Music and install **InfLink-rs** from the BetterNCM plugin store. Alternatively, download `InfLink-rs.plugin` from [InfLink-rs releases](https://github.com/apoint123/inflink-rs/releases/latest) and copy it to `C:\betterncm\plugins`.
3. Restart NetEase Cloud Music. InfLink-rs enables **SMTC** by default. If Lyricod still can't detect playback progress, check the InfLink-rs settings.

Lyricod will detect the additional playback information automatically. You can check this in Settings > About > Diagnostics.

InfLink-rs has been tested with NetEase Cloud Music 3.1.20 through 3.1.41 and 2.10.13. If a player update causes problems, check for an updated plugin. BetterNCM can be removed by deleting `msimg32.dll` from the NetEase Cloud Music installation folder, or use Remove in Lyricod's Settings > General > NetEase Cloud Music sync if you installed it through Lyricod. Do not install the older InfLink alongside InfLink-rs.

## Customization

You can customize lyric fonts, sizes, colours, backgrounds, translations, pronunciation, island mode and theme, animations, hover behaviour, and global shortcuts. The island, desktop lyrics and lyrics window have separate display settings.

Lyrics use Microsoft YaHei by default. You can choose another installed font or download **MiSans** by Xiaomi from Settings. MiSans is not bundled with Lyricod and is subject to Xiaomi's licence.

## Privacy

To find lyrics, Lyricod sends the song title, artist, and duration to NetEase Cloud Music, QQ Music, and LRCLIB. If lyric lookups for browsers and other apps are enabled, the title and artist or channel of content playing there may also be used in searches. You can turn this off in Settings > Lyrics. No additional information about your browsing or viewing is sent.

The island's audio bars use Windows audio APIs to read audio information from the current player. Lyricod does not record, save, or upload audio. You can disable music-reactive bars in Settings > Lyricod Island.

Automatic update checks contact GitHub once a day and can be disabled in Settings > About.

## Requirements

- Windows 10 version 1809 or newer, x64 or ARM64.
- Music-reactive audio bars require Windows 11. On Windows 10, they use a preset animation.

## Credits

Inspired by [Lyricify](https://github.com/WXRIW/Lyricify-App). Lyricod Island takes inspiration from WXRIW's Dynamic Lyrics Island design (CC BY-SA 4.0). QQ Music word-by-word lyric decryption code was ported from [Lyricify Lyrics Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper) (Apache License 2.0). Pinyin support uses [ToolGood.Words](https://github.com/toolgood/ToolGood.Words) (Apache License 2.0), and tray and window helpers use [WinUIEx](https://github.com/dotMorten/WinUIEx) (MIT). Lyricod Glass's liquid effect is drawn with [Win2D](https://github.com/microsoft/Win2D) by Microsoft.

Screenshots and demo GIFs may contain song lyrics, album artwork, and other third-party content for demonstration purposes. All such content belongs to its respective rights holders and is not covered by Lyricod's Apache License 2.0.

Lyrics belong to their respective copyright holders. Lyricod is not affiliated with Microsoft, NetEase, or Tencent.

## License

Lyricod for Windows is licensed under the [Apache License 2.0](LICENSE). Third-party code, assets, and licences are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
