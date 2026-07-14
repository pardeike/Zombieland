#!/usr/bin/env python3
"""Conservatively format changed active XML-like project files."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tempfile


ROOT = Path(__file__).resolve().parent.parent
UTF8_BOM = b"\xef\xbb\xbf"
XML_DECLARATION = re.compile(br"^[ \t\r\n]*(<\?xml\s+[^?]*\?>)", re.IGNORECASE)


def run_git(*args: str) -> bytes:
	result = subprocess.run(
		["git", *args],
		cwd=ROOT,
		stdout=subprocess.PIPE,
		stderr=subprocess.PIPE,
		check=False,
	)
	if result.returncode != 0:
		raise RuntimeError(result.stderr.decode("utf-8", errors="replace").strip())
	return result.stdout


def nul_paths(data: bytes) -> list[Path]:
	return [Path(os.fsdecode(value)) for value in data.split(b"\0") if value]


def is_format_target(path: Path) -> bool:
	name = path.as_posix()
	if name.startswith(("1.4/", "Originals/")):
		return False
	if name == "LoadFolders.xml" or name == "Directory.Build.props":
		return True
	if name.startswith("About/") and path.suffix.lower() == ".xml":
		return True
	if name.startswith("1.6/") and path.suffix.lower() == ".xml":
		return True
	return (
		name.startswith("Source/")
		and path.suffix.lower() == ".csproj"
		and not any(part in {"bin", "obj"} for part in path.parts)
	)


def is_runtime_xml(path: Path) -> bool:
	name = path.as_posix()
	return (
		name == "LoadFolders.xml"
		or (name.startswith("About/") and path.suffix.lower() == ".xml")
		or (name.startswith("1.6/") and path.suffix.lower() == ".xml")
	)


def changed_paths(base: str) -> list[Path]:
	# Keep old paths for moves out of the active tree so they still select runtime validation.
	diff_arguments = ("diff", "--name-only", "--no-renames", "--diff-filter=ACMRD", "-z")
	if base == "HEAD":
		committed_or_modified = nul_paths(run_git(*diff_arguments, "HEAD", "--"))
	else:
		committed_or_modified = nul_paths(run_git(*diff_arguments, base, "HEAD", "--"))
		committed_or_modified.extend(
			nul_paths(run_git(*diff_arguments, "HEAD", "--"))
		)

	untracked = nul_paths(run_git("ls-files", "--others", "--exclude-standard", "-z", "--"))
	unique = {path.as_posix(): path for path in [*committed_or_modified, *untracked]}
	return [
		path
		for _, path in sorted(unique.items())
		if is_format_target(path)
	]


def normalize_output(source: bytes, formatted: bytes) -> bytes:
	has_bom = source.startswith(UTF8_BOM)
	source_body = source[len(UTF8_BOM) :] if has_bom else source
	source_declaration = XML_DECLARATION.match(source_body)
	declaration = source_declaration.group(1) if source_declaration else None

	formatted_body = formatted[len(UTF8_BOM) :] if formatted.startswith(UTF8_BOM) else formatted
	formatted_body = formatted_body.replace(b"\r\n", b"\n").replace(b"\r", b"\n")
	generated_declaration = XML_DECLARATION.match(formatted_body)
	if generated_declaration:
		formatted_body = formatted_body[generated_declaration.end() :].lstrip(b"\n")
	if declaration:
		formatted_body = declaration + b"\n" + formatted_body

	formatted_body = formatted_body.rstrip(b"\n") + b"\n"
	if b"\r\n" in source_body:
		formatted_body = formatted_body.replace(b"\n", b"\r\n")
	return (UTF8_BOM if has_bom else b"") + formatted_body


def xmllint(*args: str, env: dict[str, str] | None = None) -> subprocess.CompletedProcess[bytes]:
	return subprocess.run(
		["xmllint", *args],
		cwd=ROOT,
		env=env,
		stdout=subprocess.PIPE,
		stderr=subprocess.PIPE,
		check=False,
	)


def canonical_xml(path: Path) -> bytes:
	result = xmllint("--noblanks", "--c14n", str(path))
	if result.returncode != 0:
		raise RuntimeError(result.stderr.decode("utf-8", errors="replace").strip())
	return result.stdout


def proposed_format(path: Path) -> bytes:
	environment = os.environ.copy()
	environment["XMLLINT_INDENT"] = "\t"
	result = xmllint("--format", str(path), env=environment)
	if result.returncode != 0:
		raise RuntimeError(result.stderr.decode("utf-8", errors="replace").strip())
	return normalize_output(path.read_bytes(), result.stdout)


def semantically_equal(path: Path, proposed: bytes) -> bool:
	with tempfile.NamedTemporaryFile(suffix=path.suffix, delete=False) as temporary:
		temporary.write(proposed)
		temporary_path = Path(temporary.name)
	try:
		return canonical_xml(path) == canonical_xml(temporary_path)
	finally:
		temporary_path.unlink(missing_ok=True)


def replace_preserving_mode(path: Path, contents: bytes) -> None:
	mode = stat.S_IMODE(path.stat().st_mode)
	with tempfile.NamedTemporaryFile(dir=path.parent, prefix=f".{path.name}.", delete=False) as temporary:
		temporary.write(contents)
		temporary_path = Path(temporary.name)
	try:
		os.chmod(temporary_path, mode)
		os.replace(temporary_path, path)
	finally:
		temporary_path.unlink(missing_ok=True)


def validate_runtime_xml() -> bool:
	result = subprocess.run([str(ROOT / "scripts/check-versioned-xml.sh")], cwd=ROOT, check=False)
	return result.returncode == 0


def parse_arguments() -> argparse.Namespace:
	parser = argparse.ArgumentParser(description=__doc__)
	parser.add_argument("--check", action="store_true", help="report files that need formatting without writing them")
	parser.add_argument("--changed", action="store_true", help="select changed active project XML files")
	parser.add_argument("--base", default="HEAD", help="compare committed files with this Git revision (default: HEAD)")
	parser.add_argument("--validate-runtime", action="store_true", help="run active XML validation when runtime XML is selected")
	parser.add_argument("paths", nargs="*", type=Path)
	arguments = parser.parse_args()
	if arguments.changed and arguments.paths:
		parser.error("paths cannot be combined with --changed")
	if not arguments.changed and not arguments.paths:
		parser.error("pass one or more paths, or use --changed")
	return arguments


def main() -> int:
	arguments = parse_arguments()
	paths = changed_paths(arguments.base) if arguments.changed else arguments.paths
	paths = [path if path.is_absolute() else ROOT / path for path in paths]
	runtime_selected = any(is_runtime_xml(path.relative_to(ROOT)) for path in paths if path.is_relative_to(ROOT))
	if arguments.changed:
		paths = [path for path in paths if path.is_file()]
	if paths and shutil.which("xmllint") is None:
		print("XML formatting requires xmllint on PATH.", file=sys.stderr)
		return 2
	needs_formatting: list[Path] = []

	for path in paths:
		try:
			proposed = proposed_format(path)
		except RuntimeError as error:
			print(f"Cannot format {path}: {error}", file=sys.stderr)
			return 2
		if proposed == path.read_bytes():
			continue
		try:
			equal = semantically_equal(path, proposed)
		except RuntimeError as error:
			print(f"Cannot validate formatted XML for {path}: {error}", file=sys.stderr)
			return 2
		if not equal:
			print(f"Refusing to rewrite {path}: canonical XML changed.", file=sys.stderr)
			return 2
		needs_formatting.append(path)
		if not arguments.check:
			replace_preserving_mode(path, proposed)

	for path in needs_formatting:
		try:
			display = path.relative_to(ROOT)
		except ValueError:
			display = path
		verb = "Would format" if arguments.check else "Formatted"
		print(f"{verb} XML: {display}")

	validation_ok = True
	if arguments.validate_runtime and runtime_selected:
		validation_ok = validate_runtime_xml()
	if not validation_ok:
		return 2
	return 1 if arguments.check and needs_formatting else 0


if __name__ == "__main__":
	sys.exit(main())
