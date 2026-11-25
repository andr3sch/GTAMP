# Cria atalhos na Area de Trabalho para o GTAMP Launcher e GTAMP Server (com o icone oficial)
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$desktop = [Environment]::GetFolderPath("Desktop")
$wsh = New-Object -ComObject WScript.Shell
$icoPath = Join-Path $root "docs\assets\gtamp.ico"

$launcherExe = Join-Path $root "src\GTAMP.Launcher\bin\Release\net8.0-windows\GTAMP.Launcher.exe"
if (Test-Path $launcherExe) {
    $s1 = $wsh.CreateShortcut((Join-Path $desktop "GTAMP Launcher.lnk"))
    $s1.TargetPath = $launcherExe
    $s1.WorkingDirectory = Split-Path -Parent $launcherExe
    $s1.IconLocation = if (Test-Path $icoPath) { "$icoPath,0" } else { "$launcherExe,0" }
    $s1.Description = "Iniciar GTAMP Launcher (GTA V Lighter Multiplayer)"
    $s1.Save()
    Write-Host "[OK] Atalho criado: GTAMP Launcher.lnk" -ForegroundColor Green
}

$serverExe = Join-Path $root "src\GTAMP.Server\bin\Release\net8.0\GTAMP.Server.exe"
if (Test-Path $serverExe) {
    $s2 = $wsh.CreateShortcut((Join-Path $desktop "GTAMP Server.lnk"))
    $s2.TargetPath = $serverExe
    $s2.WorkingDirectory = Split-Path -Parent $serverExe
    $s2.IconLocation = if (Test-Path $icoPath) { "$icoPath,0" } else { "$serverExe,0" }
    $s2.Description = "Iniciar GTAMP Dedicated Server"
    $s2.Save()
    Write-Host "[OK] Atalho criado: GTAMP Server.lnk" -ForegroundColor Green
}
