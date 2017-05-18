@ECHO ON
SETLOCAL

SET FOLDERS_TO_COPY=(About Assemblies Defs Languages Sounds Textures)
SET SOLUTION_DIR=%~2
SET TARGET_DIR=D:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\Zombieland\

SET MONO_EXE="C:\Program Files (x86)\Mono-4\bin\mono.exe"
SET PDB2MDB_PATH="C:\Program Files (x86)\Mono-4\lib\mono\4.5\pdb2mdb.exe"

SET HARMONY_PATH="%SOLUTION_DIR%2Assemblies\0Harmony.dll"
SET MOD_DLL_PATH="%SOLUTION_DIR%Assemblies\%~3"

IF %1==Debug (

	echo Creating mdb files
	%MONO_EXE% %PDB2MDB_PATH% %HARMONY_PATH%
	%MONO_EXE% %PDB2MDB_PATH% %MOD_DLL_PATH%
)

IF %1==Release (

	echo Deleting mdb files
	IF EXIST "%HARMONY_PATH%.mdb" DEL "%HARMONY_PATH%.mdb"
	IF EXIST "%MOD_DLL_PATH%.mdb" DEL "%MOD_DLL_PATH%.mdb"
)

IF NOT EXIST "%TARGET_DIR%" MKDIR "%TARGET_DIR%"
FOR %%D IN %FOLDERS_TO_COPY% DO (
	XCOPY /Q /I /Y /E "%SOLUTION_DIR%%%D" "%TARGET_DIR%%%D"
)
