@ECHO ON
SETLOCAL

REM Configure these to match your mod
REM -----------------------------------------------------
SET MOD_FOLDERNAME=Zombieland
SET FOLDERS_TO_COPY=(About Assemblies Defs Languages Patches Sounds Textures)
REM -----------------------------------------------------

SET SOLUTION_DIR=%~2
SET TARGET_DIR=%RIMWORLD_DIR_STEAM%\Mods\%MOD_FOLDERNAME%
SET TARGET_DEBUG_DIR=%RIMWORLD_DIR_STANDALONE%\Mods\%MOD_FOLDERNAME%

SET HARMONY_PATH=%SOLUTION_DIR%Assemblies\0Harmony.dll
SET MOD_DLL_PATH=%SOLUTION_DIR%Assemblies\%~3

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
	FOR %%D IN %FOLDERS_TO_COPY% DO (
		XCOPY /Q /I /Y /E "%SOLUTION_DIR%%%D" "%TARGET_DEBUG_DIR%\%%D"
	)
)

IF EXIST "%RIMWORLD_DIR_STEAM%" (
	ECHO "Copying to %TARGET_DIR%"
	IF NOT EXIST "%TARGET_DIR%" MKDIR "%TARGET_DIR%"
	FOR %%D IN %FOLDERS_TO_COPY% DO (
		XCOPY /Q /I /Y /E "%SOLUTION_DIR%%%D" "%TARGET_DIR%\%%D"
	)
)