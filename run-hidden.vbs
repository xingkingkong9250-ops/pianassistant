' run-hidden.vbs — launches run.ps1 without a console window.
' Invoked by run.bat via wscript //B.
Option Explicit
Dim shell, args, scriptPath
Set args = WScript.Arguments
If args.Count < 1 Then
    WScript.Echo "Usage: cscript //B run-hidden.vbs <run.ps1>"
    WScript.Quit 1
End If
scriptPath = args.Item(0)
Set shell = CreateObject("WScript.Shell")
shell.Run "powershell -NoProfile -ExecutionPolicy Bypass -File """ & scriptPath & """", 0, False
