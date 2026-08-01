# OptiSqueeze 🎬💾

OptiSqueeze is an ultra-lightweight (135 KB) native Windows desktop application built with C# and Windows Presentation Foundation (WPF) for reducing video file sizes by transcoding them into modern, highly efficient H.265 (HEVC) formats. 

By running on native Windows frameworks, it eliminates heavy browser runtimes (like Electron) and startup DLL errors. It starts up instantly, uses minimal memory, and has zero external dependencies.

---

## Key Features

- **Lightweight Standalone Binary**: The compiled app is a single, portable executable (`OptiSqueeze.exe`) of just **135 KB**.
- **Drag & Drop Workspace**: Drop folders or multiple individual files to immediately queue them up.
- **Dynamic Hardware Acceleration**: Automatically detects and leverages:
  - **NVIDIA NVENC** H.265 (Fastest GPU Transcoding)
  - **Intel QuickSync (QSV)** H.265
  - **AMD AMF** H.265
  - **CPU libx265** (High-efficiency software fallback)
- **Live Encoding Feedback**: Real-time progress percentage, transcoding speed multipliers (e.g., `4.5x`), frame rates (FPS), and estimated time remaining (ETA).
- **Dashboard Analytics**: Track how many total files you've processed, total space saved, and your average size reduction ratio.
- **Background Minimization**: 
  - If you close the window while transcoding, OptiSqueeze asks to minimize to the Windows System Tray to finish in the background.
  - Shows a balloon notification when tasks complete in the background.
  - Double-click the tray icon to restore the window.
- **Smart Skip Filters**:
  - Skip files that are already compressed with modern formats (`HEVC/H.265`, `AV1`, `VP9`).
  - Skip previously successfully compressed files based on your history database.
  - Skips replacement if the compressed output file is larger than the original.
- **Embedded Logging Console**: Collapsible log drawer showing raw FFmpeg stdout/stderr diagnostics.
- **Hidden Database**: Saves successful transcodes in a hidden, extensionless history file named `.compress_history` (using Windows hidden attributes).
- **Auto-Installer**: Natively checks for local FFmpeg binaries and downloads/extracts them with a single click if they are missing.

---

## Project Structure

```text
video-transcoder-gui/
├── bin/                       # Holds local FFmpeg binaries (Gitignored)
│   ├── ffmpeg.exe
│   └── ffprobe.exe
├── cli/                       # Backup CLI scripts
│   ├── codec_from_h264_to_h265.ps1
│   └── codec_from_h264_to_h265.bat
├── src/                       # Application code
│   ├── OptiSqueeze.cs         # Main C# WPF code (UI + logic)
│   ├── icon.ico               # Windows application icon
│   └── icon.png               # Custom icon PNG
├── .gitignore
├── .compress_history          # Hidden extensionless history (JSON logs)
├── build.bat                  # Compiler script invoking csc.exe
└── OptiSqueeze.exe            # Lightweight compiled application (135 KB)
```

## How to Run & Smart App Control

If your machine has **Windows 11 Smart App Control (SAC)** enabled, running a locally compiled, unsigned `.exe` (like `OptiSqueeze.exe`) might be blocked. 

To run the application safely and universally on any Windows machine:
- **Double-click `run.bat`** (or execute `run.ps1` in PowerShell).
This launcher loads the C# source code (`src/OptiSqueeze.cs`) and compiles it **directly in-memory** using your trusted PowerShell host. Because it executes within the context of a signed host (`powershell.exe`), it runs instantly and bypasses Smart App Control restrictions without changing any system policies or signing certificates.

### Rebuilding manually (Optional)
If you wish to compile it into a standalone executable file:
1. Double-click `build.bat` or run it in your terminal:
   ```cmd
   build.bat
   ```
This utilizes Windows' built-in C# compiler (`csc.exe`) located in `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. No .NET SDKs or configuration setups are required.

---

## Setup & Binaries

When launching the app, it will look for `ffmpeg.exe` and `ffprobe.exe` inside the `/bin` directory. If they are missing:
- Click the **Download & Setup Automatically** button in the startup screen to download them.
- Alternatively, copy your existing files `ffmpeg.exe` and `ffprobe.exe` and paste them into `/bin`.

---

## License

This project is licensed under the [MIT License](LICENSE).
