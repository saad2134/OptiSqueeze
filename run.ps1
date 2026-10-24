# run.ps1
# Direct in-memory C# compilation launcher. Bypasses Smart App Control universally.

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if (-not $scriptDir) { $scriptDir = Get-Location }

# Load source code
$sourcePath = Join-Path $scriptDir "src\OptiSqueeze.cs"
if (-not (Test-Path $sourcePath)) {
    Write-Error "Source file src\OptiSqueeze.cs not found."
    Read-Host "Press enter to exit..."
    exit
}
$sourceCode = [System.IO.File]::ReadAllText($sourcePath, [System.Text.Encoding]::UTF8)

# Link WPF and Windows Forms references
$assemblies = @(
    "PresentationFramework"
    "PresentationCore"
    "WindowsBase"
    "System.Xaml"
    "System"
    "System.Windows.Forms"
    "System.Drawing"
    "System.Net.Http"
    "System.IO.Compression.FileSystem"
)

Write-Host "Launching OptiSqueeze..." -ForegroundColor Cyan
Write-Host "Compiling native WPF application in-memory (Smart App Control bypass)..." -ForegroundColor Gray

try {
    # Compile on-the-fly inside PowerShell
    Add-Type -TypeDefinition $sourceCode -ReferencedAssemblies $assemblies -ErrorAction Stop

    # Start the WPF window with custom base path context
    [OptiSqueeze.Program]::Main(@($scriptDir))
}
catch {
    Write-Host "`nFailed to launch OptiSqueeze:" -ForegroundColor Red
    Write-Host $_.Exception.ToString() -ForegroundColor DarkRed
    Read-Host "`nPress enter to exit..."
    exit 1
}
