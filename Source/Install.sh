FOLDERS_TO_COPY=("About" "Assemblies" "Defs" "Languages" "Patches" "Sounds" "Textures")
SOLUTION_DIR="$2"
TARGET_DIR="/Users/ap/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods/Zombieland/"

PDB2MDB_PATH="/Library/Frameworks/Mono.framework/Versions/2.11.4/bin/pdb2mdb"

HARMONY_PATH="${SOLUTION_DIR}Assemblies/0Harmony.dll"
MOD_DLL_PATH="${SOLUTION_DIR}Assemblies/$3"

if [ $1 == "Debug" ]
then
	echo "Creating mdb files"
	$PDB2MDB_PATH "$HARMONY_PATH"
	$PDB2MDB_PATH "$MOD_DLL_PATH"
else
	echo "Deleting mdb files"
	rm -f "${HARMONY_PATH}.mdb"
	rm -f "${MOD_DLL_PATH}.mdb"
fi

mkdir -p "$TARGET_DIR"
for folder in "${FOLDERS_TO_COPY[@]}"
do
	cp -R "${SOLUTION_DIR}${folder}" "${TARGET_DIR}"
done
