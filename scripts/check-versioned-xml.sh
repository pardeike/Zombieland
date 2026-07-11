#!/usr/bin/env bash
set -euo pipefail

python3 - <<'PY'
from pathlib import Path
from collections import defaultdict
import subprocess
import sys
import xml.etree.ElementTree as ET

repo = Path(".")
load_folders = ET.parse(repo / "LoadFolders.xml").getroot()
runtime_roots = (
    "Assemblies",
    "Defs",
    "Languages",
    "Libraries",
    "Patches",
    "Resources",
    "Sounds",
    "Textures",
)
frozen_versions = {"1.4"}

errors = []
checked_versions = []
translation_sources = defaultdict(list)
keyed_keys = defaultdict(set)
languages_by_version = defaultdict(set)
def_sources = defaultdict(list)
defined_def_names = defaultdict(set)
defined_def_nodes = {}
direct_english_def_keys = defaultdict(set)
direct_english_thing_labels = {}
def_injection_entries = []
translation_values = {}

project_xml = [
    Path(path.decode())
    for path in subprocess.check_output(
        [
            "git",
            "ls-files",
            "-z",
            "--cached",
            "--others",
            "--exclude-standard",
            "--",
            "*.xml",
        ]
    ).split(b"\0")
    if path
]

for xml_path in project_xml:
    try:
        root = ET.parse(xml_path).getroot()
    except ET.ParseError as exc:
        errors.append(f"{xml_path}: XML parse error: {exc}")
        continue

    parts = xml_path.parts
    version = parts[0] if parts and parts[0] not in {"About", "Originals"} else None
    expected_root = None
    if len(parts) >= 2 and parts[1] == "Defs":
        expected_root = "Defs"
    elif len(parts) >= 2 and parts[1] == "Patches":
        expected_root = "Patch"
    elif "Languages" in parts:
        expected_root = "LanguageInfo" if xml_path.name == "LanguageInfo.xml" else "LanguageData"
    elif xml_path == repo / "LoadFolders.xml":
        expected_root = "loadFolders"
    elif xml_path == repo / "About/About.xml":
        expected_root = "ModMetaData"
    elif xml_path == repo / "About/Manifest.xml":
        expected_root = "Manifest"

    if expected_root is not None and root.tag != expected_root:
        errors.append(
            f"{xml_path}: expected root <{expected_root}>, found <{root.tag}>"
        )

    if version not in frozen_versions and expected_root is not None:
        direct_text = []
        if root.text and root.text.strip():
            direct_text.append(root.text.strip())
        direct_text.extend(
            child.tail.strip()
            for child in list(root)
            if child.tail and child.tail.strip()
        )
        if direct_text:
            preview = " | ".join(direct_text)[:160]
            errors.append(
                f"{xml_path}: unexpected text directly under <{root.tag}>: {preview!r}"
            )

    if root.tag == "LanguageData" and "Languages" in parts:
        language_index = parts.index("Languages")
        if len(parts) <= language_index + 2:
            errors.append(f"{xml_path}: incomplete language directory structure")
            continue

        language_version = parts[language_index - 1]
        language = parts[language_index + 1]
        section = parts[language_index + 2]
        if language_version in frozen_versions:
            continue

        languages_by_version[language_version].add(language)
        if section == "Keyed":
            scope = "Keyed"
            keyed_keys[(language_version, xml_path.name, language)].update(
                child.tag for child in list(root)
            )
        elif section == "DefInjected" and len(parts) > language_index + 3:
            def_type = parts[language_index + 3]
            scope = f"DefInjected/{def_type}"
        else:
            scope = section

        for child in list(root):
            if not list(child) and not (child.text or "").strip():
                errors.append(f"{xml_path}: empty translation element <{child.tag}>")
            translation_sources[
                (language_version, language, scope, child.tag)
            ].append(xml_path)
            translation_values[
                (language_version, language, scope, child.tag)
            ] = (child.text or "").strip()
            if section == "DefInjected" and len(parts) > language_index + 3:
                def_injection_entries.append(
                    (language_version, language, def_type, child.tag, xml_path)
                )

    if root.tag == "Defs" and version not in frozen_versions:
        for def_node in list(root):
            def_name = def_node.findtext("defName")
            if def_name:
                def_sources[(version, def_node.tag, "defName", def_name)].append(xml_path)
                defined_def_names[(version, def_node.tag)].add(def_name)
                defined_def_nodes[(version, def_node.tag, def_name)] = def_node
                for field_name in ("label", "description"):
                    field_value = def_node.findtext(field_name)
                    if field_value and field_value.strip():
                        direct_english_def_keys[(version, def_node.tag)].add(
                            f"{def_name}.{field_name}"
                        )
                        if (
                            def_node.tag == "ThingDef"
                            and field_name == "label"
                            and def_node.get("Abstract", "false").lower() != "true"
                        ):
                            direct_english_thing_labels[(version, def_name)] = field_value.strip()
            abstract_name = def_node.get("Name") or def_node.get("name")
            if abstract_name:
                def_sources[(version, def_node.tag, "Name", abstract_name)].append(xml_path)

for (version, language, scope, key), sources in sorted(translation_sources.items()):
    if len(sources) > 1:
        source_list = ", ".join(str(path) for path in sources)
        errors.append(
            f"{version}/{language}/{scope}: duplicate translation key {key}: {source_list}"
        )

for (version, def_type, name_kind, name), sources in sorted(def_sources.items()):
    if len(sources) > 1:
        source_list = ", ".join(str(path) for path in sources)
        errors.append(
            f"{version}/{def_type}: duplicate {name_kind} {name}: {source_list}"
        )

def english_def_path_exists(def_node, field_path):
    current = def_node
    for segment in field_path.split("."):
        if segment.isdigit():
            children = list(current)
            index = int(segment)
            if index >= len(children):
                return False
            current = children[index]
        else:
            current = current.find(segment)
            if current is None:
                return False
    return True


for version, language, def_type, key, source in sorted(def_injection_entries):
    def_name = key.split(".", 1)[0]
    if def_name not in defined_def_names[(version, def_type)]:
        errors.append(
            f"{source}: DefInjected key {key} does not target an English "
            f"{def_type} def in {version}/Defs"
        )
        continue
    field_path = key.split(".", 1)[1] if "." in key else ""
    def_node = defined_def_nodes[(version, def_type, def_name)]
    if not field_path or not english_def_path_exists(def_node, field_path):
        errors.append(
            f"{source}: DefInjected key {key} does not target an English "
            f"field path in {version}/Defs"
        )

for (version, def_type), expected_keys in sorted(direct_english_def_keys.items()):
    scope = f"DefInjected/{def_type}"
    for language in sorted(languages_by_version[version] - {"English"}):
        translated_keys = {
            key
            for candidate_version, candidate_language, candidate_scope, key in translation_sources
            if candidate_version == version
            and candidate_language == language
            and candidate_scope == scope
        }
        missing = sorted(expected_keys - translated_keys)
        if missing:
            errors.append(
                f"{version}/{language}/{scope}: missing English def keys "
                + ", ".join(missing)
            )

for version in sorted(languages_by_version):
    scope = "DefInjected/ThingDef"
    for language in sorted(languages_by_version[version]):
        labels = defaultdict(list)
        for (candidate_version, def_name), english_label in direct_english_thing_labels.items():
            if candidate_version != version:
                continue
            label = english_label
            if language != "English":
                label = translation_values.get(
                    (version, language, scope, f"{def_name}.label"),
                    english_label,
                )
            labels[label.casefold()].append(def_name)
        for label, def_names in sorted(labels.items()):
            if len(def_names) > 1:
                errors.append(
                    f"{version}/{language}/{scope}: duplicate active ThingDef label "
                    f"{label!r}: " + ", ".join(sorted(def_names))
                )

for (version, file_name, language), keys in sorted(keyed_keys.items()):
    if language != "English":
        continue
    for other_language in sorted(languages_by_version[version] - {"English"}):
        other_keys = keyed_keys.get((version, file_name, other_language), set())
        missing = sorted(keys - other_keys)
        extra = sorted(other_keys - keys)
        if missing or extra:
            details = []
            if missing:
                details.append("missing " + ", ".join(missing))
            if extra:
                details.append("extra " + ", ".join(extra))
            errors.append(
                f"{version}/{other_language}/Keyed/{file_name}: " + "; ".join(details)
            )

for folder in runtime_roots:
    if (repo / folder).exists():
        errors.append(
            f"{folder}: root runtime folder is forbidden; put active content under a version folder"
        )

for node in list(load_folders):
    tag = node.tag
    if tag.startswith("v"):
        version = tag[1:]
    elif tag == "default":
        continue
    else:
        version = tag

    entries = [
        (child.text or "").strip()
        for child in list(node)
        if child.tag == "li"
    ]
    version_dir = repo / version

    if not version_dir.exists():
        errors.append(f"{version}: LoadFolders entry exists but {version}/ is missing")
        continue

    checked_versions.append(version)

    if entries != [version]:
        errors.append(
            f"{version}: LoadFolders must be isolated and list only {version}; got {entries}"
        )

if not checked_versions:
    errors.append("No version folders checked from LoadFolders.xml")

if errors:
    print("Versioned XML layout check failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    sys.exit(1)

print(
    f"Project XML OK ({len(project_xml)} files); versioned runtime layout OK: "
    + ", ".join(checked_versions)
    + " are isolated and no root runtime folders exist"
)
PY
