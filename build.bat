@echo off
title Compiling OptiSqueeze...
echo Compiling C# Native WPF Application...

C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:OptiSqueeze.exe /win32icon:src\icon.ico /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /reference:PresentationFramework.dll /reference:PresentationCore.dll /reference:WindowsBase.dll /reference:System.Xaml.dll /reference:System.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Net.Http.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\OptiSqueeze.cs

if %errorlevel% equ 0 (
    echo.
    echo Compilation Successful! Generated OptiSqueeze.exe
) else (
    echo.
    echo Compilation Failed. Check errors above.
    pause
)
