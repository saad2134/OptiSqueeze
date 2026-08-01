@echo off
title Compress Videos (H.264 to H.265 GPU)
echo Launching video compressor...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0codec_from_h264_to_h265.ps1" -targetDir "%~1"
pause
