@echo off
cd /d "%~dp0"
if not exist node_modules (call npm install)
echo Starting Tag Game server... close this window to stop it.
node server.js
pause
