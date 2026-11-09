; Per-user installer for Dareu Mouse. No admin rights: installs to
; %LOCALAPPDATA%\Programs\Dareu Mouse and registers under HKCU.
; Build after `dotnet publish src/DareuMouse -c Release -o dist`:
;   makensis installer/DareuMouse.nsi

Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"

!define APP "Dareu Mouse"
!define EXE "DareuMouse.exe"
!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DareuMouse"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"

Name "${APP}"
OutFile "..\dist\DareuMouse-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\${APP}"
InstallDirRegKey HKCU "${UNINST_KEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
ManifestDPIAware true

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APP}"
VIAddVersionKey "FileDescription" "${APP} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "coricogit"

!define MUI_ICON "..\src\DareuMouse\mouse.ico"
!define MUI_UNICON "..\src\DareuMouse\mouse.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Spanish"
!insertmacro MUI_LANGUAGE "English"

LangString SecApp ${LANG_SPANISH} "Dareu Mouse"
LangString SecApp ${LANG_ENGLISH} "Dareu Mouse"
LangString SecDesktop ${LANG_SPANISH} "Ícono en el escritorio"
LangString SecDesktop ${LANG_ENGLISH} "Desktop icon"
LangString SecStartup ${LANG_SPANISH} "Iniciar con Windows"
LangString SecStartup ${LANG_ENGLISH} "Start with Windows"

LangString DescApp ${LANG_SPANISH} "La app de la bandeja del sistema y su acceso en el menú Inicio."
LangString DescApp ${LANG_ENGLISH} "The tray app and its Start menu shortcut."
LangString DescDesktop ${LANG_SPANISH} "Un acceso directo en el escritorio."
LangString DescDesktop ${LANG_ENGLISH} "A shortcut on the desktop."
LangString DescStartup ${LANG_SPANISH} "Abre la app en la bandeja al iniciar sesión. Se puede cambiar después desde el panel."
LangString DescStartup ${LANG_ENGLISH} "Opens the app in the tray when you sign in. You can change it later from the panel."

; Close a running copy so its exe can be replaced or removed.
!macro CloseApp
  nsExec::Exec 'taskkill /IM "${EXE}" /F'
  Pop $0
  Sleep 500
!macroend

Section "!$(SecApp)" SecMain
  SectionIn RO
  !insertmacro CloseApp
  SetOutPath "$INSTDIR"
  File "..\dist\${EXE}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\${APP}.lnk" "$INSTDIR\${EXE}"

  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "coricogit"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout" "https://github.com/coricogit/corico-mouse-windowss"
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" $0
SectionEnd

Section "$(SecDesktop)" SecDesk
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\${EXE}"
SectionEnd

; Same value the app's own "Start with Windows" checkbox writes.
Section "$(SecStartup)" SecRun
  WriteRegStr HKCU "${RUN_KEY}" "DareuMouse" '"$INSTDIR\${EXE}" --tray'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} $(DescApp)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesk} $(DescDesktop)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecRun} $(DescStartup)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  !insertmacro CloseApp
  Delete "$INSTDIR\${EXE}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APP}.lnk"
  Delete "$DESKTOP\${APP}.lnk"
  DeleteRegValue HKCU "${RUN_KEY}" "DareuMouse"
  DeleteRegKey HKCU "${UNINST_KEY}"
SectionEnd
