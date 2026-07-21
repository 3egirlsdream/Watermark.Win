#!/usr/bin/env python3
"""Validate Watermark LayoutSchemaVersion 2 template config.json files."""

from __future__ import annotations

import argparse
import json
import math
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable


@dataclass(frozen=True)
class Finding:
    severity: str
    location: str
    message: str


NODE_ARRAYS = ("Containers", "Texts", "Logos", "Lines")
COLOR_PATTERN = re.compile(r"^#[0-9A-Fa-f]{8}$")
ID_PATTERN = re.compile(r"^[0-9A-F]{32}$")
NON_V2_CANVAS_FIELDS = {"EnableMarginXS"}
NON_V2_COMMON_NODE_FIELDS = {"Margin", "Transform"}
NON_V2_CONTAINER_FIELDS = {
    "Angle",
    "ContainerAlignment",
    "HeightPercent",
    "HorizontalAlignment",
    "Orientation",
    "VerticalAlignment",
    "WidthPercent",
    "XOffset",
    "YOffset",
}
PHOTO_METADATA_NAME_HINTS = (
    "机型", "镜头", "相机", "曝光", "拍摄", "时间", "日期", "坐标", "位置", "地点",
    "编号", "期号", "画幅", "胶卷", "参数", "光圈", "快门", "焦距", "感光", "ISO",
)


def enum_value(value: Any, names: dict[str, int]) -> int | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, str):
        return names.get(value.lower())
    return None


def finite_number(value: Any) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    result = float(value)
    return result if math.isfinite(result) else None


def add_range(findings: list[Finding], location: str, value: Any, low: float, high: float, *, high_inclusive: bool = True) -> None:
    number = finite_number(value)
    invalid = number is None or number < low or number > high or (not high_inclusive and number == high)
    if invalid:
        end = "]" if high_inclusive else ")"
        findings.append(Finding("error", location, f"必须位于 [{low}, {high}{end}。"))


def add_enum(findings: list[Finding], location: str, value: Any, names: dict[str, int], allowed: set[int]) -> int | None:
    resolved = enum_value(value, names)
    if resolved not in allowed:
        findings.append(Finding("error", location, f"枚举值无效：{value!r}。"))
        return None
    return resolved


def validate_length(findings: list[Finding], location: str, value: Any, low: float, high: float, *, optional: bool = False) -> None:
    if value is None and optional:
        return
    if not isinstance(value, dict):
        findings.append(Finding("error", location, "必须是 WMStyleLength 对象。"))
        return
    unit = add_enum(findings, f"{location}.Unit", value.get("Unit"), {"auto": 0, "percent": 1}, {0, 1})
    if unit == 1:
        add_range(findings, f"{location}.Value", value.get("Value"), low, high)


def validate_thickness(findings: list[Finding], location: str, value: Any, low: float, high: float) -> None:
    if not isinstance(value, dict):
        findings.append(Finding("error", location, "必须是包含 Top/Right/Bottom/Left 的对象。"))
        return
    for edge in ("Top", "Right", "Bottom", "Left"):
        add_range(findings, f"{location}.{edge}", value.get(edge), low, high)


def validate_transform(findings: list[Finding], location: str, value: Any, *, static: bool) -> None:
    if not isinstance(value, dict):
        findings.append(Finding("error", location, "必须是 Transform 对象。"))
        return
    offset_x = finite_number(value.get("OffsetXPercent"))
    offset_y = finite_number(value.get("OffsetYPercent"))
    scale_x = finite_number(value.get("ScaleX"))
    scale_y = finite_number(value.get("ScaleY"))
    rotation = finite_number(value.get("Rotation"))
    if static:
        if (offset_x, offset_y, scale_x, scale_y, rotation) != (0.0, 0.0, 1.0, 1.0, 0.0):
            findings.append(Finding("error", location, "Static 节点 Transform 必须保持 offset=0、scale=1、rotation=0。"))
        return
    add_range(findings, f"{location}.ScaleX", value.get("ScaleX"), 0.1, 4)
    add_range(findings, f"{location}.ScaleY", value.get("ScaleY"), 0.1, 4)
    add_range(findings, f"{location}.Rotation", value.get("Rotation"), -180, 180, high_inclusive=False)
    add_range(findings, f"{location}.OffsetXPercent", value.get("OffsetXPercent"), -500, 500)
    add_range(findings, f"{location}.OffsetYPercent", value.get("OffsetYPercent"), -500, 500)


def validate_color(findings: list[Finding], location: str, value: Any, *, optional: bool = False) -> None:
    if optional and (value is None or value == ""):
        return
    if not isinstance(value, str) or not COLOR_PATTERN.fullmatch(value):
        findings.append(Finding("error", location, "颜色必须使用 #RRGGBBAA。"))


def validate_style(findings: list[Finding], node: dict[str, Any], node_id: str, *, root: bool) -> None:
    style = node.get("Style")
    location = f"node[{node_id}].Style"
    if not isinstance(style, dict):
        findings.append(Finding("error", location, "V2 节点必须包含 Style。"))
        return
    position = add_enum(findings, f"{location}.Position", style.get("Position"), {"static": 0, "absolute": 1}, {0, 1})
    if root and position != 1:
        findings.append(Finding("error", f"{location}.Position", "顶级容器必须为 Absolute。"))
    if not root and position is None:
        return
    validate_length(findings, f"{location}.Width", style.get("Width"), 0, 100)
    validate_length(findings, f"{location}.Height", style.get("Height"), 0, 100)
    for name in ("Top", "Right", "Bottom", "Left"):
        validate_length(findings, f"{location}.{name}", style.get(name), -25, 125, optional=True)
    if position == 0 and any(style.get(name) is not None for name in ("Top", "Right", "Bottom", "Left")):
        findings.append(Finding("warning", location, "Static 节点的 Top/Right/Bottom/Left 不参与布局，应改用 Margin。"))
    if position == 1 and all(style.get(name) is None for name in ("Top", "Right", "Bottom", "Left")):
        findings.append(Finding("warning", location, "Absolute 节点未设置 inset，将默认放在父内容区左上角。"))
    validate_thickness(findings, f"{location}.Margin", style.get("Margin"), -25, 25)
    validate_thickness(findings, f"{location}.Padding", style.get("Padding"), 0, 25)
    add_enum(findings, f"{location}.Overflow", style.get("Overflow"), {"visible": 0, "hidden": 1}, {0, 1})
    add_enum(findings, f"{location}.Flex", style.get("Flex"), {"none": 0, "initial": 1, "fill": 2}, {0, 1, 2})
    add_enum(findings, f"{location}.FlexDirection", style.get("FlexDirection"), {"horizontal": 0, "vertical": 1}, {0, 1})
    add_enum(findings, f"{location}.JustifyContent", style.get("JustifyContent"), {"start": 0, "center": 1, "end": 2}, {0, 1, 2})
    add_enum(findings, f"{location}.AlignItems", style.get("AlignItems"), {"start": 0, "center": 1, "end": 2, "baseline": 3}, {0, 1, 2, 3})
    if not isinstance(style.get("ZIndex"), int) or isinstance(style.get("ZIndex"), bool):
        findings.append(Finding("error", f"{location}.ZIndex", "必须是整数。"))
    if not isinstance(style.get("FlexReverse"), bool):
        findings.append(Finding("error", f"{location}.FlexReverse", "必须是布尔值。"))
    add_range(findings, f"{location}.Gap", style.get("Gap"), 0, 25)
    validate_transform(findings, f"{location}.Transform", style.get("Transform"), static=position == 0)


def safe_asset_path(template_dir: Path, raw: str) -> Path | None:
    if not raw:
        return None
    candidate = Path(raw)
    if candidate.is_absolute():
        return candidate.resolve()
    resolved = (template_dir / candidate).resolve()
    try:
        resolved.relative_to(template_dir.resolve())
    except ValueError:
        return None
    return resolved


def validate_assets(findings: list[Finding], nodes: Iterable[tuple[str, dict[str, Any]]], template_dir: Path, strict: bool) -> None:
    for kind, node in nodes:
        node_id = str(node.get("ID") or "?")
        if kind == "Texts":
            font = node.get("FontFamily")
            if isinstance(font, str) and font and font != "MiSans.ttf" and ("/" in font or "\\" in font or Path(font).suffix):
                font_path = safe_asset_path(template_dir, font)
                if font_path is None:
                    findings.append(Finding("error", f"node[{node_id}].FontFamily", "字体路径逃逸模板目录。"))
                elif not font_path.is_file():
                    findings.append(Finding("error" if strict else "warning", f"node[{node_id}].FontFamily", f"字体不存在：{font_path}"))
        path = node.get("Path")
        if not isinstance(path, str) or not path:
            if kind == "Logos":
                if node.get("AutoSetLogo", False):
                    if strict:
                        findings.append(Finding("error", f"node[{node_id}].Path", "严格资源模式要求自动品牌 Logo 同时提供可打包的固定/回退 Path；当前压缩模板分支不会按 Make 读取品牌资源。"))
                else:
                    findings.append(Finding("warning", f"node[{node_id}].Path", "Logo 没有固定 Path，也未启用 AutoSetLogo。"))
            continue
        resolved = safe_asset_path(template_dir, path)
        if resolved is None:
            findings.append(Finding("error", f"node[{node_id}].Path", "资源路径逃逸模板目录。"))
        elif not resolved.is_file():
            severity = "error" if strict or kind == "Containers" else "warning"
            findings.append(Finding(severity, f"node[{node_id}].Path", f"资源不存在：{resolved}"))


def validate_config(data: Any, template_dir: Path, strict_assets: bool = False) -> list[Finding]:
    findings: list[Finding] = []
    if not isinstance(data, dict):
        return [Finding("error", "$", "配置根节点必须是 JSON 对象。")]
    if data.get("LayoutSchemaVersion") != 2:
        findings.append(Finding("error", "$.LayoutSchemaVersion", "必须固定为 2。"))
    for field in sorted(NON_V2_CANVAS_FIELDS):
        if field in data:
            findings.append(Finding("error", f"$.{field}", "V2 配置不得包含此画布字段。"))
    canvas_type = add_enum(findings, "$.CanvasType", data.get("CanvasType"), {"normal": 0, "split": 1}, {0, 1})
    if canvas_type == 1:
        add_range(findings, "$.CustomWidth", data.get("CustomWidth"), 1, 100000)
        add_range(findings, "$.CustomHeight", data.get("CustomHeight"), 1, 100000)
    validate_color(findings, "$.BackgroundColor", data.get("BackgroundColor"))
    validate_thickness(findings, "$.BorderThickness", data.get("BorderThickness"), 0, 50)
    canvas_id = data.get("ID")
    if not isinstance(canvas_id, str) or not canvas_id.strip():
        findings.append(Finding("error", "$.ID", "模板 ID 不能为空。"))
    elif not ID_PATTERN.fullmatch(canvas_id):
        findings.append(Finding("warning", "$.ID", "新模板 ID 建议使用 32 位大写十六进制字符串。"))
    if not isinstance(data.get("Name"), str) or not data.get("Name", "").strip():
        findings.append(Finding("warning", "$.Name", "模板名称为空。"))

    nodes: list[tuple[str, dict[str, Any]]] = []
    for name in NODE_ARRAYS:
        value = data.get(name)
        if not isinstance(value, list):
            findings.append(Finding("error", f"$.{name}", "必须存在且为数组。"))
            continue
        for index, node in enumerate(value):
            if not isinstance(node, dict):
                findings.append(Finding("error", f"$.{name}[{index}]", "节点必须是对象。"))
            else:
                nodes.append((name, node))

    by_id: dict[str, tuple[str, dict[str, Any]]] = {}
    children: dict[str, list[tuple[int, str]]] = {}
    root_ids: set[str] = set()
    container_ids: set[str] = set()
    parent_of: dict[str, str] = {}

    for kind, node in nodes:
        node_id = node.get("ID")
        location = f"$.{kind}"
        if not isinstance(node_id, str) or not node_id.strip():
            findings.append(Finding("error", location, "节点 ID 不能为空。"))
            continue
        if node_id in by_id:
            findings.append(Finding("error", f"node[{node_id}]", "节点 ID 重复。"))
            continue
        by_id[node_id] = (kind, node)
        if not ID_PATTERN.fullmatch(node_id):
            findings.append(Finding("warning", f"node[{node_id}].ID", "新节点 ID 建议使用 32 位大写十六进制字符串。"))
        if kind == "Containers":
            container_ids.add(node_id)
        pnode = node.get("PNode")
        if not isinstance(pnode, dict):
            findings.append(Finding("error", f"node[{node_id}].PNode", "必须包含 PID 和 SEQ。"))
            continue
        pid = pnode.get("PID")
        seq = pnode.get("SEQ")
        if not isinstance(pid, str) or not pid:
            findings.append(Finding("error", f"node[{node_id}].PNode.PID", "PID 必须是非空字符串。"))
            continue
        if not isinstance(seq, int) or isinstance(seq, bool) or seq < 0:
            findings.append(Finding("error", f"node[{node_id}].PNode.SEQ", "SEQ 必须是非负整数。"))
            continue
        parent_of[node_id] = pid
        children.setdefault(pid, []).append((seq, node_id))
        if kind == "Containers" and pid == "0":
            root_ids.add(node_id)

    for node_id, (kind, node) in by_id.items():
        pid = parent_of.get(node_id)
        if pid is None:
            continue
        if kind != "Containers" and pid == "0":
            findings.append(Finding("error", f"node[{node_id}].PNode.PID", "叶子节点不能直接挂在画布上。"))
        if pid != "0" and pid not in container_ids:
            findings.append(Finding("error", f"node[{node_id}].PNode.PID", f"父容器不存在：{pid}。"))
        if kind == "Containers" and pid != "0":
            parent_pid = parent_of.get(pid)
            if parent_pid != "0":
                findings.append(Finding("error", f"node[{node_id}].PNode.PID", "当前序列化只支持两层容器；不能在二级容器下继续放容器。"))
        prohibited_fields = set(NON_V2_COMMON_NODE_FIELDS)
        if kind == "Containers":
            prohibited_fields.update(NON_V2_CONTAINER_FIELDS)
        elif kind == "Texts":
            prohibited_fields.add("Percent")
        for field in sorted(prohibited_fields):
            if field in node:
                findings.append(Finding("error", f"node[{node_id}].{field}", "V2 布局不得包含此节点顶层字段；请使用 Style 或节点自身的 V2 属性。"))
        validate_style(findings, node, node_id, root=node_id in root_ids)

        if not isinstance(node.get("Enabled"), bool):
            findings.append(Finding("warning", f"node[{node_id}].Enabled", "建议显式写入布尔值。"))
        if "IsLocked" in node and not isinstance(node.get("IsLocked"), bool):
            findings.append(Finding("error", f"node[{node_id}].IsLocked", "必须是布尔值。"))

        style = node.get("Style") if isinstance(node.get("Style"), dict) else {}
        if kind == "Containers":
            validate_color(findings, f"node[{node_id}].BackgroundColor", node.get("BackgroundColor"), optional=True)
            properties = node.get("ContainerProperties")
            if isinstance(properties, dict):
                blur_enabled = properties.get("EnableGaussianBlur", False)
                if not isinstance(blur_enabled, bool):
                    findings.append(Finding("error", f"node[{node_id}].ContainerProperties.EnableGaussianBlur", "必须是布尔值。"))
                elif blur_enabled:
                    add_range(findings, f"node[{node_id}].ContainerProperties.GaussianDeep", properties.get("GaussianDeep"), 1, 60)
                    background = node.get("BackgroundColor")
                    if isinstance(background, str) and COLOR_PATTERN.fullmatch(background) and background[-2:].lower() == "ff":
                        findings.append(Finding("warning", f"node[{node_id}].BackgroundColor", "背景模糊容器使用了不透明背景色，模糊结果会被填充遮住。"))
        if kind == "Texts":
            add_range(findings, f"node[{node_id}].FontSize", node.get("FontSize"), 0.1, 25)
            add_range(findings, f"node[{node_id}].LetterSpacing", node.get("LetterSpacing", 0), -1, 3)
            if "LetterSpacing" not in node:
                findings.append(Finding("warning", f"node[{node_id}].LetterSpacing", "新 V2 文本建议显式写入字距；0 表示字体默认间距。"))
            validate_color(findings, f"node[{node_id}].FontColor", node.get("FontColor"))
            validate_color(findings, f"node[{node_id}].BorderColor", node.get("BorderColor"))
            exifs = node.get("Exifs")
            if not isinstance(exifs, list) or not exifs:
                findings.append(Finding("warning", f"node[{node_id}].Exifs", "文本没有 EXIF/固定文字片段，将渲染为空。"))
            elif any(hint in str(node.get("Name", "")) for hint in PHOTO_METADATA_NAME_HINTS) and all(
                not isinstance(entry, dict) or not str(entry.get("Key", "")).strip() for entry in exifs
            ):
                findings.append(Finding(
                    "warning",
                    f"node[{node_id}].Exifs",
                    "照片信息文字必须绑定相机元数据 Key，示例值不能写成固定文字。",
                ))
            if node.get("TextWrap") is True:
                width = style.get("Width") if isinstance(style.get("Width"), dict) else {}
                if enum_value(width.get("Unit"), {"auto": 0, "percent": 1}) != 1:
                    findings.append(Finding("warning", f"node[{node_id}].Style.Width", "换行文本建议设置明确百分比宽度。"))
        elif kind == "Logos":
            add_range(findings, f"node[{node_id}].Percent", node.get("Percent"), 0.01, 100)
        elif kind == "Lines":
            add_range(findings, f"node[{node_id}].Percent", node.get("Percent"), 1, 100)
            add_range(findings, f"node[{node_id}].Thickness", node.get("Thickness"), 1, 20)
            add_enum(findings, f"node[{node_id}].Orientation", node.get("Orientation"), {"horizontal": 0, "vertical": 1}, {0, 1})
            validate_color(findings, f"node[{node_id}].Color", node.get("Color"))

    for pid, entries in children.items():
        seen_seq: set[int] = set()
        for seq, node_id in entries:
            if seq in seen_seq:
                findings.append(Finding("error", f"parent[{pid}].SEQ", f"同一父节点 SEQ={seq} 重复（包含 {node_id}）。"))
            seen_seq.add(seq)
        expected = list(range(len(entries)))
        actual = sorted(seq for seq, _ in entries)
        if actual != expected:
            findings.append(Finding("warning", f"parent[{pid}].SEQ", f"建议连续编号 0..{max(0, len(entries)-1)}，当前为 {actual}。"))

    if not root_ids:
        findings.append(Finding("error", "$.Containers", "至少需要一个 PID=0 的顶级容器。"))

    validate_assets(findings, nodes, template_dir, strict_assets)
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config", type=Path, help="config.json 路径")
    parser.add_argument("--template-dir", type=Path, help="模板资源根目录，默认使用 config.json 所在目录")
    parser.add_argument("--strict-assets", action="store_true", help="将缺失的可选 Logo/字体资源也视为错误")
    args = parser.parse_args()

    try:
        data = json.loads(args.config.read_text(encoding="utf-8"))
    except FileNotFoundError:
        print(f"ERROR $.config: 文件不存在：{args.config}", file=sys.stderr)
        return 2
    except (OSError, json.JSONDecodeError) as exc:
        print(f"ERROR $.config: 无法读取 JSON：{exc}", file=sys.stderr)
        return 2

    template_dir = (args.template_dir or args.config.parent).resolve()
    findings = validate_config(data, template_dir, args.strict_assets)
    for finding in findings:
        print(f"{finding.severity.upper()} {finding.location}: {finding.message}")
    errors = sum(item.severity == "error" for item in findings)
    warnings = sum(item.severity == "warning" for item in findings)
    node_count = sum(len(data.get(name, [])) for name in NODE_ARRAYS if isinstance(data.get(name), list))
    print(f"SUMMARY nodes={node_count} errors={errors} warnings={warnings}")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
