const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
  checkBinaries: () => ipcRenderer.invoke('check-binaries'),
  downloadBinaries: () => ipcRenderer.invoke('download-binaries'),
  selectFolder: () => ipcRenderer.invoke('select-folder'),
  selectFiles: () => ipcRenderer.invoke('select-files'),
  scanPaths: (paths) => ipcRenderer.invoke('scan-paths'),
  getGPUCapabilities: () => ipcRenderer.invoke('get-gpu-capabilities'),
  startCompression: (data) => ipcRenderer.invoke('start-compression', data),
  cancelCompression: () => ipcRenderer.invoke('cancel-compression'),
  openFolder: (path) => ipcRenderer.invoke('open-folder', path),
  openFile: (path) => ipcRenderer.invoke('open-file', path),
  getHistory: () => ipcRenderer.invoke('get-history'),
  clearHistory: () => ipcRenderer.invoke('clear-history'),
  
  // Listeners from Main to Renderer
  onProgress: (callback) => ipcRenderer.on('compress-status', (event, value) => callback(value)),
  onLog: (callback) => ipcRenderer.on('compress-log', (event, value) => callback(value)),
  onBinaryDownloadProgress: (callback) => ipcRenderer.on('binary-download-status', (event, value) => callback(value))
});
