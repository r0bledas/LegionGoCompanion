@echo off
title Legion Go Input & Polling Tester
cd /d "%~dp0"
"C:\Users\LLG\dotnet\dotnet.exe" exec "%~dp0bin\Release\net10.0-windows10.0.19041.0\LegionInputTester.dll"
