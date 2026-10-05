Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!ifdef CLIENT_ONLY
  !define PRODUCT "A K Reporting Client"
  !define FOLDER "AK Diagnostic Reporting Client"
  !define SETUPFILE "AK-Reporting-Client-${VERSION}-win-x64.exe"
!else
  !define PRODUCT "A K Diagnostic Reporting"
  !define FOLDER "AK Diagnostic Reporting"
  !define SETUPFILE "AK-Reporting-Setup-${VERSION}-win-x64.exe"
!endif
Name "${PRODUCT} ${VERSION} (engineering test)"
OutFile "${OUTPUT}\${SETUPFILE}"
InstallDir "$PROGRAMFILES64\${FOLDER}"
InstallDirRegKey HKLM "Software\AKReporting\${FOLDER}" "InstallPath"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${PRODUCT}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "Offline Windows engineering-validation installer"
VIAddVersionKey "LegalCopyright" "A K Diagnostic Centre & Polyclinic"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Install the offline reporting application for Windows 10/11 x64.$\r$\n$\r$\nThis is an engineering test build. Clinical report configurations remain drafts pending qualified center review.$\r$\n$\r$\nSaved report data is preserved when uninstalling."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!ifndef CLIENT_ONLY
  !define MUI_FINISHPAGE_RUN "$INSTDIR\setup\AkReporting.WindowsSetup.exe"
  !define MUI_FINISHPAGE_RUN_TEXT "Run first-time setup / repair local services"
!endif
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  SetRegView 64
  SetShellVarContext all
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "Use this package on a 64-bit Windows 10/11 PC."
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "This local installation package requires Windows 10 or 11. The net48 client compatibility work for Windows 7 remains separate."
    Abort
  ${EndIf}
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 528040
    MessageBox MB_OK|MB_ICONSTOP ".NET Framework 4.8 or newer 4.x is required for the reporting client. Install it from Microsoft before running setup."
    Abort
  ${EndIf}
FunctionEnd

Section "Install"
  SetShellVarContext all
  SetRegView 64
!ifndef CLIENT_ONLY
  IfFileExists "$INSTDIR\setup\AkReporting.WindowsSetup.exe" 0 services_stopped
    ExecWait '"$INSTDIR\setup\AkReporting.WindowsSetup.exe" --stop-services' $0
    ${If} $0 != 0
      MessageBox MB_OK|MB_ICONSTOP "Could not stop the existing reporting services. No application files will be replaced."
      Abort
    ${EndIf}
  services_stopped:
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "${PAYLOAD}\prerequisites\VC_redist.x64.exe"
  ExecWait '"$PLUGINSDIR\VC_redist.x64.exe" /install /quiet /norestart' $0
  ${If} $0 == 3010
    SetRebootFlag true
  ${ElseIf} $0 == 1638
    ; A newer compatible redistributable can already be present (e.g. Visual Studio).
    ReadRegDWORD $1 HKLM "SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64" "Installed"
    ${If} $1 != 1
      MessageBox MB_OK|MB_ICONSTOP "A conflicting Visual C++ prerequisite was detected. Resolve it and retry setup."
      Abort
    ${EndIf}
  ${ElseIf} $0 != 0
    MessageBox MB_OK|MB_ICONSTOP "The Microsoft Visual C++ prerequisite could not install (code $0). Resolve the prerequisite and retry setup."
    Abort
  ${EndIf}
!endif
  SetOutPath "$INSTDIR\client"
  File /r "${PAYLOAD}\client\*.*"
  SetOutPath "$INSTDIR\docs"
  File /r "${PAYLOAD}\docs\*.*"
!ifndef CLIENT_ONLY
  SetOutPath "$INSTDIR\host"
  File /r "${PAYLOAD}\host\*.*"
  SetOutPath "$INSTDIR\setup"
  File /r "${PAYLOAD}\setup\*.*"
  SetOutPath "$INSTDIR\postgres"
  File /r "${PAYLOAD}\postgres\*.*"
  SetOutPath "$INSTDIR"
  File "${PAYLOAD}\package-manifest.json"
!endif
  CreateDirectory "$SMPROGRAMS\${PRODUCT}"
  CreateShortcut "$SMPROGRAMS\${PRODUCT}\Reporting Client.lnk" "$INSTDIR\client\AkReporting.Desktop.exe"
  CreateShortcut "$DESKTOP\${PRODUCT}.lnk" "$INSTDIR\client\AkReporting.Desktop.exe"
  CreateShortcut "$SMPROGRAMS\${PRODUCT}\Read First.lnk" "$INSTDIR\docs\README-FIRST.txt"
!ifndef CLIENT_ONLY
  CreateShortcut "$SMPROGRAMS\${PRODUCT}\Local Setup and Repair.lnk" "$INSTDIR\setup\AkReporting.WindowsSetup.exe"
!endif
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\AKReporting\${FOLDER}" "InstallPath" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${FOLDER}" "DisplayName" "${PRODUCT} (engineering test)"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${FOLDER}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${FOLDER}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${FOLDER}" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${FOLDER}" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${FOLDER}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  SetRegView 64
!ifndef CLIENT_ONLY
  IfFileExists "$INSTDIR\setup\AkReporting.WindowsSetup.exe" 0 missing_setup
    ExecWait '"$INSTDIR\setup\AkReporting.WindowsSetup.exe" --remove-services' $0
    ${If} $0 != 0
      MessageBox MB_OK|MB_ICONSTOP "Could not remove this installation's services. Application files and saved data are preserved. Repair setup and retry uninstall."
      Abort
    ${EndIf}
    Goto services_removed
  missing_setup:
    MessageBox MB_OK|MB_ICONSTOP "The setup utility is missing. Reinstall the package to repair it before uninstalling."
    Abort
  services_removed:
!endif
  !include "${UNINSTALL_LIST}"
  Delete "$DESKTOP\${PRODUCT}.lnk"
  Delete "$SMPROGRAMS\${PRODUCT}\Reporting Client.lnk"
  Delete "$SMPROGRAMS\${PRODUCT}\Read First.lnk"
  Delete "$SMPROGRAMS\${PRODUCT}\Local Setup and Repair.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT}"
  DeleteRegKey HKLM "Software\AKReporting\${FOLDER}"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${FOLDER}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  ; Never delete ProgramData, PostgreSQL data, exports or backups.
SectionEnd
