@echo off
rem Builds VirtualDisplayViewer.exe into the app\ folder
cd /d "%~dp0src\VirtualDisplayViewer"
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o "%~dp0app"
