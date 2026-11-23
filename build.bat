@echo off
rem build.bat - builds VirtualDisplayViewer.exe into the app\ folder. Needs the .NET 10 SDK.
rem Work from the viewer's source folder, wherever this script is run from:
cd /d "%~dp0src\VirtualDisplayViewer"
rem Compile one framework-dependent exe (needs the .NET 10 Desktop Runtime) into ..\..\app:
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o "%~dp0app"
