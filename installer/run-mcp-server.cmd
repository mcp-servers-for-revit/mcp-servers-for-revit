@echo off
rem stdio launcher for the bundled mcp-server-for-revit build.
rem Prefers the portable Node shipped by this installer ({app}\node),
rem falls back to whatever "node" resolves in PATH.
set NODE=%~dp0..\node\node.exe
if not exist "%NODE%" set NODE=node
"%NODE%" "%~dp0build\index.js" %*
