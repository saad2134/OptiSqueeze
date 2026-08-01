param(
    [string]$targetDir
)

# Configuration
$ffmpegPath = "C:\Users\UwU\Downloads\ffmpeg.exe"
$ffprobePath = "C:\Users\UwU\Downloads\ffprobe.exe"
$zipUrl = "https://github.com/GyanD/codexffmpeg/releases/download/6.1.1/ffmpeg-6.1.1-essentials_build.zip"

# Prompt for directory if not passed as a parameter
if (-not $targetDir) {
    Write-Host "=== Video Compressor Tool ===" -ForegroundColor Green
    $targetDir = Read-Host "Please drag-and-drop or enter the folder path to compress"
    $targetDir = $targetDir.Trim("`"", "'").Trim()
}

# Verify directory exists
if (-not (Test-Path -LiteralPath $targetDir -PathType Container)) {
    Write-Host "Error: Directory '$targetDir' not found." -ForegroundColor Red
    Read-Host "Press Enter to exit..."
    exit
}

# 1. Download FFmpeg if missing
if (-not (Test-Path $ffmpegPath) -or -not (Test-Path $ffprobePath)) {
    Write-Host "FFmpeg or FFprobe not found. Downloading latest release..." -ForegroundColor Cyan
    $zipPath = "C:\Users\UwU\Downloads\ffmpeg.zip"
    $extractPath = "C:\Users\UwU\Downloads\ffmpeg-temp"
    
    # Download
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath
    Write-Host "Download complete. Extracting..." -ForegroundColor Cyan
    
    # Extract
    if (Test-Path $extractPath) { Remove-Item $extractPath -Recurse -Force }
    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force
    
    # Locate binaries
    $ffmpegBin = Get-ChildItem -Path $extractPath -Filter "ffmpeg.exe" -Recurse | Select-Object -First 1
    $ffprobeBin = Get-ChildItem -Path $extractPath -Filter "ffprobe.exe" -Recurse | Select-Object -First 1
    
    # Copy
    Copy-Item -Path $ffmpegBin.FullName -Destination $ffmpegPath -Force
    Copy-Item -Path $ffprobeBin.FullName -Destination $ffprobePath -Force
    
    # Clean up
    Remove-Item -Path $zipPath -Force
    Remove-Item -Path $extractPath -Recurse -Force
    Write-Host "FFmpeg successfully installed!" -ForegroundColor Green
}

# 2. Search for videos to compress
Write-Host "Scanning for videos in: $targetDir..." -ForegroundColor Cyan
$videos = Get-ChildItem -LiteralPath $targetDir -Recurse -File | Where-Object { 
    $_.Extension -match '^\.(mp4|mkv|avi)$' 
}

$totalVideos = $videos.Count
Write-Host "Found $totalVideos videos to analyze." -ForegroundColor Cyan

$processedCount = 0
$savedBytes = 0

# Load processing history to prevent re-analyzing/re-compressing already processed files
$historyFile = Join-Path $targetDir ".compress_history.txt"
$history = @{}
if (Test-Path $historyFile) {
    Get-Content $historyFile | ForEach-Object { $history[$_] = $true }
}

# 3. Main processing loop
foreach ($video in $videos) {
    $filePath = $video.FullName
    
    # Check history first
    if ($history.ContainsKey($filePath)) {
        Write-Host "Skipping (already processed in history): $($video.Name)" -ForegroundColor Gray
        continue
    }
    $dirName = $video.DirectoryName
    $tempPath = Join-Path $dirName "temp_compress_$($video.Name)"
    
    # Get codec using ffprobe
    $codec = & $ffprobePath -v error -select_streams v:0 -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1 $filePath
    $codec = $codec.Trim()
    
    if ($codec -eq "hevc" -or $codec -eq "av1" -or $codec -eq "vp9") {
        Write-Host "Skipping (already compressed): $($video.Name) ($codec)" -ForegroundColor Gray
        # Add to history so we don't even run ffprobe on it next time
        $filePath | Out-File -FilePath $historyFile -Append -Encoding utf8
        $history[$filePath] = $true
        continue
    }
    
    Write-Host "--------------------------------------------------"
    Write-Host "Compressing: $($video.Name) (Codec: $codec | Size: $([Math]::Round($video.Length / 1MB, 2)) MB)" -ForegroundColor Yellow
    
    # Transcode try 1: NVIDIA NVENC (Fastest GPU)
    $success = $false
    & $ffmpegPath -y -v error -stats -i $filePath -c:v hevc_nvenc -cq 24 -c:a copy -map_metadata 0 $tempPath
    
    if ($LASTEXITCODE -eq 0 -and (Test-Path $tempPath) -and (Get-Item $tempPath).Length -gt 0) {
        $success = $true
    } else {
        # Transcode try 2: Intel QuickSync (QSV GPU)
        Write-Host "NVIDIA GPU encoding failed. Trying Intel QuickSync (QSV) GPU..." -ForegroundColor DarkYellow
        if (Test-Path $tempPath) { Remove-Item $tempPath -Force }
        
        & $ffmpegPath -y -v error -stats -i $filePath -c:v hevc_qsv -pix_fmt nv12 -global_quality 24 -c:a copy -map_metadata 0 $tempPath
        
        if ($LASTEXITCODE -eq 0 -and (Test-Path $tempPath) -and (Get-Item $tempPath).Length -gt 0) {
            $success = $true
        } else {
            # Transcode try 3: CPU Encoding (libx265 - Slower)
            Write-Host "Intel QSV failed. Falling back to CPU software encoding (libx265 - SLOWER)..." -ForegroundColor DarkYellow
            if (Test-Path $tempPath) { Remove-Item $tempPath -Force }
            
            & $ffmpegPath -y -v error -stats -i $filePath -c:v libx265 -preset fast -crf 24 -c:a copy -map_metadata 0 $tempPath
            
            if ($LASTEXITCODE -eq 0 -and (Test-Path $tempPath) -and (Get-Item $tempPath).Length -gt 0) {
                $success = $true
            }
        }
    }
    
    if ($success) {
        $oldSize = $video.Length
        $newSize = (Get-Item $tempPath).Length
        
        if ($newSize -lt $oldSize) {
            # Success: replace file
            Remove-Item -LiteralPath $filePath -Force
            Rename-Item -LiteralPath $tempPath -NewName $video.Name -Force
            
            $saved = $oldSize - $newSize
            $savedBytes += $saved
            $processedCount++
            
            # Add to history
            $filePath | Out-File -FilePath $historyFile -Append -Encoding utf8
            $history[$filePath] = $true
            
            $percent = [Math]::Round(($saved / $oldSize) * 100, 1)
            Write-Host "Success! Saved $([Math]::Round($saved / 1MB, 2)) MB ($percent% smaller)" -ForegroundColor Green
        } else {
            # Output is larger than original (rare, but happens for heavily compressed low-res files)
            Remove-Item -LiteralPath $tempPath -Force
            Write-Host "Skipped: Compressed file was larger than original." -ForegroundColor Yellow
            
            # Add to history so we don't try to re-compress it in future runs
            $filePath | Out-File -FilePath $historyFile -Append -Encoding utf8
            $history[$filePath] = $true
        }
    } else {
        # Failed
        if (Test-Path $tempPath) { Remove-Item -LiteralPath $tempPath -Force }
        Write-Host "Failed to compress: $($video.Name). Keeping original." -ForegroundColor Red
        Write-Host "Note: If NVENC fails, ensure your graphics card drivers are updated." -ForegroundColor DarkYellow
    }
}

Write-Host "==================================================" -ForegroundColor Green
Write-Host "Compressing Session Complete!" -ForegroundColor Green
Write-Host "Files compressed: $processedCount" -ForegroundColor Green
Write-Host "Total space saved: $([Math]::Round($savedBytes / 1GB, 2)) GB" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
Read-Host "Press Enter to exit..."
