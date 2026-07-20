#!/usr/bin/env bash
set -euo pipefail

python3 - <<'PY'
from pathlib import Path
from collections import Counter, defaultdict
import re
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
            for nested in child.iter():
                if nested is child or list(nested):
                    continue
                if not (nested.text or "").strip():
                    errors.append(
                        f"{xml_path}: empty nested translation element "
                        f"<{child.tag}>/<{nested.tag}>"
                    )
            translation_sources[
                (language_version, language, scope, child.tag)
            ].append(xml_path)
            translation_values[
                (language_version, language, scope, child.tag)
            ] = "".join(child.itertext()).strip()
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

def normalized_translation_handle(value):
    value = value.strip().replace(" ", "_").replace("\n", "_")
    value = value.replace("\r", "").replace("\t", "_").replace(".", "")
    value = value.replace("-", "")
    value = re.sub(r"\{[^{}]*\}", "", value)
    value = "".join(
        char
        for char in value
        if char in "qwertyuiopasdfghjklzxcvbnmQWERTYUIOPASDFGHJKLZXCVBNM1234567890-_"
    )
    value = re.sub(r"_+", "_", value).strip("_")
    if value.isdigit():
        value = "_" + value
    return value


def named_list_children(node):
    children = list(node)
    if not children:
        return []

    class_handles = []
    for child in children:
        class_name = child.get("Class", "").rsplit(".", 1)[-1]
        for prefix in ("QuestNode_", "QuestPart_"):
            if class_name.startswith(prefix):
                class_name = class_name[len(prefix):]
        class_handles.append(normalized_translation_handle(class_name))
    class_counts = Counter(handle.casefold() for handle in class_handles if handle)

    base_handles = []
    for child, class_handle in zip(children, class_handles):
        if class_handle and class_counts[class_handle.casefold()] == 1:
            handle = class_handle
        else:
            handle = ""
            for field_name in ("inSignal", "label", "def", "name", "storeAs"):
                field_value = child.findtext(field_name)
                if field_value:
                    handle = normalized_translation_handle(field_value)
                    break
            if not handle:
                handle = class_handle
        base_handles.append(handle)

    handle_counts = Counter(handle.casefold() for handle in base_handles if handle)
    handle_indices = defaultdict(int)
    result = []
    for child, handle in zip(children, base_handles):
        if not handle:
            result.append(("", child))
            continue
        folded = handle.casefold()
        if handle_counts[folded] > 1:
            indexed_handle = f"{handle}-{handle_indices[folded]}"
            handle_indices[folded] += 1
            result.append((indexed_handle, child))
        else:
            result.append((handle, child))
    return result


def english_def_node_at_path(def_node, field_path):
    current = def_node
    for segment in field_path.split("."):
        if segment == "slateRef" and current.get("TKey") is not None:
            # SlateRef<T> is represented by the TKey-bearing XML node itself.
            continue
        if segment.isdigit():
            children = list(current)
            index = int(segment)
            if index >= len(children):
                return None
            current = children[index]
        else:
            direct_child = current.find(segment)
            if direct_child is not None:
                current = direct_child
                continue
            named_child = next(
                (
                    child
                    for handle, child in named_list_children(current)
                    if handle.casefold() == segment.casefold()
                ),
                None,
            )
            if named_child is None:
                return None
            current = named_child
    return current


def english_def_value_at_path(def_node, field_path):
    node = english_def_node_at_path(def_node, field_path)
    if node is None:
        return None
    value = "".join(node.itertext()).strip()
    return value or None


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
    if not field_path or english_def_value_at_path(def_node, field_path) is None:
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
    translated_languages = sorted(languages_by_version[version] - {"English"})
    def_scopes = sorted(
        {
            scope
            for candidate_version, _, scope, _ in translation_sources
            if candidate_version == version and scope.startswith("DefInjected/")
        }
    )
    for scope in def_scopes:
        expected_keys = {
            key
            for candidate_version, candidate_language, candidate_scope, key in translation_sources
            if candidate_version == version
            and candidate_language != "English"
            and candidate_scope == scope
        }
        for language in translated_languages:
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
                    f"{version}/{language}/{scope}: missing active DefInjected keys "
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
        if language != "English":
            makeshift_description = translation_values.get(
                (version, language, scope, "ZombieSerumSimple.description")
            )
            full_description = translation_values.get(
                (version, language, scope, "Zombie100Serum.description")
            )
            if (
                makeshift_description is not None
                and full_description is not None
                and makeshift_description != full_description
            ):
                errors.append(
                    f"{version}/{language}/{scope}: makeshift and regular 100% "
                    "zombie serum descriptions must remain functionally identical"
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


def settings_help_targets():
    settings_path = repo / "Source/SettingsDialog.cs"
    extensions_path = repo / "Source/DialogExtensions.cs"
    settings_types_path = repo / "Source/ZombieSettings.cs"
    source_paths = (settings_path, extensions_path, settings_types_path)
    missing_sources = [str(path) for path in source_paths if not path.exists()]
    if missing_sources:
        errors.append(
            "Settings help validation is missing source files: "
            + ", ".join(missing_sources)
        )
        return set()

    settings_source = settings_path.read_text(encoding="utf-8-sig")
    extensions_source = extensions_path.read_text(encoding="utf-8-sig")
    settings_types_source = settings_types_path.read_text(encoding="utf-8-sig")
    dialog_source = settings_source + "\n" + extensions_source
    targets = set()

    # These helpers all register their translation ID with the settings help panel.
    direct_patterns = (
        r'\.Dialog_(?:Label|Checkbox|List|Integer|FloatSlider|EnumSlider|IntSlider|TimeSlider|Enum)\(\s*"([^"]+)"',
        r'\.Dialog_Text\([^,]+,\s*"([^"]+)"',
        r'\.Dialog_RadioButton\([^,]+,\s*"([^"]+)"',
        r'\.Help\(\s*"([^"]+)"',
    )
    for pattern in direct_patterns:
        targets.update(re.findall(pattern, dialog_source))

    # A Dialog_Button's visible label, rather than its description, owns the help ID.
    targets.update(
        re.findall(
            r'\.Dialog_Button\(\s*"[^"]+"\s*,\s*"([^"]+)"',
            dialog_source,
        )
    )

    # Anomaly rows may provide the visible label or a separate final help ID.
    for match in re.finditer(
        r'\.Dialog_AnomalyTargetingOverride\((.*?)\);',
        settings_source,
        re.DOTALL,
    ):
        targets.update(re.findall(r'"([^"]+)"', match.group(1)))

    # Resolve local string variables used as help or button-label IDs. This covers
    # both the main-menu/in-game reset-title choice and the music-share slider.
    variable_sources = {}
    for match in re.finditer(
        r'\b(?:const\s+string|var)\s+(\w+)\s*=\s*([^;]+);',
        dialog_source,
    ):
        variable_sources[match.group(1)] = set(
            re.findall(r'"([^"]+)"', match.group(2))
        )
    for variable, values in variable_sources.items():
        if not values:
            continue
        if re.search(rf'\.Help\(\s*{re.escape(variable)}\b', dialog_source):
            targets.update(values)
        if re.search(
            rf'\.Dialog_Button\(\s*[^,]+,\s*{re.escape(variable)}\b',
            dialog_source,
        ):
            targets.update(values)

    # The special-zombie sliders take their IDs from a local tuple array.
    targets.update(
        re.findall(
            r'new\s+FloatRef\([^\n]*?\),\s*"([^"]+)"\s*\)',
            settings_source,
        )
    )

    # Dialog_Enum creates one help target for every value of the bound enum.
    settings_fields = dict(
        (field, field_type)
        for field_type, field in re.findall(
            r'\bpublic\s+(\w+)\s+(\w+)\s*(?:=|;)',
            settings_types_source,
        )
    )
    for field in re.findall(
        r'\.Dialog_Enum\(\s*"[^"]+"\s*,\s*ref\s+settings\.(\w+)\s*\)',
        settings_source,
    ):
        enum_type = settings_fields.get(field)
        if enum_type is None:
            errors.append(
                f"{settings_path}: cannot resolve enum type for settings field {field}"
            )
            continue
        enum_match = re.search(
            rf'\benum\s+{re.escape(enum_type)}(?:\s*:\s*\w+)?\s*\{{(.*?)\}}',
            settings_types_source,
            re.DOTALL,
        )
        if enum_match is None:
            errors.append(
                f"{settings_types_path}: cannot find enum declaration {enum_type}"
            )
            continue
        for value in re.findall(
            r'^\s*(\w+)\s*(?:=[^,]+)?\s*,?\s*$',
            enum_match.group(1),
            re.MULTILINE,
        ):
            targets.add(f"{enum_type}_{value}")

    return targets


required_settings_help_keys = {
    f"{target}_Help" for target in settings_help_targets()
}
for version in sorted(languages_by_version):
    if version in frozen_versions:
        continue
    for language in sorted(languages_by_version[version]):
        translated_keys = {
            key
            for candidate_version, candidate_language, scope, key in translation_sources
            if candidate_version == version
            and candidate_language == language
            and scope == "Keyed"
        }
        missing = sorted(required_settings_help_keys - translated_keys)
        if missing:
            errors.append(
                f"{version}/{language}/Keyed: missing settings help keys "
                + ", ".join(missing)
            )


def required_runtime_tokens(value):
    tokens = []
    token_patterns = (
        ("brace", r"\{[^{}]+\}"),
        ("bracket", r"\[[^\[\]]+\]"),
        ("target", r"Target[A-Z]"),
        ("newline", r"\\n"),
        ("tag", r"</?[A-Za-z][^>]*>"),
        ("rule", r"[A-Za-z][A-Za-z0-9_]*->"),
        ("percent", r"%"),
    )
    for token_kind, pattern in token_patterns:
        tokens.extend((token_kind, match) for match in re.findall(pattern, value))
    return Counter(tokens)


for (version, language, scope, key), translated_value in sorted(translation_values.items()):
    if language == "English":
        continue
    english_value = None
    if scope == "Keyed":
        english_value = translation_values.get((version, "English", scope, key))
    elif scope.startswith("DefInjected/"):
        def_type = scope.split("/", 1)[1]
        def_name, _, field_path = key.partition(".")
        def_node = defined_def_nodes.get((version, def_type, def_name))
        if def_node is not None and field_path:
            english_value = english_def_value_at_path(def_node, field_path)
    if english_value is None:
        continue
    required_tokens = required_runtime_tokens(english_value)
    translated_tokens = required_runtime_tokens(translated_value)
    missing_tokens = required_tokens - translated_tokens
    if missing_tokens:
        details = ", ".join(
            f"{kind}:{token!r} x{count}"
            for (kind, token), count in sorted(missing_tokens.items())
        )
        errors.append(
            f"{version}/{language}/{scope}: translation key {key} is missing "
            f"English runtime tokens: {details}"
        )
    unexpected_tokens = translated_tokens - required_tokens
    unexpected_tokens = Counter(
        {
            token_key: count
            for token_key, count in unexpected_tokens.items()
            if not (token_key[0] == "brace" and "?" in token_key[1])
        }
    )
    if unexpected_tokens:
        details = ", ".join(
            f"{kind}:{token!r} x{count}"
            for (kind, token), count in sorted(unexpected_tokens.items())
        )
        errors.append(
            f"{version}/{language}/{scope}: translation key {key} has unexpected "
            f"runtime tokens not present in English: {details}"
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
