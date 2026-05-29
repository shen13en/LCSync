@echo off
set ELECTRON_MIRROR=https://npmmirror.com/mirrors/electron/
set CSC_IDENTITY_AUTO_DISCOVERY=false
set USE_SYSTEM_7ZA=true
set PATH=%~dp0;%PATH%
npx electron-builder --win --x64
