@echo off
setlocal EnableDelayedExpansion

rem Build a portable FFIXSaveEditor.exe. Run this from Windows (see
rem README.md in this folder for why it can't be cross-compiled from Linux).
rem
rem Everything printed here is also saved to build.log next to this script,
rem and this window stays open (press a key to close it) whether the build
rem succeeds or fails, so a failure is never just a window that vanished.

cd /d "%~dp0"
set LOGFILE=%~dp0build.log
echo Build started %date% %time% > "%LOGFILE%"

call :main
set RESULT=%ERRORLEVEL%
echo.
if %RESULT% neq 0 (
    echo ============================================================
    echo BUILD FAILED. Full log saved to: %LOGFILE%
    echo Scroll up to see the error, or open build.log in a text editor.
    echo ============================================================
) else (
    echo ============================================================
    echo BUILD SUCCEEDED.
    echo Portable exe: %~dp0dist\FFIXSaveEditor.exe
    echo You can copy just that one file anywhere - nothing else needed.
    echo ============================================================
)
echo.
pause
exit /b %RESULT%

:main
echo === Checking project layout ===
if not exist "..\ffix_save_gui.py" (
    echo.
    echo This "windows" folder is missing its sibling files - PyInstaller needs
    echo ..\ffix_save_gui.py, ..\ffix_save_tool.py, ..\ffix_save_data.py, and
    echo ..\ffix_save_memoria.py, which aren't here:
    echo     %~dp0..\ffix_save_gui.py
    echo.
    echo This almost always means only the "windows" folder was copied to this
    echo machine on its own. Go back and copy the ENTIRE project folder here
    echo instead ^(the one that directly contains ffix_save_gui.py, with
    echo "windows" as a subfolder of it^), then run build.bat from inside
    echo windows\ again.
    exit /b 1
)

echo === Locating Python ===
set PYCMD=
where py >nul 2>&1
if %ERRORLEVEL% equ 0 (
    py -3 --version >>"%LOGFILE%" 2>&1
    if !ERRORLEVEL! equ 0 set PYCMD=py -3
)
if "!PYCMD!"=="" (
    where python >nul 2>&1
    if !ERRORLEVEL! equ 0 (
        python --version >>"%LOGFILE%" 2>&1
        if !ERRORLEVEL! equ 0 set PYCMD=python
    )
)
if "!PYCMD!"=="" (
    echo Python was not found on PATH ^(tried "py -3" and "python"^).
    echo Install Python 3.10+ from https://www.python.org/downloads/
    echo IMPORTANT: on the installer's first screen, check "Add python.exe to PATH".
    exit /b 1
)
echo Using: !PYCMD! & echo Using: !PYCMD! >> "%LOGFILE%"

echo === Creating build virtual environment ===
if exist .buildenv (
    echo Reusing existing .buildenv ^(delete this folder to start fresh^)
) else (
    !PYCMD! -m venv .buildenv >>"%LOGFILE%" 2>&1
    if !ERRORLEVEL! neq 0 (
        echo Failed to create a virtual environment in .buildenv - see %LOGFILE%
        exit /b 1
    )
)

if not exist .buildenv\Scripts\activate.bat (
    echo .buildenv\Scripts\activate.bat is missing - the venv wasn't created correctly.
    echo Delete the .buildenv folder and try again, and check %LOGFILE% for details.
    exit /b 1
)
call .buildenv\Scripts\activate.bat

echo === Installing build dependencies ^(this can take a minute^) ===
python -m pip install --upgrade pip >>"%LOGFILE%" 2>&1
python -m pip install pyinstaller pycryptodome pyside6-essentials >>"%LOGFILE%" 2>&1
if !ERRORLEVEL! neq 0 (
    echo Failed to install dependencies - see %LOGFILE%
    echo Common causes: no internet access, or a corporate proxy/firewall blocking pypi.org.
    exit /b 1
)

echo === Building FFIXSaveEditor.exe ^(this can take a minute^) ===
python -m PyInstaller --noconfirm --clean FFIXSaveEditor.spec >>"%LOGFILE%" 2>&1
if !ERRORLEVEL! neq 0 (
    echo PyInstaller failed - see %LOGFILE% for the actual error.
    echo A common cause on Windows is antivirus/SmartScreen quarantining the
    echo PyInstaller bootloader mid-build ^(it's a generic, unsigned exe, which
    echo some antivirus products flag on sight^) - check your antivirus's
    echo quarantine/history if the log doesn't point to an obvious cause, and
    echo add an exclusion for this folder if so.
    exit /b 1
)

if not exist dist\FFIXSaveEditor.exe (
    echo PyInstaller reported success but dist\FFIXSaveEditor.exe is missing - see %LOGFILE%
    exit /b 1
)
exit /b 0
