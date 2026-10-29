; NSIS installer for QEMU Explorer.
;
;   makensis -DVERSION=1.2.3 -DARCH=x64 -DSOURCE=publish\win-x64 -DOUTFILE=dist\QemuXplorer-setup.exe tools\windows-installer.nsi
;
; ARCH is x64 or x86. The x64 installer refuses to run on 32-bit Windows and installs to
; Program Files; the x86 one installs to Program Files (x86) on 64-bit Windows.

!ifndef VERSION
  !error "pass -DVERSION=x.y.z"
!endif
!ifndef ARCH
  !error "pass -DARCH=x64 or -DARCH=x86"
!endif
!ifndef SOURCE
  !error "pass -DSOURCE=<publish folder>"
!endif
!ifndef OUTFILE
  !define OUTFILE "QemuXplorer-setup.exe"
!endif

!define APPNAME "QEMU Explorer"
!define EXENAME "QemuXplorer.exe"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\QemuXplorer-${ARCH}"

Unicode true
Name "${APPNAME}"
OutFile "${OUTFILE}"
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!if "${ARCH}" == "x64"
  InstallDir "$PROGRAMFILES64\${APPNAME}"
!else
  InstallDir "$PROGRAMFILES\${APPNAME}"
!endif
InstallDirRegKey HKLM "${UNINSTKEY}" "InstallLocation"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} installer (${ARCH})"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"

!include "MUI2.nsh"
!include "x64.nsh"

!define MUI_ICON "..\src\QemuXplorer.App\Assets\icon.ico"
!define MUI_UNICON "..\src\QemuXplorer.App\Assets\icon.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
!if "${ARCH}" == "x64"
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "This installer is for 64-bit Windows. Use the x86 installer instead."
    Abort
  ${EndIf}
  SetRegView 64
!endif
FunctionEnd

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "${SOURCE}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr HKLM "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXENAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
!if "${ARCH}" == "x64"
  SetRegView 64
!endif
  RMDir /r "$SMPROGRAMS\${APPNAME}"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "${UNINSTKEY}"
SectionEnd
