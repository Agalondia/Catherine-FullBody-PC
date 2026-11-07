# Catherine: Full Body — PC Installer & Launcher

<img width="889" height="532" alt="image" src="https://github.com/user-attachments/assets/36986491-47d3-4e84-a93a-aedfb5265ef0" />

A standalone, all-in-one Windows installer and launcher wrapper that turns your legally dumped PS4 `.pkg` files of **Catherine: Full Body** (`CUSA14836`) into a seamless, native-feeling PC installation.

[**Video Showcase**](https://youtu.be/B8CBTJt_KrA?si=XY2VCjJizXVeSSIO)

## ✨ Features
* **True All-in-One Setup:** Automatically unpacks your Base Game `.pkg`, merges the Update Patch `.pkg`, and batch-installs all DLC `.pkg` files into a clean `Data\` directory.
* **Built-In Keybind Menu:** Customize all keyboard bindings (including primary keys and secondary Numpad slots) directly inside the launcher.
* **Zero Emulator Clutter:** Boots directly into borderless fullscreen with AMD FSR + RCAS sharpening enabled and all debug console windows suppressed.
* **Built-In Fast Boot Toggle:** Includes a 1-click toggle to skip the startup company logos and jump straight to the title screen.
* **Direct-Boot Mode:** Check *"Launch directly next time"* to bypass the launcher completely on future boots.

## 🚀 How to Install & Play
1. Download **`Catherine.exe`** from the [**Releases**](../../releases) page.
2. Run `Catherine.exe` and choose your installation folder (defaults to `C:\Games\Catherine Full Body`).
3. Select your dumped **Base Game PKG**, optional **Update Patch PKG**, and optional **DLC Folder**.
4. Click **Start Installation** (takes ~3–5 minutes depending on your SSD speed).
5. Use the newly created **Catherine - Full Body** desktop shortcut to play!

---

<img width="889" height="532" alt="image" src="https://github.com/user-attachments/assets/ae50f815-d9df-4ccc-a588-a2871d15d434" />


## 🎮 Controls (Controller & Keyboard)

### 1. Controller (Recommended — Plug & Play)
**Xbox, PlayStation (DualShock 4 / DualSense), and Switch Pro controllers** are detected automatically out of the box—just plug in or connect via Bluetooth before launching.

### 2. Keyboard Controls (Customizable in Launcher)
You can rebind any key directly inside the launcher by clicking **⌨ Customize Keyboard Controls**, or use the default layout below:

| PlayStation Input | Main Keyboard Key | Alt / Numpad Key | In-Game Action |
| :--- | :--- | :--- | :--- |
| **Left Stick / D-Pad** | **`W` `A` `S` `D`** | **Arrow Keys** | Move Vincent / Navigate Menus |
| **Cross (`✕`)** | **`N`** | **`NUM 2`** | Confirm / Climb / Select |
| **Circle (`○`)** | **`B`** | **`NUM 6`** | Cancel / Back |
| **Square (`□`)** | **`V`** | **`NUM 4`** | **Grab / Push / Pull Blocks** |
| **Triangle (`△`)** | **`C`** | **`NUM 8`** | Use Item / Open Cell Phone |
| **L1** | **`Q`** | — | **Undo Last Move (Rewind)** |
| **R1** | **`U`** | — | Toggle Camera / Auto-Climb |
| **L2 / R2** | **`E`** / **`O`** | — | Look / Pan Puzzle Camera |
| **Options (Start)** | **`Enter`** | — | Pause Menu / Skip Cutscenes |
| **Touchpad** | **`Spacebar`** | — | Touchpad Button |
| **Exit Game** | **`Alt + F4`** | — | Safely Close Game to Desktop |

---

## 💡 Useful Tips
* **Reopening the Launcher:** If you enabled *"Launch directly next time"* and want to open the launcher settings again, **hold `Shift`** while double-clicking your desktop shortcut.
* **Exiting the Game:** Because console games do not have an "Exit to Desktop" menu button, press **`Alt + F4`** on your keyboard to safely close the game.
* **Regarding the 30 FPS Lock:** The game engine's puzzle physics are natively tied to a 30 Hz tick rate. For smooth 60/120 FPS without breaking block physics, use [**Lossless Scaling (LSFG)**](https://store.steampowered.com/app/993090/Lossless_Scaling) or driver-level [**AMD AFMF**](https://www.youtube.com/watch?v=s_vUwosUAAs).

## 🙏 Credits & Disclaimer
* Powered by [**shadPS4**](https://github.com/shadps4-emu/shadPS4).
* PKGs unpacked with [**shadPS4Plus**](https://github.com/AzaharPlus/shadPS4Plus).
* **Legal Notice:** This tool contains **zero** copyrighted Atlus/Sega game assets, ROMs, or proprietary Sony keys. You must provide your own legally dumped PlayStation 4 `.pkg` files to use this software.

## 🛠️ Building from Source
If compiling from source code using `.NET 8 SDK`:
1. Place a 7-second MP4 named `intro.mp4` in a folder named `Launcher/`.
2. Compress your configured shadPS4 folder into `Engine.zip` inside the `Launcher/` folder.
3. Run `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true`.

## ⚠️ Security Notice

[**VirusTotal Scan Result**](https://www.virustotal.com/gui/file/255c0e961cb68b54899039793374659df07ffde1cd7025478bd85bd58b1128d6)

**Regarding Bkav Pro's Analysis:** `Catherine.exe` is compiled as a self-contained single-file executable that carries the emulator inside its code. Overly aggressive machine-learning scanners (like Bkav) see an `.exe` containing an embedded archive and flag it as a generic heuristic false positive.

This installer/launcher will **NEVER** ask you to disable Windows Defender, add an antivirus exclusion folder, or paste a command into PowerShell. If anyone in a comment, Discord DM, or GitHub issue tells you to run an `irm | iex` command for a "fix," it is malware.
