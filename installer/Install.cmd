@echo off
setlocal
:: Relaunch as administrator if needed
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
cd /d "%~dp0"

echo [1/4] Installing test certificate...
certutil -addstore -f Root DFRDisplayKm-TestCert.cer || goto :fail
certutil -addstore -f TrustedPublisher DFRDisplayKm-TestCert.cer || goto :fail

echo [2/4] Enabling test signing mode (needs restart)...
bcdedit /set testsigning on || goto :fail

rem pnputil exit codes: 259 = already installed / nothing to update, 3010 = restart required (both are fine)
echo [3/4] Installing driver package (USB composite, then display)...
pnputil /add-driver "package\DFRUsbCcgp.inf" /install
if %errorlevel% neq 0 if %errorlevel% neq 259 if %errorlevel% neq 3010 goto :fail
pnputil /add-driver "package\DFRDisplayKm.inf" /install
if %errorlevel% neq 0 if %errorlevel% neq 259 if %errorlevel% neq 3010 goto :fail

echo [4/4] Done.
echo.
echo Restart Windows now. Secure Boot must be OFF (see README).
echo After restart, the Touch Bar display should appear as "iBridge Display".
pause
exit /b 0

:fail
echo.
echo Installation failed. See the messages above.
pause
exit /b 1
