"""从发布输出创建可导入的 .spkg；可在 Windows 和 Linux 上运行。"""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def package(source: Path, output: Path) -> str:
    source = source.resolve()
    metadata = json.loads((source / "plugin.json").read_text(encoding="utf-8-sig"))
    required = ["plugin.json", "LICENSE", "Languages/en.json", "Languages/en.xaml",
                "Languages/zh-cn.json", "Languages/zh-cn.xaml"]
    for key in ("ExecuteFileName", "IconPath"):
        name = metadata[key]
        if not isinstance(name, str) or "/" in name or "\\" in name or name in ("", ".", ".."):
            raise ValueError(f"Invalid {key}: expected a root filename")
        required.append(name)
    for name in required:
        path = source / name
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f"Missing or empty package file: {name}")
    if not metadata.get("PluginID") or not metadata.get("Version"):
        raise ValueError("PluginID and Version are required")

    files = [path for path in source.rglob("*") if path.is_file()
             and (path.suffix.lower() in (".dll", ".json", ".xaml", ".png") or path.name == "LICENSE")
             and path.name != "STranslate.Plugin.dll"]
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(files):
            info = zipfile.ZipInfo(path.relative_to(source).as_posix(), date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            archive.writestr(info, path.read_bytes())
    with zipfile.ZipFile(output) as archive:
        if archive.testzip() is not None or not set(required).issubset(archive.namelist()):
            raise ValueError("Package verification failed")
    checksum = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(output.suffix + ".sha256").write_text(f"{checksum}  {output.name}\n", encoding="utf-8")
    return checksum


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    print(f"Created {args.output} (SHA256 {package(args.source, args.output)})")
