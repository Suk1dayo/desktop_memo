@echo off
setlocal
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "REF_ROOT=C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found at %CSC%
    exit /b 1
)
echo Compiling DesktopMemo.exe ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /utf8output ^
  /win32manifest:"%~dp0src\app.manifest" ^
  /win32icon:"%~dp0src\app.ico" ^
  /r:"%REF_ROOT%\WPF\PresentationCore.dll" ^
  /r:"%REF_ROOT%\WPF\PresentationFramework.dll" ^
  /r:"%REF_ROOT%\WPF\WindowsBase.dll" ^
  /r:"%REF_ROOT%\System.Xaml.dll" ^
  /r:"%REF_ROOT%\System.Windows.Forms.dll" ^
  /r:"%REF_ROOT%\System.Drawing.dll" ^
  /r:"%REF_ROOT%\System.Net.Http.dll" ^
  /r:"%REF_ROOT%\System.Web.Extensions.dll" ^
  /r:"%REF_ROOT%\System.Runtime.Serialization.dll" ^
  /out:"%~dp0DesktopMemo.exe" ^
  "%~dp0src\*.cs"
if errorlevel 1 (
    echo [FAILED] DesktopMemo.exe compilation failed.
    exit /b 1
)
echo [OK] Built "%~dp0DesktopMemo.exe"

echo Compiling DesktopMemoServer.exe ...
"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /codepage:65001 /utf8output ^
  /win32icon:"%~dp0src\app.ico" ^
  /r:"%REF_ROOT%\System.Net.Http.dll" ^
  /r:"%REF_ROOT%\System.Web.Extensions.dll" ^
  /r:"%REF_ROOT%\System.Runtime.Serialization.dll" ^
  /out:"%~dp0DesktopMemoServer.exe" ^
  "%~dp0server_src\ServerProgram.cs" "%~dp0src\Models.cs" "%~dp0src\StorageService.cs" "%~dp0src\AiSortService.cs"
if errorlevel 1 (
    echo [FAILED] DesktopMemoServer.exe compilation failed.
    exit /b 1
)
echo [OK] Built "%~dp0DesktopMemoServer.exe"
exit /b 0
