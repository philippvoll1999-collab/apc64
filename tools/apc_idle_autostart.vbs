' Startet den APC64-Idle-Waechter ohne sichtbares Fenster (fuer den Windows-Autostart).
CreateObject("WScript.Shell").Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""C:\DEV\APC64\tools\apc_idle_service.ps1""", 0, False
