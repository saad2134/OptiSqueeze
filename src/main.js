const { app, BrowserWindow, ipcMain, dialog, shell } = require('electron');
const path = require('path');
const fs = require('fs');
const { spawn, exec } = require('child_process');
const https = require('https');
const admZip = require('adm-zip'); // Note: we'll add adm-zip or use PowerShell on Windows to extract.
// Since we are on Windows, we can use built-in Expand-Archive via PowerShell, avoiding extra node dependency issues!

let mainWindow;
let activeFFmpegProcess = null;
let isCancelled = false;

// Paths
const appDir = app.isPackaged ? path.dirname(app.getPath('exe')) : app.getAppPath();
const binDir = path.join(appDir, 'bin');
const ffmpegPath = path.join(binDir, 'ffmpeg.exe');
const ffprobePath = path.join(binDir, 'ffprobe.exe');
const historyPath = path.join(appDir, 'compress_history.json');

// Ensure bin folder exists
if (!fs.existsSync(binDir)) {
  fs.mkdirSync(binDir, { recursive: true });
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1100,
    height: 800,
    minWidth: 850,
    minHeight: 650,
    title: "OptiSqueeze - Video Compressor & Encoder",
    frame: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  mainWindow.loadFile(path.join(__dirname, 'index.html'));
  
  // Open DevTools in dev mode if needed
  // mainWindow.webContents.openDevTools();
}

app.whenReady().then(() => {
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    // Kill any active transcoder process on close
    if (activeFFmpegProcess) {
      activeFFmpegProcess.kill('SIGINT');
    }
    app.quit();
  }
});

// Helper: Run command and return output
function runCommand(command, args = []) {
  return new Promise((resolve, reject) => {
    const proc = spawn(command, args);
    let stdout = '';
    let stderr = '';
    proc.stdout.on('data', (data) => stdout += data.toString());
    proc.stderr.on('data', (data) => stderr += data.toString());
    proc.on('close', (code) => {
      if (code === 0) resolve(stdout || stderr);
      else reject(new Error(stderr || stdout || `Process exited with code ${code}`));
    });
  });
}

// 1. Check & Download Binaries
ipcMain.handle('check-binaries', () => {
  return fs.existsSync(ffmpegPath) && fs.existsSync(ffprobePath);
});

ipcMain.handle('download-binaries', async (event) => {
  const zipUrl = "https://github.com/GyanD/codexffmpeg/releases/download/6.1.1/ffmpeg-6.1.1-essentials_build.zip";
  const zipPath = path.join(binDir, 'ffmpeg.zip');
  const tempExtractPath = path.join(binDir, 'ffmpeg_temp');

  mainWindow.webContents.send('binary-download-status', { status: 'downloading', progress: 0 });

  try {
    // Download zip using native https
    await new Promise((resolve, reject) => {
      const file = fs.createWriteStream(zipPath);
      let downloadedBytes = 0;
      let totalBytes = 0;

      https.get(zipUrl, (response) => {
        if (response.statusCode !== 200) {
          reject(new Error(`Server responded with status ${response.statusCode}`));
          return;
        }

        totalBytes = parseInt(response.headers['content-length'], 10) || 0;

        response.on('data', (chunk) => {
          downloadedBytes += chunk.length;
          file.write(chunk);
          if (totalBytes > 0) {
            const percent = Math.round((downloadedBytes / totalBytes) * 100);
            mainWindow.webContents.send('binary-download-status', { status: 'downloading', progress: percent });
          }
        });

        response.on('end', () => {
          file.end();
          resolve();
        });
      }).on('error', (err) => {
        fs.unlinkSync(zipPath);
        reject(err);
      });
    });

    mainWindow.webContents.send('binary-download-status', { status: 'extracting', progress: 100 });

    // Extract zip using PowerShell Expand-Archive since we are on Windows!
    if (fs.existsSync(tempExtractPath)) {
      fs.rmSync(tempExtractPath, { recursive: true, force: true });
    }
    fs.mkdirSync(tempExtractPath, { recursive: true });

    // Run powershell extraction
    const psCmd = `Expand-Archive -Path "${zipPath}" -DestinationPath "${tempExtractPath}" -Force`;
    await runCommand('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', psCmd]);

    // Find and copy ffmpeg.exe and ffprobe.exe recursively
    const findAndCopy = (dir) => {
      const files = fs.readdirSync(dir);
      for (const file of files) {
        const fullPath = path.join(dir, file);
        const stat = fs.statSync(fullPath);
        if (stat.isDirectory()) {
          findAndCopy(fullPath);
        } else if (file === 'ffmpeg.exe') {
          fs.copyFileSync(fullPath, ffmpegPath);
        } else if (file === 'ffprobe.exe') {
          fs.copyFileSync(fullPath, ffprobePath);
        }
      }
    };

    findAndCopy(tempExtractPath);

    // Clean up
    fs.unlinkSync(zipPath);
    fs.rmSync(tempExtractPath, { recursive: true, force: true });

    mainWindow.webContents.send('binary-download-status', { status: 'complete', progress: 100 });
    return true;
  } catch (error) {
    console.error("Binary download/extraction failed", error);
    mainWindow.webContents.send('binary-download-status', { status: 'error', error: error.message });
    return false;
  }
});

// 2. Select Folder or Files
ipcMain.handle('select-folder', async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    properties: ['openDirectory']
  });
  return result.canceled ? null : result.filePaths[0];
});

ipcMain.handle('select-files', async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    properties: ['openFile', 'multiSelections'],
    filters: [
      { name: 'Video Files', extensions: ['mp4', 'mkv', 'avi', 'mov', 'flv', 'webm', 'ts', 'm4v'] }
    ]
  });
  return result.canceled ? null : result.filePaths;
});

// 3. Scan paths and fetch metadata
ipcMain.handle('scan-paths', async (event, paths) => {
  const videoFiles = [];

  const scanFile = async (filePath) => {
    const ext = path.extname(filePath).toLowerCase();
    if (!['.mp4', '.mkv', '.avi', '.mov', '.flv', '.webm', '.ts', '.m4v'].includes(ext)) return;

    try {
      // Get codec and duration via ffprobe
      // ffprobe -v error -select_streams v:0 -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1
      const codecNameOut = await runCommand(ffprobePath, [
        '-v', 'error', '-select_streams', 'v:0', '-show_entries', 'stream=codec_name',
        '-of', 'default=noprint_wrappers=1:nokey=1', filePath
      ]);
      const codec = codecNameOut.trim();

      // Get duration
      // ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1
      const durationOut = await runCommand(ffprobePath, [
        '-v', 'error', '-show_entries', 'format=duration',
        '-of', 'default=noprint_wrappers=1:nokey=1', filePath
      ]);
      const duration = parseFloat(durationOut.trim()) || 0;

      const stats = fs.statSync(filePath);
      videoFiles.push({
        name: path.basename(filePath),
        path: filePath,
        sizeBytes: stats.size,
        codec: codec,
        duration: duration
      });
    } catch (e) {
      console.error(`Failed ffprobe check for ${filePath}`, e);
    }
  };

  const scanDir = async (dirPath) => {
    const files = fs.readdirSync(dirPath);
    for (const file of files) {
      const fullPath = path.join(dirPath, file);
      if (fs.existsSync(fullPath)) {
        const stat = fs.statSync(fullPath);
        if (stat.isDirectory()) {
          await scanDir(fullPath);
        } else {
          await scanFile(fullPath);
        }
      }
    }
  };

  for (const p of paths) {
    if (fs.existsSync(p)) {
      const stat = fs.statSync(p);
      if (stat.isDirectory()) {
        await scanDir(p);
      } else {
        await scanFile(p);
      }
    }
  }

  return videoFiles;
});

// 4. Detect GPU Capabilities
ipcMain.handle('get-gpu-capabilities', async () => {
  const caps = { nvenc: false, qsv: false, amf: false };
  if (!fs.existsSync(ffmpegPath)) return caps;

  try {
    const encodersOut = await runCommand(ffmpegPath, ['-encoders']);
    if (encodersOut.includes('hevc_nvenc')) caps.nvenc = true;
    if (encodersOut.includes('hevc_qsv')) caps.qsv = true;
    if (encodersOut.includes('hevc_amf')) caps.amf = true;
  } catch (e) {
    console.error("Failed to query encoders", e);
  }
  return caps;
});

// 5. Transcode Execution Loop
ipcMain.handle('start-compression', async (event, { queue, settings }) => {
  isCancelled = false;
  const history = loadHistory();
  const results = [];

  for (let i = 0; i < queue.length; i++) {
    if (isCancelled) break;

    const file = queue[i];
    
    // Notify starting file
    mainWindow.webContents.send('compress-status', {
      index: i,
      status: 'processing',
      progress: 0,
      speed: '0x',
      fps: 0,
      timeRemaining: 'Calculating...'
    });

    const isAlreadyHevc = ['hevc', 'av1', 'vp9'].includes(file.codec.toLowerCase());
    
    // Check setting "Skip already compressed files"
    if (settings.skipHevc && isAlreadyHevc) {
      mainWindow.webContents.send('compress-status', {
        index: i,
        status: 'skipped',
        message: 'Already in H.265/HEVC or alternative efficient codec.'
      });
      results.push({ path: file.path, status: 'skipped', reason: 'already_efficient' });
      addToHistory({
        filePath: file.path,
        date: new Date().toISOString(),
        status: 'Skipped',
        originalSize: file.sizeBytes,
        compressedSize: file.sizeBytes,
        savedBytes: 0,
        duration: file.duration,
        encoder: file.codec
      }, history);
      continue;
    }

    // Check setting "Skip history"
    if (settings.useHistory && history[file.path]) {
      mainWindow.webContents.send('compress-status', {
        index: i,
        status: 'skipped',
        message: 'Already processed in history.'
      });
      results.push({ path: file.path, status: 'skipped', reason: 'history' });
      continue;
    }

    // Determine encoder to use
    let encoder = 'libx265'; // fallback CPU
    if (settings.encoder === 'auto') {
      const caps = await ipcMain.handleObj['get-gpu-capabilities'](); // Call internal handler directly
      if (caps.nvenc) encoder = 'hevc_nvenc';
      else if (caps.qsv) encoder = 'hevc_qsv';
      else if (caps.amf) encoder = 'hevc_amf';
    } else {
      encoder = settings.encoder;
    }

    // Temp path in original directory
    const dir = path.dirname(file.path);
    const tempName = `temp_compress_${path.basename(file.path)}`;
    const tempPath = path.join(dir, tempName);

    // Build FFmpeg Arguments
    const ffmpegArgs = ['-y', '-v', 'error', '-stats', '-i', file.path];

    // Encoder settings
    ffmpegArgs.push('-c:v', encoder);
    if (encoder === 'hevc_nvenc') {
      ffmpegArgs.push('-cq', settings.crf.toString());
    } else if (encoder === 'hevc_qsv') {
      ffmpegArgs.push('-global_quality', settings.crf.toString());
    } else if (encoder === 'hevc_amf') {
      ffmpegArgs.push('-rc', 'cqp', '-qp_i', settings.crf.toString(), '-qp_p', settings.crf.toString());
    } else {
      // libx265 CPU
      ffmpegArgs.push('-crf', settings.crf.toString(), '-preset', settings.preset);
    }

    // Audio action
    if (settings.audioAction === 'copy') {
      ffmpegArgs.push('-c:a', 'copy');
    } else {
      ffmpegArgs.push('-c:a', 'aac', '-b:a', '128k');
    }

    // Metadata map
    ffmpegArgs.push('-map_metadata', '0');

    // Output path
    ffmpegArgs.push(tempPath);

    mainWindow.webContents.send('compress-log', { index: i, text: `Running: ffmpeg ${ffmpegArgs.join(' ')}\n` });

    try {
      const success = await runFFmpegWithProgress(ffmpegArgs, file.duration, i);

      if (success && !isCancelled) {
        const stats = fs.statSync(tempPath);
        const oldSize = file.sizeBytes;
        const newSize = stats.size;

        if (newSize < oldSize || settings.forceOverwrite) {
          // Success: replace original file or rename if name conflict
          let targetPath = file.path;
          if (settings.overwriteOriginal) {
            fs.unlinkSync(file.path);
            fs.renameSync(tempPath, file.path);
          } else {
            // Save to new file path with "_compressed" suffix
            const parsed = path.parse(file.path);
            targetPath = path.join(parsed.dir, `${parsed.name}_compressed${parsed.ext}`);
            if (fs.existsSync(targetPath)) {
              fs.unlinkSync(targetPath);
            }
            fs.renameSync(tempPath, targetPath);
          }

          const saved = oldSize - newSize;
          const percent = Math.round((saved / oldSize) * 100);

          mainWindow.webContents.send('compress-status', {
            index: i,
            status: 'completed',
            originalSize: oldSize,
            compressedSize: newSize,
            savedBytes: saved,
            percentSaved: percent,
            outputPath: targetPath
          });

          results.push({ path: file.path, status: 'completed', originalSize: oldSize, compressedSize: newSize });
          addToHistory({
            filePath: file.path,
            outputPath: targetPath,
            date: new Date().toISOString(),
            status: 'Success',
            originalSize: oldSize,
            compressedSize: newSize,
            savedBytes: saved,
            duration: file.duration,
            encoder: encoder
          }, history);
        } else {
          // Compressed file is larger, discard
          fs.unlinkSync(tempPath);
          mainWindow.webContents.send('compress-status', {
            index: i,
            status: 'skipped',
            message: 'Compression did not reduce file size. Kept original.'
          });
          results.push({ path: file.path, status: 'skipped', reason: 'larger_size' });
          addToHistory({
            filePath: file.path,
            date: new Date().toISOString(),
            status: 'Skipped (Larger)',
            originalSize: oldSize,
            compressedSize: oldSize,
            savedBytes: 0,
            duration: file.duration,
            encoder: encoder
          }, history);
        }
      } else {
        // Failed or cancelled
        if (fs.existsSync(tempPath)) fs.unlinkSync(tempPath);
        
        mainWindow.webContents.send('compress-status', {
          index: i,
          status: isCancelled ? 'cancelled' : 'failed',
          message: isCancelled ? 'Compression cancelled by user.' : 'Transcoding process failed.'
        });
        results.push({ path: file.path, status: isCancelled ? 'cancelled' : 'failed' });
      }
    } catch (err) {
      console.error(err);
      if (fs.existsSync(tempPath)) fs.unlinkSync(tempPath);
      mainWindow.webContents.send('compress-status', {
        index: i,
        status: 'failed',
        message: err.message
      });
      results.push({ path: file.path, status: 'failed', error: err.message });
    }
  }

  return results;
});

// Helper: Run FFmpeg and parse progress output from stderr
function runFFmpegWithProgress(args, totalDurationSec, fileIndex) {
  return new Promise((resolve) => {
    activeFFmpegProcess = spawn(ffmpegPath, args);

    activeFFmpegProcess.stdout.on('data', (data) => {
      mainWindow.webContents.send('compress-log', { index: fileIndex, text: data.toString() });
    });

    let buffer = '';
    activeFFmpegProcess.stderr.on('data', (data) => {
      const text = data.toString();
      mainWindow.webContents.send('compress-log', { index: fileIndex, text: text });

      // Progress lines look like:
      // frame=  281 fps=140 q=-0.0 size=    2304kB time=00:00:11.24 bitrate=1678.9kbits/s speed=5.58x
      buffer += text;
      const lines = buffer.split('\r');
      buffer = lines.pop(); // keep last incomplete line

      for (const line of lines) {
        const cleanLine = line.trim();
        if (!cleanLine) continue;

        // Extract values using regex
        const timeMatch = cleanLine.match(/time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})/);
        const fpsMatch = cleanLine.match(/fps=\s*(\d+(\.\d+)?)/);
        const speedMatch = cleanLine.match(/speed=\s*(\d+(\.\d+)?)x/);

        if (timeMatch && totalDurationSec > 0) {
          const hrs = parseInt(timeMatch[1], 10);
          const mins = parseInt(timeMatch[2], 10);
          const secs = parseInt(timeMatch[3], 10);
          const ms = parseInt(timeMatch[4], 10);
          
          const currentSecs = (hrs * 3600) + (mins * 60) + secs + (ms / 100);
          const progress = Math.min(Math.round((currentSecs / totalDurationSec) * 100), 99);
          
          const fps = fpsMatch ? parseFloat(fpsMatch[1]) : 0;
          const speed = speedMatch ? speedMatch[0] : '0x';
          const speedVal = speedMatch ? parseFloat(speedMatch[1]) : 0;

          // Estimate time remaining
          let timeRemaining = 'Calculating...';
          if (speedVal > 0 && progress > 5) {
            const remainingSecs = (totalDurationSec - currentSecs) / speedVal;
            const remMin = Math.floor(remainingSecs / 60);
            const remSec = Math.floor(remainingSecs % 60);
            timeRemaining = `${remMin}m ${remSec}s`;
          }

          mainWindow.webContents.send('compress-status', {
            index: fileIndex,
            status: 'processing',
            progress: progress,
            speed: speed,
            fps: fps,
            timeRemaining: timeRemaining
          });
        }
      }
    });

    activeFFmpegProcess.on('close', (code) => {
      activeFFmpegProcess = null;
      resolve(code === 0);
    });
  });
}

// 6. Cancel compression IPC handler
ipcMain.handle('cancel-compression', () => {
  isCancelled = true;
  if (activeFFmpegProcess) {
    activeFFmpegProcess.kill('SIGINT');
  }
});

// 7. Open directory/file Shell Helpers
ipcMain.handle('open-folder', async (event, folderPath) => {
  if (fs.existsSync(folderPath)) {
    const isDir = fs.statSync(folderPath).isDirectory();
    const dir = isDir ? folderPath : path.dirname(folderPath);
    await shell.openPath(dir);
  }
});

ipcMain.handle('open-file', async (event, filePath) => {
  if (fs.existsSync(filePath)) {
    await shell.showItemInFolder(filePath);
  }
});

// 8. History Load/Save
function loadHistory() {
  if (!fs.existsSync(historyPath)) return {};
  try {
    const content = fs.readFileSync(historyPath, 'utf8');
    const list = JSON.parse(content);
    const map = {};
    if (Array.isArray(list)) {
      list.forEach(item => {
        if (item.filePath) map[item.filePath] = item;
      });
    }
    return map;
  } catch (e) {
    console.error("Failed parsing history", e);
    return {};
  }
}

function getHistoryList() {
  if (!fs.existsSync(historyPath)) return [];
  try {
    const content = fs.readFileSync(historyPath, 'utf8');
    const parsed = JSON.parse(content);
    return Array.isArray(parsed) ? parsed : [];
  } catch (e) {
    return [];
  }
}

function addToHistory(item, historyMap) {
  const list = getHistoryList();
  list.unshift(item); // prepend new items
  fs.writeFileSync(historyPath, JSON.stringify(list, null, 2), 'utf8');
  historyMap[item.filePath] = item;
}

ipcMain.handle('get-history', () => {
  return getHistoryList();
});

ipcMain.handle('clear-history', () => {
  fs.writeFileSync(historyPath, JSON.stringify([], null, 2), 'utf8');
  return true;
});

// Internal lookup of IPC handlers to allow main script self-calls
const ipcMainHandleObj = {};
const originalHandle = ipcMain.handle.bind(ipcMain);
ipcMain.handle = (channel, listener) => {
  ipcMainHandleObj[channel] = listener;
  originalHandle(channel, listener);
};
ipcMain.handleObj = ipcMainHandleObj;
