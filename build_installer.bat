@echo off
setlocal
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "REF_ROOT=C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found at %CSC%
    exit /b 1
)

call "%~dp0build.bat"
if errorlevel 1 (
    echo [FAILED] Core binaries build failed.
    exit /b 1
)

set "OBJ_DIR=%~dp0obj_installer"
set "ZIP_PATH=%OBJ_DIR%\DesktopMemoPayload.zip"
if exist "%OBJ_DIR%" rmdir /s /q "%OBJ_DIR%"
mkdir "%OBJ_DIR%"

echo Packaging payload archive...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.IO.Compression; Add-Type -AssemblyName System.IO.Compression.FileSystem; $root='%~dp0'; $zip='%ZIP_PATH%'; if (Test-Path $zip) { Remove-Item $zip -Force }; $za=[System.IO.Compression.ZipFile]::Open($zip,[System.IO.Compression.ZipArchiveMode]::Create); try { foreach ($f in @('DesktopMemo.exe','DesktopMemoServer.exe','start_server.bat','setup_windows_server.ps1')) { $p=Join-Path $root $f; if (Test-Path $p) { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za,$p,$f,[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null } }; $webDir=Join-Path $root 'web'; if (Test-Path $webDir) { Get-ChildItem $webDir -Recurse -File | ForEach-Object { $rel='web/'+$_.FullName.Substring($webDir.Length+1).Replace('\','/'); [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za,$_.FullName,$rel,[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null } } } finally { $za.Dispose() }"
if errorlevel 1 (
    echo [FAILED] Payload packaging failed.
    rmdir /s /q "%OBJ_DIR%"
    exit /b 1
)
if not exist "%ZIP_PATH%" (
    echo [FAILED] Payload archive missing.
    rmdir /s /q "%OBJ_DIR%"
    exit /b 1
)

echo Compiling DesktopMemoSetup.exe ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /utf8output ^
  /win32manifest:"%~dp0src\app.manifest" ^
  /win32icon:"%~dp0src\app.ico" ^
  /resource:"%ZIP_PATH%",DesktopMemoPayload.zip ^
  /r:"%REF_ROOT%\WPF\PresentationCore.dll" ^
  /r:"%REF_ROOT%\WPF\PresentationFramework.dll" ^
  /r:"%REF_ROOT%\WPF\WindowsBase.dll" ^
  /r:"%REF_ROOT%\System.Xaml.dll" ^
  /r:"%REF_ROOT%\System.Windows.Forms.dll" ^
  /r:"%REF_ROOT%\System.Drawing.dll" ^
  /r:"%REF_ROOT%\System.Net.Http.dll" ^
  /r:"%REF_ROOT%\System.Web.Extensions.dll" ^
  /r:"%REF_ROOT%\System.Runtime.Serialization.dll" ^
  /r:"%REF_ROOT%\System.IO.Compression.dll" ^
  /r:"%REF_ROOT%\System.IO.Compression.FileSystem.dll" ^
  /out:"%~dp0DesktopMemoSetup.exe" ^
  "%~dp0installer_src\InstallerProgram.cs" "%~dp0src\Models.cs" "%~dp0src\ThemeService.cs" "%~dp0src\GlassControlStyles.cs" "%~dp0src\SpringAnimationHelper.cs" "%~dp0src\Win32Native.cs"
set "COMPILE_ERR=%errorlevel%"
rmdir /s /q "%OBJ_DIR%"
if not "%COMPILE_ERR%"=="0" (
    echo [FAILED] DesktopMemoSetup.exe compilation failed.
    exit /b 1
)
echo [OK] Built "%~dp0DesktopMemoSetup.exe"
exit /b 0
