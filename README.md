# OptiSqueeze 🎬💾

OptiSqueeze is a premium Windows GUI desktop application built with Electron for reducing video file sizes by transcoding them into modern, highly efficient H.265 (HEVC) formats. It detects your GPU capabilities dynamically, running hardware-accelerated encodings whenever possible, and features a state-of-the-art glassmorphic user interface.

---

## Key Features

- **Drag & Drop Workspace**: Drop folders or multiple individual files to immediately queue them up.
- **Dynamic Hardware Acceleration**: Automatically detects and leverages:
  - **NVIDIA NVENC** H.265 (Fastest GPU Transcoding)
  - **Intel QuickSync (QSV)** H.265
  - **AMD AMF** H.265
  - **CPU libx265** (High-efficiency software fallback)
- **Live Encoding Feedback**: Real-time progress bars, transcoding speeds (e.g. `3.5x`), frame rates (FPS), and estimated time remaining (ETA).
- **Dashboard & Analytics**: Track how much total disk space you've reclaimed, files processed, and your average compression ratio.
- **Flexible Settings**:
  - Constant Rate Factor (CRF) quality slider (values 18 to 32).
  - CPU Preset speed selection.
  - Audio handling options: Copy streams natively (0% loss) or re-encode to compact AAC stereo.
  - Choose to overwrite original files or append a `_compressed` suffix.
- **Smart Optimization Filters**:
  - Skip files that are already compressed with modern formats (`HEVC/H.265`, `AV1`, `VP9`).
  - Skip previously successfully compressed files based on your history database.
  - Skips replacement if the compressed output file is larger than the original (retaining original quality).
- **Embedded Logging Console**: Collapsible raw terminal log feed straight from the active FFmpeg thread.
- **Built-in Auto-Installer**: The application automatically checks for local FFmpeg binaries and can download/extract them with a single click.

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
│   ├── main.js                # Electron main thread
│   ├── preload.js             # IPC security bridge
│   ├── renderer.js            # Frontend logic
│   ├── index.html             # UI Structure
│   └── style.css              # Glassmorphic UI styles
├── .gitignore
├── compress_history.json      # Cache of successfully encoded files
└── package.json               # Build configurations & dependencies
```

---

## Installation & Setup

Ensure you have [Node.js (v18+)](https://nodejs.org) installed on your system.

1. **Clone the repository**:
   ```bash
   git clone https://github.com/yourusername/optisqueeze.git
   cd optisqueeze
   ```

2. **Install project dependencies**:
   ```bash
   npm install
   ```

3. **FFmpeg Binary Configuration**:
   When launching the app, it will look for `ffmpeg.exe` and `ffprobe.exe` inside the `/bin` directory. If they are missing:
   - Click the **Download & Setup Automatically** button in the startup screen to download them.
   - Alternatively, copy your existing files `ffmpeg.exe` and `ffprobe.exe` and paste them into `/bin`.

---

## Running the Application

To run the application in development mode:
```bash
npm start
```

---

## Packaging into a Portable Executable

To bundle the application into a standalone Windows `.exe` installer or portable executable (managed inside the `dist/` directory):
```bash
npm run build
```

This uses `electron-builder` under the hood, matching the production configs defined in `package.json`.

---

## License

This project is open-source and licensed under the [MIT License](LICENSE).
