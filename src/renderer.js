// Local App State
let queue = [];
let isCompressing = false;
let activeTab = 'compressor';

// Format Helpers
function formatBytes(bytes, decimals = 2) {
  if (bytes === 0) return '0 Bytes';
  const k = 1024;
  const dm = decimals < 0 ? 0 : decimals;
  const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + ' ' + sizes[i];
}

function formatDuration(seconds) {
  if (!seconds || isNaN(seconds)) return 'Unknown';
  const hrs = Math.floor(seconds / 3600);
  const mins = Math.floor((seconds % 3600) / 60);
  const secs = Math.floor(seconds % 60);
  if (hrs > 0) {
    return `${hrs}h ${mins}m ${secs}s`;
  }
  return `${mins}m ${secs}s`;
}

// 1. Initial State and Binary Verification
document.addEventListener('DOMContentLoaded', async () => {
  setupTabs();
  setupSettingsListeners();
  setupDragAndDrop();
  setupConsoleToggle();
  
  const hasBinaries = await window.electronAPI.checkBinaries();
  if (!hasBinaries) {
    showBinaryOverlay(true);
  } else {
    initializeDashboard();
  }
});

// Setup Binary Missing Overlay Actions
const binaryOverlay = document.getElementById('binary-overlay');
const btnDownloadBinaries = document.getElementById('btn-download-binaries');
const binaryActions = document.getElementById('binary-actions');
const binaryProgressPanel = document.getElementById('binary-progress-panel');
const binaryProgressText = document.getElementById('binary-progress-text');
const binaryProgressPercent = document.getElementById('binary-progress-percent');
const binaryProgressFill = document.getElementById('binary-progress-fill');

function showBinaryOverlay(show) {
  if (show) {
    binaryOverlay.classList.remove('hidden');
  } else {
    binaryOverlay.classList.add('hidden');
  }
}

btnDownloadBinaries.addEventListener('click', async () => {
  binaryActions.classList.add('hidden');
  binaryProgressPanel.classList.remove('hidden');
  
  const success = await window.electronAPI.downloadBinaries();
  if (success) {
    showBinaryOverlay(false);
    initializeDashboard();
  } else {
    binaryActions.classList.remove('hidden');
    binaryProgressPanel.classList.add('hidden');
    alert("Failed to download FFmpeg binaries. Check your internet connection or try installing manually.");
  }
});

// Listen to download progress
window.electronAPI.onBinaryDownloadProgress(({ status, progress, error }) => {
  if (status === 'downloading') {
    binaryProgressText.innerText = "Downloading FFmpeg ZIP...";
    binaryProgressPercent.innerText = `${progress}%`;
    binaryProgressFill.style.width = `${progress}%`;
  } else if (status === 'extracting') {
    binaryProgressText.innerText = "Extracting files (this may take a few seconds)...";
    binaryProgressPercent.innerText = "100%";
    binaryProgressFill.style.width = "100%";
  } else if (status === 'error') {
    binaryProgressText.innerText = `Error: ${error}`;
    binaryProgressPercent.innerText = "0%";
    binaryProgressFill.style.width = "0%";
  }
});

// Main Initializer once binaries exist
async function initializeDashboard() {
  // Query GPU hardware encoders
  const caps = await window.electronAPI.getGPUCapabilities();
  if (caps.nvenc) document.getElementById('badge-nvenc').classList.add('active');
  if (caps.qsv) document.getElementById('badge-qsv').classList.add('active');
  if (caps.amf) document.getElementById('badge-amf').classList.add('active');

  // Load history & stats
  await loadStatsAndHistory();
}

// 2. Navigation Tab Setup
function setupTabs() {
  const navItems = document.querySelectorAll('.nav-item');
  const tabPanes = document.querySelectorAll('.tab-pane');

  navItems.forEach(item => {
    item.addEventListener('click', () => {
      if (isCompressing && item.getAttribute('data-tab') !== 'compressor') {
        alert("Please wait for the current compression job to complete or cancel it first.");
        return;
      }

      navItems.forEach(nav => nav.classList.remove('active'));
      tabPanes.forEach(pane => pane.classList.remove('active'));

      item.classList.add('active');
      activeTab = item.getAttribute('data-tab');
      document.getElementById(`tab-${activeTab}`).classList.add('active');

      if (activeTab === 'history') {
        loadStatsAndHistory();
      }
    });
  });
}

// 3. Settings UI Interactive Behavior
function setupSettingsListeners() {
  const encoderSelect = document.getElementById('setting-encoder');
  const cpuPresetGroup = document.getElementById('cpu-preset-group');
  const crfSlider = document.getElementById('setting-crf');
  const crfDisplay = document.getElementById('crf-value-display');

  // Show CPU preset only if CPU encoder (libx265) is selected
  encoderSelect.addEventListener('change', () => {
    if (encoderSelect.value === 'libx265') {
      cpuPresetGroup.classList.remove('hidden');
    } else {
      cpuPresetGroup.classList.add('hidden');
    }
  });

  // Slider update display
  crfSlider.addEventListener('input', () => {
    crfDisplay.innerText = crfSlider.value;
  });
}

// 4. Console Accordion Setup
function setupConsoleToggle() {
  const toggleBtn = document.getElementById('btn-toggle-console');
  const consoleBody = document.getElementById('console-body');

  toggleBtn.addEventListener('click', () => {
    consoleBody.classList.toggle('collapsed');
  });
}

// 5. Drag and Drop & Browsing Configuration
function setupDragAndDrop() {
  const dropZone = document.getElementById('drop-zone');
  const btnFiles = document.getElementById('btn-browse-files');
  const btnFolder = document.getElementById('btn-browse-folder');

  ['dragenter', 'dragover'].forEach(eventName => {
    dropZone.addEventListener(eventName, (e) => {
      e.preventDefault();
      dropZone.classList.add('dragover');
    }, false);
  });

  ['dragleave', 'drop'].forEach(eventName => {
    dropZone.addEventListener(eventName, (e) => {
      e.preventDefault();
      dropZone.classList.remove('dragover');
    }, false);
  });

  dropZone.addEventListener('drop', async (e) => {
    const files = e.dataTransfer.files;
    if (files.length > 0) {
      const paths = Array.from(files).map(f => f.path);
      await handleAddPaths(paths);
    }
  });

  btnFiles.addEventListener('click', async (e) => {
    e.stopPropagation(); // prevent triggering dropzone click
    const paths = await window.electronAPI.selectFiles();
    if (paths) await handleAddPaths(paths);
  });

  btnFolder.addEventListener('click', async (e) => {
    e.stopPropagation(); // prevent triggering dropzone click
    const path = await window.electronAPI.selectFolder();
    if (path) await handleAddPaths([path]);
  });

  // Click anywhere on dropzone (excluding buttons) to browse files
  dropZone.addEventListener('click', async (e) => {
    if (e.target !== btnFiles && e.target !== btnFolder) {
      const paths = await window.electronAPI.selectFiles();
      if (paths) await handleAddPaths(paths);
    }
  });
}

// Handle scanning and adding paths to queue
async function handleAddPaths(paths) {
  document.body.style.cursor = 'wait';
  try {
    const scanned = await window.electronAPI.scanPaths(paths);
    
    // Filter duplicates
    const existingPaths = new Set(queue.map(f => f.path));
    const newFiles = scanned.filter(f => !existingPaths.has(f.path));
    
    if (newFiles.length === 0) {
      if (scanned.length > 0) {
        alert("Selected files are already in the compression queue.");
      } else {
        alert("No valid video files found in the selected folders (.mp4, .mkv, .avi, etc.).");
      }
      return;
    }

    queue = queue.concat(newFiles);
    renderQueue();
  } catch (error) {
    console.error(error);
    alert("Error scanning files: " + error.message);
  } finally {
    document.body.style.cursor = 'default';
  }
}

// Render queue file list cards
function renderQueue() {
  const queuePanel = document.getElementById('queue-panel');
  const dropZone = document.getElementById('drop-zone');
  const queueList = document.getElementById('queue-list');
  const queueCount = document.getElementById('queue-count');
  const totalInputSizeDisp = document.getElementById('total-input-size');

  if (queue.length === 0) {
    queuePanel.classList.add('hidden');
    dropZone.classList.remove('hidden');
    return;
  }

  // Swap views
  dropZone.classList.add('hidden');
  queuePanel.classList.remove('hidden');

  queueCount.innerText = `${queue.length} File${queue.length > 1 ? 's' : ''} Loaded`;
  
  let totalBytes = 0;
  queueList.innerHTML = '';
  
  queue.forEach((file, index) => {
    totalBytes += file.sizeBytes;
    
    const card = document.createElement('div');
    card.className = 'queue-item';
    card.id = `queue-item-${index}`;
    
    card.innerHTML = `
      <div class="file-info">
        <span class="file-name" title="${file.name}">${file.name}</span>
        <div class="file-meta">
          <span>${file.codec.toUpperCase()}</span>
          <span class="meta-divider"></span>
          <span>${formatBytes(file.sizeBytes)}</span>
          <span class="meta-divider"></span>
          <span>${formatDuration(file.duration)}</span>
        </div>
      </div>
      <div class="file-status">
        <span class="status-text pending" id="status-txt-${index}">Pending</span>
        <span class="help-text" id="status-details-${index}">Waiting to start</span>
      </div>
      <div class="item-progress-bar">
        <div class="item-progress-fill" id="progress-fill-${index}"></div>
      </div>
    `;
    
    queueList.appendChild(card);
  });

  totalInputSizeDisp.innerText = formatBytes(totalBytes);
}

// Clear active queue
const btnClearQueue = document.getElementById('btn-clear-queue');
btnClearQueue.addEventListener('click', () => {
  if (isCompressing) return;
  queue = [];
  renderQueue();
  document.getElementById('console-logs').innerText = 'Console waiting for task...';
});

// 6. Start Compression Runner
const btnStart = document.getElementById('btn-start');
const btnCancel = document.getElementById('btn-cancel');

btnStart.addEventListener('click', async () => {
  if (queue.length === 0 || isCompressing) return;

  isCompressing = true;
  btnStart.classList.add('hidden');
  btnCancel.classList.remove('hidden');
  btnCancel.innerText = "Cancel Process";
  btnCancel.removeAttribute('disabled');
  btnClearQueue.setAttribute('disabled', 'true');
  
  // Disable sidebar navigation tabs & settings inputs to lock screen state
  document.querySelectorAll('.nav-item').forEach(el => {
    if (!el.classList.contains('active')) el.style.pointerEvents = 'none';
  });
  toggleSettingsDisabled(true);

  // Gather UI Settings
  const settings = {
    encoder: document.getElementById('setting-encoder').value,
    crf: parseInt(document.getElementById('setting-crf').value, 10),
    preset: document.getElementById('setting-preset').value,
    audioAction: document.getElementById('setting-audio').value,
    overwriteOriginal: document.getElementById('setting-overwrite-original').checked,
    forceOverwrite: document.getElementById('setting-force-overwrite').checked,
    skipHevc: document.getElementById('setting-skip-hevc').checked,
    useHistory: document.getElementById('setting-use-history').checked
  };

  document.getElementById('console-logs').innerText = 'Starting compression process...\n';
  
  try {
    const results = await window.electronAPI.startCompression({ queue, settings });
    
    // Complete Session Alert
    const processed = results.filter(r => r.status === 'completed');
    const skipped = results.filter(r => r.status === 'skipped');
    const failed = results.filter(r => r.status === 'failed');
    
    let totalSaved = 0;
    processed.forEach(r => {
      if (r.originalSize && r.compressedSize) {
        totalSaved += (r.originalSize - r.compressedSize);
      }
    });

    const msg = `Compression completed!\n\nSuccess: ${processed.length}\nSkipped: ${skipped.length}\nFailed: ${failed.length}\nTotal space saved: ${formatBytes(totalSaved)}`;
    alert(msg);

  } catch (error) {
    console.error("Transcode session crashed", error);
    alert("Compression session failed: " + error.message);
  } finally {
    isCompressing = false;
    btnStart.classList.remove('hidden');
    btnCancel.classList.add('hidden');
    btnClearQueue.removeAttribute('disabled');

    // Re-enable navigation
    document.querySelectorAll('.nav-item').forEach(el => el.style.pointerEvents = 'auto');
    toggleSettingsDisabled(false);
  }
});

btnCancel.addEventListener('click', async () => {
  btnCancel.setAttribute('disabled', 'true');
  btnCancel.innerText = "Stopping process...";
  await window.electronAPI.cancelCompression();
});

// Disable/Enable setting controls helper
function toggleSettingsDisabled(disable) {
  const selectElements = document.querySelectorAll('.settings-card select');
  const checkboxElements = document.querySelectorAll('.settings-card input[type="checkbox"]');
  const slider = document.getElementById('setting-crf');

  selectElements.forEach(el => el.disabled = disable);
  checkboxElements.forEach(el => el.disabled = disable);
  slider.disabled = disable;
}

// 7. Event Listeners for Live Encoding Feedback
window.electronAPI.onProgress((data) => {
  const { index, status, progress, speed, fps, timeRemaining, originalSize, compressedSize, savedBytes, percentSaved, message } = data;
  
  const card = document.getElementById(`queue-item-${index}`);
  const statusTxt = document.getElementById(`status-txt-${index}`);
  const detailsTxt = document.getElementById(`status-details-${index}`);
  const progressFill = document.getElementById(`progress-fill-${index}`);

  if (!card) return;

  // Auto-scroll list to focus on active item
  card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });

  if (status === 'processing') {
    card.classList.add('active');
    statusTxt.className = 'status-text processing';
    statusTxt.innerText = `Running (${progress}%)`;
    detailsTxt.innerText = `${speed} speed | ${fps} FPS | ${timeRemaining} left`;
    progressFill.style.width = `${progress}%`;
  } else if (status === 'completed') {
    card.classList.remove('active');
    card.style.borderColor = 'rgba(16, 185, 129, 0.4)';
    statusTxt.className = 'status-text completed';
    statusTxt.innerText = 'Completed';
    detailsTxt.innerHTML = `<span class="save-positive">Saved ${formatBytes(savedBytes)} (-${percentSaved}%)</span>`;
    progressFill.style.width = '100%';
  } else if (status === 'skipped') {
    card.classList.remove('active');
    card.style.borderColor = 'rgba(245, 158, 11, 0.4)';
    statusTxt.className = 'status-text skipped';
    statusTxt.innerText = 'Skipped';
    detailsTxt.innerText = message || 'Already compressed';
  } else if (status === 'failed') {
    card.classList.remove('active');
    card.style.borderColor = 'rgba(239, 68, 68, 0.4)';
    statusTxt.className = 'status-text failed';
    statusTxt.innerText = 'Failed';
    detailsTxt.innerText = message || 'Error running ffmpeg';
  } else if (status === 'cancelled') {
    card.classList.remove('active');
    statusTxt.className = 'status-text failed';
    statusTxt.innerText = 'Cancelled';
    detailsTxt.innerText = 'Cancelled by user';
  }
});

// Append logs to Console View
const consoleLogs = document.getElementById('console-logs');
const consoleBody = document.getElementById('console-body');
window.electronAPI.onLog(({ text }) => {
  consoleLogs.appendChild(document.createTextNode(text));
  // Keep scrolling to bottom of console logs if console pane is open
  if (!consoleBody.classList.contains('collapsed')) {
    consoleBody.scrollTop = consoleBody.scrollHeight;
  }
});

// 8. Stats & History Calculations
async function loadStatsAndHistory() {
  const historyList = await window.electronAPI.getHistory();

  // Elements
  const totalFilesEl = document.getElementById('stats-total-files');
  const totalSavedEl = document.getElementById('stats-total-saved');
  const avgRatioEl = document.getElementById('stats-avg-ratio');
  const tableBody = document.getElementById('history-table-body');

  if (!historyList || historyList.length === 0) {
    totalFilesEl.innerText = '0';
    totalSavedEl.innerText = '0 MB';
    avgRatioEl.innerText = '0%';
    tableBody.innerHTML = `<tr><td colspan="7" class="empty-state">No historical items found. Run some compression tasks first!</td></tr>`;
    return;
  }

  // Accumulators
  const successItems = historyList.filter(h => h.status === 'Success');
  const totalSavedBytes = successItems.reduce((acc, curr) => acc + (curr.savedBytes || 0), 0);
  
  let totalRatioSum = 0;
  successItems.forEach(h => {
    if (h.originalSize > 0) {
      totalRatioSum += (h.savedBytes / h.originalSize);
    }
  });
  
  const avgRatio = successItems.length > 0 
    ? Math.round((totalRatioSum / successItems.length) * 100) 
    : 0;

  totalFilesEl.innerText = successItems.length.toString();
  totalSavedEl.innerText = formatBytes(totalSavedBytes);
  avgRatioEl.innerText = `${avgRatio}%`;

  // Render Table
  tableBody.innerHTML = '';
  historyList.forEach((item) => {
    const row = document.createElement('tr');
    
    const formattedDate = new Date(item.date).toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });

    const isSuccess = item.status === 'Success';
    const originalSizeFormatted = formatBytes(item.originalSize);
    const compressedSizeFormatted = formatBytes(item.compressedSize);
    
    let savedCellText = '0 MB';
    if (isSuccess && item.savedBytes > 0) {
      const pct = Math.round((item.savedBytes / item.originalSize) * 100);
      savedCellText = `<span class="save-positive">${formatBytes(item.savedBytes)} (-${pct}%)</span>`;
    } else {
      savedCellText = `<span class="help-text">${item.status}</span>`;
    }

    row.innerHTML = `
      <td title="${item.filePath}" style="max-width: 250px; overflow: hidden; text-overflow: ellipsis;">${pathFilename(item.filePath)}</td>
      <td>${originalSizeFormatted}</td>
      <td>${compressedSizeFormatted}</td>
      <td>${savedCellText}</td>
      <td><span class="badge" style="background: rgba(255,255,255,0.03); color: var(--text-muted); display:inline-block; width:auto; padding: 2px 8px; text-transform:lowercase;">${item.encoder}</span></td>
      <td style="font-size:12px; color: var(--text-dark);">${formattedDate}</td>
      <td>
        <button class="btn btn-secondary btn-sm" onclick="locateFile('${item.outputPath || item.filePath}')">Locate</button>
      </td>
    `;
    
    tableBody.appendChild(row);
  });
}

// Setup Clear History Button
const btnClearHistory = document.getElementById('btn-clear-history');
btnClearHistory.addEventListener('click', async () => {
  if (confirm("Are you sure you want to clear your compression dashboard stats and run history? This cannot be undone.")) {
    await window.electronAPI.clearHistory();
    await loadStatsAndHistory();
  }
});

// Helper functions for path handling in renderer
function pathFilename(fullPath) {
  if (!fullPath) return 'Unknown';
  return fullPath.split('\\').pop().split('/').pop();
}

window.locateFile = function(targetPath) {
  window.electronAPI.openFile(targetPath);
};
