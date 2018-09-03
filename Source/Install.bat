REM ################ Mod build and install script (c) Andreas Pardeike 2018 ################
REM
REM Call this script from Visual Studio's Build Events post-build event command line box:
REM "$(ProjectDir)Install.bat" $(ConfigurationName) "$(ProjectDir)" "$(ProjectName)" "About Assemblies Languages Textures"
REM
REM The project structure should look like this:
REM
REM ProjectFolder
REM +- About
REM +- Assemblies
REM |  +- 0Harmony.dll
REM |  +- 0Harmony.dll.mbd (optional for debug build)
REM |  +- 0Harmony.pdb (optional for debug build)
REM |  +- Modname.dll
REM |  +- Modname.dll.mbd (optional for debug build)
REM |  +- Modname.pdb (optional for debug build)
REM +- Languages
REM +- Source
REM |  +- Modname
REM |  |  +- Modname.csproj
REM |  |  +- Modname.csproj.user
REM |  |  +- Install.bat
REM |  |  +- Install.sh
REM +- Textures
REM +- .gitattributes
REM +- .gitignore
REM +- LICENSE
REM +- README.md
REM +- Modname.sln
REM
REM Also needed are the following environment variables in the system settings (example values):
REM
REM MONO_EXE = C:\Program Files (x86)\Mono-4\bin\mono.exe
REM PDB2MDB_PATH = C:\Program Files (x86)\Mono-4\lib\mono\4.5\pdb2mdb.exe
REM RIMWORLD_DIR_STEAM = C:\Program Files (x86)\Steam\steamapps\common\RimWorld
REM RIMWORLD_DIR_STANDALONE = C:\Program Files (x86)\RimWorld1722Win
REM RIMWORLD_MOD_DEBUG = --debugger-agent=transport=dt_socket,address=127.0.0.1:56000,server=y
REM
REM Finally, configure Visual Studio's Debug configuration with the rimworld exe as an external
REM program and set the working directory to the directory containing the exe.
REM
REM To debug, build the project (this script will install the mod), then run "Debug" (F5) which
REM will start RimWorld in paused state. Finally, choose "Debug -> Attach Unity Debugger" and
REM press "Input IP" and accept the default 127.0.0.1 : 56000

@ECHO ON
SETLOCAL ENABLEDELAYEDEXPANSION

SET SOLUTION_DIR=%~2
SET SOLUTION_DIR=%SOLUTION_DIR:~0,-7%
SET TARGET_DIR=%RIMWORLD_DIR_STEAM%\Mods\%~3
SET TARGET_DEBUG_DIR=%RIMWORLD_DIR_STANDALONE%\Mods\%~3
SET ZIP_EXE="C:\Program Files\7-Zip\7z.exe"

SET HARMONY_PATH=%SOLUTION_DIR%Assemblies\0Harmony.dll
SET MOD_DLL_PATH=%SOLUTION_DIR%Assemblies\%~3.dll

ECHO # Preprocessing

IF %1==Debug (

	IF EXIST "%HARMONY_PATH:~0,-4%.pdb" (
		ECHO "Creating mdb for %HARMONY_PATH%"
		"%MONO_EXE%" "%PDB2MDB_PATH%" "%HARMONY_PATH%"
	)
	IF EXIST "%MOD_DLL_PATH:~0,-4%.pdb" (
		ECHO "Creating mdb for %MOD_DLL_PATH%"
		"%MONO_EXE%" "%PDB2MDB_PATH%" "%MOD_DLL_PATH%"
	)
)

IF %1==Release (

	IF EXIST "%HARMONY_PATH%.mdb" (
		ECHO "Deleting %HARMONY_PATH%.mdb"
		DEL "%HARMONY_PATH%.mdb"
	)
	IF EXIST "%MOD_DLL_PATH%.mdb" (
		ECHO "Deleting %MOD_DLL_PATH%.mdb"
		DEL "%MOD_DLL_PATH%.mdb"
	)
)

IF EXIST "%RIMWORLD_DIR_STANDALONE%" (
	ECHO "Copying to %TARGET_DEBUG_DIR%"
	IF NOT EXIST "%TARGET_DEBUG_DIR%" MKDIR "%TARGET_DEBUG_DIR%"
	FOR %%D IN (%~4) DO (
		XCOPY /Q /I /Y /E "%SOLUTION_DIR%%%D" "%TARGET_DEBUG_DIR%\%%D"
	)
)

IF EXIST "%RIMWORLD_DIR_STEAM%" (
	ECHO "Copying to %TARGET_DIR%"
	IF NOT EXIST "%TARGET_DIR%" MKDIR "%TARGET_DIR%"
	FOR %%D IN (%~4) DO (
		XCOPY /Q /I /Y /E "%SOLUTION_DIR%%%D" "%TARGET_DIR%\%%D"
	)
	%ZIP_EXE% a "%TARGET_DIR%.zip" "%TARGET_DIR%"
)
