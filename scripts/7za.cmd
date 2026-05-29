@echo off
setlocal enabledelayedexpansion
set "_args="
for %%a in (%*) do (
    if not "%%a"=="-snl" (
        if not "%%a"=="-snld" (
            set "_args=!_args! %%a"
        )
    )
)
"D:\SoloProject\LCSync\node_modules\7zip-bin\win\x64\7za.exe" !_args!
exit /b 0
