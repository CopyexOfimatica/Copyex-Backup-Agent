Unicode True
!include "MUI2.nsh"

!define APPNAME "Copyex Backup Agent"
!define EXEFILE "CopyexBackupAgent.exe"
!define REGKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\CopyexBackupAgent"
; Debe coincidir con AssemblyInformationalVersion de Properties\AssemblyInfo.cs
!define VERSION "1.0.0"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1034 "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=1034 "CompanyName" "Copyex Ofimática S.L."
VIAddVersionKey /LANG=1034 "LegalCopyright" "Copyright © 2026 Copyex Ofimática S.L. Licencia AGPL-3.0"
VIAddVersionKey /LANG=1034 "LegalTrademarks" "Copyex es una marca de Copyex Ofimática S.L."
VIAddVersionKey /LANG=1034 "FileDescription" "Instalador de ${APPNAME}"
VIAddVersionKey /LANG=1034 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1034 "ProductVersion" "${VERSION}"

Name "${APPNAME}"
Caption "${APPNAME}"
BrandingText "Copyex · Si copias, ¡copia bien!"
OutFile "CopyexBackupAgent_Setup.exe"
InstallDir "$PROGRAMFILES64\Copyex\CopyexBackupAgent"
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!define MUI_ICON "Assets\CopyexBackupAgent.ico"
!define MUI_UNICON "Assets\CopyexBackupAgent.ico"
!define MUI_ABORTWARNING
; El instalador corre como administrador: si abriera el programa directamente, este heredaría esos
; permisos y la tarea programada quedaría creada como administrador (luego no se podría cambiar).
; Abriéndolo a través de explorer.exe se ejecuta como el usuario normal.
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Abrir Copyex Backup Agent"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchAsUser

!define MUI_WELCOMEPAGE_TEXT "Este asistente instalará ${APPNAME} ${VERSION}.$\r$\n$\r$\nDesarrollado por Copyex Ofimática S.L.$\r$\n$\r$\nPulse Siguiente para continuar."
!insertmacro MUI_PAGE_WELCOME
; Licencia AGPL: es informativa, no hace falta aceptarla para usar el programa.
!define MUI_LICENSEPAGE_TEXT_TOP "Copyex Backup Agent es software libre bajo la licencia GNU AGPL v3."
!define MUI_LICENSEPAGE_TEXT_BOTTOM "Se distribuye sin ninguna garantía. Pulse Siguiente para continuar."
!define MUI_LICENSEPAGE_BUTTON "$(^NextBtn)"
!insertmacro MUI_PAGE_LICENSE "LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Spanish"

Function LaunchAsUser
    Exec '"$WINDIR\explorer.exe" "$INSTDIR\${EXEFILE}"'
FunctionEnd

Section "Copyex Backup Agent"
    ; Cierra la versión anterior si está abierta (por ejemplo, en la bandeja).
    nsExec::Exec 'taskkill /IM ${EXEFILE} /F'
    Sleep 800

    SetOutPath "$INSTDIR"
    File "bin\CopyexBackupAgent.exe"
    File "bin\CopyexBackupAgent.exe.config"

    ; Soporte de rutas largas de Windows (global del sistema; no se revierte al desinstalar).
    WriteRegDWORD HKLM "SYSTEM\CurrentControlSet\Control\FileSystem" "LongPathsEnabled" 1

    WriteUninstaller "$INSTDIR\Desinstalar.exe"

    CreateDirectory "$SMPROGRAMS\Copyex"
    CreateShortcut "$SMPROGRAMS\Copyex\Copyex Backup Agent.lnk" "$INSTDIR\${EXEFILE}"
    CreateShortcut "$DESKTOP\Copyex Backup Agent.lnk" "$INSTDIR\${EXEFILE}"

    WriteRegStr HKLM "${REGKEY}" "DisplayName" "${APPNAME}"
    WriteRegStr HKLM "${REGKEY}" "Publisher" "Copyex Ofimática S.L."
    WriteRegStr HKLM "${REGKEY}" "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "${REGKEY}" "DisplayIcon" "$INSTDIR\${EXEFILE}"
    WriteRegStr HKLM "${REGKEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "${REGKEY}" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
    WriteRegDWORD HKLM "${REGKEY}" "NoModify" 1
    WriteRegDWORD HKLM "${REGKEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
    nsExec::Exec 'taskkill /IM ${EXEFILE} /F'
    nsExec::Exec 'schtasks /Delete /TN "CopyexBackupAgent_Backup" /F'
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "CopyexBackupAgent"

    Delete "$SMPROGRAMS\Copyex\Copyex Backup Agent.lnk"
    RMDir "$SMPROGRAMS\Copyex"
    Delete "$DESKTOP\Copyex Backup Agent.lnk"
    DeleteRegKey HKLM "${REGKEY}"

    Delete "$INSTDIR\${EXEFILE}"
    Delete "$INSTDIR\CopyexBackupAgent.exe.config"

    MessageBox MB_YESNO|MB_ICONQUESTION "¿Eliminar también la configuración y el historial?$\r$\n(Las copias guardadas en el NAS no se tocan.)" IDNO KeepConfig
    RMDir /r "$APPDATA\CopyexBackupAgent"
KeepConfig:
    Delete "$INSTDIR\Desinstalar.exe"
    RMDir "$INSTDIR"
    RMDir "$PROGRAMFILES64\Copyex"
SectionEnd
