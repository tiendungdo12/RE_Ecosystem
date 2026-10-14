# axml_parser.py
"""
Trình phân tích siêu tốc Android Binary XML (AXML) & Android Package Container (.apk, .xapk, .apks)
Thuần Python, không phụ thuộc thư viện ngoài, tốc độ < 3ms.
"""

import contextlib
import io
import json
import os
import struct
import sys
import zipfile
from typing import Any, Optional, Union

_current_dir = os.path.dirname(os.path.abspath(__file__))
if _current_dir not in sys.path:
    sys.path.insert(0, _current_dir)

from malware_rules import ANDROID_DANGEROUS_PERMISSIONS, ANDROID_PACKER_SIGNATURES

MAX_STRING_POOL_ENTRIES = 100_000
MAX_TOTAL_DECODED_STRING_BYTES = 30 * 1024 * 1024  # 30 MiB safety limit for decoded string memory
MAX_AXML_ELEMENTS = 10_000          # Tối đa 10,000 XML elements trong một manifest
MAX_AXML_NESTING_DEPTH = 100        # Tối đa 100 cấp độ lồng nhau (nesting depth)
MAX_AXML_CHUNKS = 30_000           # Tối đa 30,000 chunks trong luồng AXML


class AXMLParser:
    """Giải mã định dạng Android Binary XML (AXML) thành cây đối tượng."""

    CHUNK_AXML_HEADER = 0x00080003
    CHUNK_STRING_POOL = 0x00010001
    CHUNK_RESOURCE_MAP = 0x00080180
    CHUNK_START_NAMESPACE = 0x00100100
    CHUNK_END_NAMESPACE = 0x00100101
    CHUNK_START_ELEMENT = 0x00100102
    CHUNK_END_ELEMENT = 0x00100103
    CHUNK_TEXT = 0x00100104

    # Data Types in TypedValue
    TYPE_NULL = 0x00
    TYPE_REFERENCE = 0x01
    TYPE_ATTRIBUTE = 0x02
    TYPE_STRING = 0x03
    TYPE_FLOAT = 0x04
    TYPE_DIMENSION = 0x05
    TYPE_FRACTION = 0x06
    TYPE_INT_DEC = 0x10
    TYPE_INT_HEX = 0x11
    TYPE_INT_BOOLEAN = 0x12

    def __init__(self, data: bytes):
        self.data = data
        self.strings: list[str] = []
        self.root: dict[str, Any] = {"tag": "manifest", "attributes": {}, "children": []}
        self.error: str | None = None
        self.total_decoded_bytes = 0

    def parse(self) -> dict[str, Any]:
        self.strings = []
        self.root = {"tag": "manifest", "attributes": {}, "children": []}
        self.error = None
        self.total_decoded_bytes = 0
        if len(self.data) < 8:
            self.error = f"AXML data too short ({len(self.data)} bytes)"
            return {"tag": "manifest", "attributes": {}, "children": [], "error": self.error}

        magic, file_size = struct.unpack_from("<II", self.data, 0)
        if magic != self.CHUNK_AXML_HEADER and (magic & 0xFFFF) != 0x0003:
            # Fallback: raw plain text XML
            fallback = self._parse_fallback_text_xml()
            if fallback is not None:
                self.root = fallback
                if "error" in fallback:
                    self.error = fallback["error"]
                return self.root
            self.error = f"Invalid AXML header magic: 0x{magic:08X}"
            return {"tag": "manifest", "attributes": {}, "children": [], "error": self.error}
        if file_size < 8:
            self.error = f"Declared AXML file size ({file_size}) is too small (minimum 8 bytes required)"
            return {"tag": "manifest", "attributes": {}, "children": [], "error": self.error}

        if file_size != len(self.data):
            self.error = f"Inconsistent AXML header file size: declared {file_size} bytes, actual data length {len(self.data)} bytes"

        offset = 8
        element_stack: list[dict[str, Any]] = []
        parsed_any_element = False
        root_element_count = 0
        stream_len = min(file_size, len(self.data))
        total_elements = 0
        total_chunks = 0

        while offset < stream_len:
            if offset + 8 > stream_len:
                self.error = f"Malformed or truncated AXML chunk at offset {offset}"
                break

            chunk_type, chunk_size = struct.unpack_from("<II", self.data, offset)
            if chunk_size <= 0 or offset + chunk_size > stream_len:
                self.error = f"Malformed or truncated AXML chunk at offset {offset} (size={chunk_size}, available={stream_len - offset})"
                break

            total_chunks += 1
            if total_chunks > MAX_AXML_CHUNKS:
                self.error = f"AXML chunk count ({total_chunks}) exceeds safety limit ({MAX_AXML_CHUNKS}) at offset {offset}"
                break

            if (chunk_type & 0xFFFF) == (self.CHUNK_STRING_POOL & 0xFFFF):
                self._parse_string_pool(offset)
                if self.error:
                    break
            elif (chunk_type & 0xFFFF) == (self.CHUNK_START_ELEMENT & 0xFFFF):
                if len(element_stack) >= MAX_AXML_NESTING_DEPTH:
                    self.error = f"AXML element nesting depth ({len(element_stack) + 1}) exceeds safety limit ({MAX_AXML_NESTING_DEPTH}) at offset {offset}"
                    break

                total_elements += 1
                if total_elements > MAX_AXML_ELEMENTS:
                    self.error = f"AXML element count ({total_elements}) exceeds safety limit ({MAX_AXML_ELEMENTS}) at offset {offset}"
                    break

                elem = self._parse_start_element(offset)
                if elem:
                    parsed_any_element = True
                    if element_stack:
                        element_stack[-1]["children"].append(elem)
                    else:
                        if root_element_count >= 1:
                            self.error = f"Multiple root elements detected in AXML document at offset {offset}: '{elem.get('tag')}'"
                            break
                        self.root = elem
                        root_element_count += 1
                    element_stack.append(elem)
                else:
                    if not self.error:
                        self.error = f"Malformed start-element chunk at offset {offset}"
                    break
            elif (chunk_type & 0xFFFF) == (self.CHUNK_END_ELEMENT & 0xFFFF):
                if not self._parse_end_element(offset, element_stack):
                    break

            offset += chunk_size

        if self.error:
            element_stack.clear()
            if isinstance(self.root.get("children"), list):
                self.root["children"].clear()
        elif element_stack:
            self.error = f"Unclosed XML elements remaining at end of stream: {[e.get('tag') for e in element_stack]}"
            element_stack.clear()
            if isinstance(self.root.get("children"), list):
                self.root["children"].clear()

        if not parsed_any_element and not self.error:
            self.error = "No XML elements parsed from binary AXML chunk"

        if self.error:
            self.root["error"] = self.error

        return self.root

    def _parse_string_pool(self, offset: int):
        try:
            if offset + 28 > len(self.data):
                self.error = f"Malformed string pool chunk header at offset {offset}: less than 28 bytes available"
                return

            (
                chunk_type,
                header_size,
                chunk_size,
                string_count,
                style_count,
                flags,
                strings_start,
                styles_start,
            ) = struct.unpack_from("<HHIIIIII", self.data, offset)

            chunk_end = offset + chunk_size
            if chunk_size < header_size or chunk_end > len(self.data):
                self.error = f"Malformed string pool chunk at offset {offset}: declared chunk_size={chunk_size} exceeds available data ({len(self.data) - offset} bytes)"
                return

            if header_size < 28 or header_size > chunk_size:
                self.error = f"Malformed string pool chunk at offset {offset}: invalid header_size={header_size} (chunk_size={chunk_size})"
                return

            if string_count > MAX_STRING_POOL_ENTRIES:
                self.error = f"Malformed string pool at offset {offset}: string_count ({string_count}) exceeds safety limit ({MAX_STRING_POOL_ENTRIES})"
                return

            if style_count > MAX_STRING_POOL_ENTRIES:
                self.error = f"Malformed string pool at offset {offset}: style_count ({style_count}) exceeds safety limit ({MAX_STRING_POOL_ENTRIES})"
                return

            offset_table_pos = offset + header_size
            total_offset_table_size = (string_count + style_count) * 4
            if header_size + total_offset_table_size > chunk_size:
                self.error = f"Malformed string pool at offset {offset}: offset table ({total_offset_table_size} bytes) exceeds chunk boundary (header_size={header_size}, chunk_size={chunk_size})"
                return

            if string_count > 0 and (strings_start < header_size + total_offset_table_size or strings_start > chunk_size):
                self.error = f"Malformed string pool at offset {offset}: strings_start={strings_start} is outside chunk boundaries (table ends at {header_size + total_offset_table_size}, chunk_size={chunk_size})"
                return

            if style_count > 0 and styles_start > 0 and (styles_start < header_size + total_offset_table_size or styles_start > chunk_size):
                self.error = f"Malformed string pool at offset {offset}: styles_start={styles_start} is outside chunk boundaries (table ends at {header_size + total_offset_table_size}, chunk_size={chunk_size})"
                return

            is_utf8 = bool(flags & 0x00000100)
            string_data_start = offset + strings_start

            offsets = []
            for i in range(string_count):
                str_off = struct.unpack_from("<I", self.data, offset_table_pos + i * 4)[0]
                offsets.append(str_off)

            self.strings = []
            decoded_by_offset: dict[int, str] = {}

            for str_off in offsets:
                if str_off in decoded_by_offset:
                    self.strings.append(decoded_by_offset[str_off])
                    continue

                cur_pos = string_data_start + str_off
                if cur_pos < string_data_start or cur_pos >= chunk_end:
                    self.error = f"String offset {str_off} points outside string pool chunk boundary at offset {offset} (cur_pos={cur_pos}, chunk_end={chunk_end})"
                    return

                if is_utf8:
                    # UTF-8 string pool: 1-2 bytes char count + 1-2 bytes byte length
                    if cur_pos >= chunk_end:
                        self.error = f"UTF-8 string length prefix exceeds string pool boundary at offset {offset}"
                        return
                    char_len = self.data[cur_pos]
                    cur_pos += 1
                    if char_len & 0x80:
                        if cur_pos >= chunk_end:
                            self.error = f"UTF-8 string multi-byte char length exceeds string pool boundary at offset {offset}"
                            return
                        cur_pos += 1

                    if cur_pos >= chunk_end:
                        self.error = f"UTF-8 string byte length prefix exceeds string pool boundary at offset {offset}"
                        return
                    byte_len = self.data[cur_pos]
                    cur_pos += 1
                    if byte_len & 0x80:
                        if cur_pos >= chunk_end:
                            self.error = f"UTF-8 string multi-byte byte length exceeds string pool boundary at offset {offset}"
                            return
                        byte_len = ((byte_len & 0x7F) << 8) | self.data[cur_pos]
                        cur_pos += 1

                    # String data must be followed by a null terminator (0x00) within the chunk boundary
                    if cur_pos + byte_len >= chunk_end:
                        self.error = f"UTF-8 string at offset {str_off} exceeds string pool chunk boundary at offset {offset} (length={byte_len}, available={chunk_end - cur_pos})"
                        return

                    if self.data[cur_pos + byte_len] != 0:
                        self.error = f"UTF-8 string at offset {str_off} is missing null terminator within string pool chunk at offset {offset}"
                        return

                    if self.total_decoded_bytes + byte_len > MAX_TOTAL_DECODED_STRING_BYTES:
                        self.error = f"String pool cumulative decoded bytes exceeds safety limit ({MAX_TOTAL_DECODED_STRING_BYTES} bytes) at offset {offset}"
                        return

                    self.total_decoded_bytes += byte_len
                    raw_str = self.data[cur_pos : cur_pos + byte_len]
                    decoded_str = raw_str.decode("utf-8", errors="replace")
                    decoded_by_offset[str_off] = decoded_str
                    self.strings.append(decoded_str)
                else:
                    # UTF-16LE string pool: 2 bytes (or 4 bytes) length prefix
                    if cur_pos + 2 > chunk_end:
                        self.error = f"UTF-16LE string length prefix exceeds string pool boundary at offset {offset}"
                        return
                    char_len = struct.unpack_from("<H", self.data, cur_pos)[0]
                    cur_pos += 2
                    if char_len & 0x8000:
                        if cur_pos + 2 > chunk_end:
                            self.error = f"UTF-16LE string extended length prefix exceeds string pool boundary at offset {offset}"
                            return
                        char_len = ((char_len & 0x7FFF) << 16) | struct.unpack_from("<H", self.data, cur_pos)[0]
                        cur_pos += 2

                    byte_len = char_len * 2
                    # String data must be followed by a 2-byte null terminator (\x00\x00) within the chunk boundary
                    if cur_pos + byte_len + 2 > chunk_end:
                        self.error = f"UTF-16LE string at offset {str_off} exceeds string pool chunk boundary at offset {offset} (byte_len={byte_len}, available={chunk_end - cur_pos})"
                        return

                    if self.data[cur_pos + byte_len : cur_pos + byte_len + 2] != b"\x00\x00":
                        self.error = f"UTF-16LE string at offset {str_off} is missing null terminator within string pool chunk at offset {offset}"
                        return

                    if self.total_decoded_bytes + byte_len > MAX_TOTAL_DECODED_STRING_BYTES:
                        self.error = f"String pool cumulative decoded bytes exceeds safety limit ({MAX_TOTAL_DECODED_STRING_BYTES} bytes) at offset {offset}"
                        return

                    self.total_decoded_bytes += byte_len
                    raw_str = self.data[cur_pos : cur_pos + byte_len]
                    decoded_str = raw_str.decode("utf-16le", errors="replace")
                    decoded_by_offset[str_off] = decoded_str
                    self.strings.append(decoded_str)
        except Exception as e:
            self.error = f"Malformed string pool chunk at offset {offset}: {e}"

    def _get_string(self, idx: int) -> str:
        if 0 <= idx < len(self.strings):
            return self.strings[idx]
        return ""

    def _parse_start_element(self, offset: int) -> Optional[dict[str, Any]]:
        try:
            if offset + 36 > len(self.data):
                self.error = f"Malformed or truncated start-element header at offset {offset}"
                return None

            # Struct: chunk_type(2), header_size(2), chunk_size(4), line_number(4), comment(4), ns_idx(4), name_idx(4),
            # attr_start(2), attr_size(2), attr_count(2), id_idx(2), class_idx(2), style_idx(2)
            (
                chunk_type,
                header_size,
                chunk_size,
                line_no,
                comment,
                ns_idx,
                name_idx,
                attr_start,
                attr_size,
                attr_count,
                id_idx,
                class_idx,
                style_idx,
            ) = struct.unpack_from("<HHIIIIIHHHHHH", self.data, offset)

            if chunk_size < 36:
                self.error = f"Invalid start-element chunk size {chunk_size} at offset {offset} (minimum 36)"
                return None

            if header_size < 16:
                self.error = f"Invalid start-element header size {header_size} at offset {offset} (expected 16)"
                return None

            if header_size > chunk_size:
                self.error = f"Invalid start-element header size {header_size} exceeds chunk size {chunk_size} at offset {offset}"
                return None

            if name_idx < 0 or name_idx >= len(self.strings):
                self.error = f"Invalid start-element name index {name_idx} at offset {offset}"
                return None

            tag_name = self._get_string(name_idx)
            if not tag_name:
                self.error = f"Empty start-element tag name for name index {name_idx} at offset {offset}"
                return None

            attributes = {}

            if attr_start >= header_size + 20:
                attr_offset = offset + attr_start
            else:
                attr_offset = offset + header_size + attr_start

            if attr_offset < offset + 36:
                attr_offset = offset + 36

            if attr_count > 0 and attr_size < 20:
                self.error = f"Invalid start-element attribute size {attr_size} at offset {offset} (minimum 20)"
                return None

            expected_attr_end = attr_offset + attr_count * attr_size

            if expected_attr_end > offset + chunk_size or expected_attr_end > len(self.data):
                self.error = (
                    f"Malformed start-element attributes at offset {offset}: "
                    f"declared {attr_count} attributes (size={attr_size}) exceed chunk boundary "
                    f"(chunk_size={chunk_size}, available={len(self.data) - offset})"
                )
                return None
            elif expected_attr_end < offset + chunk_size:
                self.error = (
                    f"Malformed start-element chunk at offset {offset}: "
                    f"declared chunk_size={chunk_size} exceeds attribute table boundary "
                    f"(expected {expected_attr_end - offset} bytes for {attr_count} attributes, "
                    f"slack={offset + chunk_size - expected_attr_end} bytes)"
                )
                return None

            for i in range(attr_count):
                pos = attr_offset + i * attr_size
                if pos + 20 > len(self.data) or pos + 20 > offset + chunk_size:
                    self.error = f"Truncated attribute at offset {pos}"
                    return None
                (
                    a_ns_idx,
                    a_name_idx,
                    a_val_str_idx,
                    a_val_size,
                    a_res0,
                    a_data_type,
                    a_data,
                ) = struct.unpack_from("<IIIHBBI", self.data, pos)

                if a_name_idx < 0 or a_name_idx >= len(self.strings):
                    self.error = f"Invalid attribute name index {a_name_idx} at offset {pos}"
                    return None

                attr_name = self._get_string(a_name_idx)
                if not attr_name:
                    self.error = f"Empty attribute name for name index {a_name_idx} at offset {pos}"
                    return None

                attr_value = ""
                if a_val_str_idx != 0xFFFFFFFF:
                    if a_val_str_idx < 0 or a_val_str_idx >= len(self.strings):
                        self.error = f"Invalid attribute string value index {a_val_str_idx} at offset {pos}"
                        return None
                    attr_value = self._get_string(a_val_str_idx)
                elif a_data_type == self.TYPE_INT_BOOLEAN:
                    attr_value = "true" if a_data != 0 else "false"
                elif a_data_type in (self.TYPE_INT_DEC, self.TYPE_INT_HEX):
                    attr_value = str(a_data)
                elif a_data_type == self.TYPE_REFERENCE:
                    attr_value = f"@0x{a_data:08x}"
                else:
                    attr_value = str(a_data)

                attributes[attr_name] = attr_value

            return {"tag": tag_name, "attributes": attributes, "children": []}
        except Exception as e:
            self.error = f"Malformed start-element at offset {offset}: {e}"
            return None

    def _parse_end_element(self, offset: int, element_stack: list[dict[str, Any]]) -> bool:
        try:
            if offset + 24 > len(self.data):
                self.error = f"Malformed end-element chunk at offset {offset}: less than 24 bytes available"
                return False

            (
                chunk_type,
                header_size,
                chunk_size,
                line_no,
                comment,
                ns_idx,
                name_idx,
            ) = struct.unpack_from("<HHIIIII", self.data, offset)

            if chunk_size < 24:
                self.error = f"Invalid end-element chunk size {chunk_size} at offset {offset} (minimum 24 bytes required)"
                return False

            if header_size < 16:
                self.error = f"Invalid end-element header size {header_size} at offset {offset} (expected 16)"
                return False

            if header_size > chunk_size:
                self.error = f"Invalid end-element header size {header_size} exceeds chunk size {chunk_size} at offset {offset}"
                return False

            if name_idx < 0 or name_idx >= len(self.strings):
                self.error = f"Invalid end-element name index {name_idx} at offset {offset}"
                return False

            tag_name = self._get_string(name_idx)
            if not tag_name:
                self.error = f"Empty end-element tag name for name index {name_idx} at offset {offset}"
                return False

            if not element_stack:
                self.error = f"Unexpected end-element '{tag_name}' without matching start-element at offset {offset}"
                return False

            expected_tag = element_stack[-1].get("tag")
            if tag_name != expected_tag:
                self.error = f"Mismatched end-element tag '{tag_name}' does not match expected '{expected_tag}' at offset {offset}"
                return False

            element_stack.pop()
            return True
        except Exception as e:
            self.error = f"Malformed end-element at offset {offset}: {e}"
            return False

    def _parse_fallback_text_xml(self) -> Optional[dict[str, Any]]:
        try:
            import io
            import xml.etree.ElementTree as ET

            stripped = self.data.lstrip()
            is_utf16 = False
            if stripped.startswith(b"\xef\xbb\xbf"):
                stripped = stripped[3:].lstrip()
            elif stripped.startswith(b"\xff\xfe") or stripped.startswith(b"\xfe\xff"):
                is_utf16 = True

            if not is_utf16 and not stripped.startswith(b"<"):
                return None

            total_elements = 0
            current_depth = 0
            element_stack: list[dict[str, Any]] = []
            root_node: Optional[dict[str, Any]] = None

            # Stream parse using iterparse to enforce budgets on-the-fly BEFORE allocating the full tree
            for event, elem in ET.iterparse(io.BytesIO(stripped), events=("start", "end")):
                if event == "start":
                    total_elements += 1
                    current_depth += 1
                    if current_depth > MAX_AXML_NESTING_DEPTH:
                        elem.clear()
                        raise ValueError(
                            f"AXML element nesting depth ({current_depth}) exceeds safety limit ({MAX_AXML_NESTING_DEPTH})"
                        )
                    if total_elements > MAX_AXML_ELEMENTS:
                        elem.clear()
                        raise ValueError(
                            f"AXML element count ({total_elements}) exceeds safety limit ({MAX_AXML_ELEMENTS})"
                        )

                    tag = elem.tag.split("}")[-1] if "}" in elem.tag else elem.tag
                    attrs = {k.split("}")[-1] if "}" in k else k: v for k, v in elem.attrib.items()}
                    node: dict[str, Any] = {"tag": tag, "attributes": attrs, "children": []}

                    if element_stack:
                        element_stack[-1]["children"].append(node)
                    else:
                        root_node = node
                    element_stack.append(node)
                elif event == "end":
                    current_depth -= 1
                    if element_stack:
                        element_stack.pop()
                    elem.clear()

            return root_node
        except ValueError as val_err:
            self.error = str(val_err)
            return {"tag": "manifest", "attributes": {}, "children": [], "error": self.error}
        except ET.ParseError as parse_err:
            self.error = f"Malformed plain-text XML manifest: {parse_err}"
            return {"tag": "manifest", "attributes": {}, "children": [], "error": self.error}
        except Exception as ex:
            self.error = f"Plain-text XML parsing failed: {ex}"
            return {"tag": "manifest", "attributes": {}, "children": [], "error": self.error}


def extract_manifest_info(manifest_bytes: bytes) -> dict[str, Any]:
    """Trích xuất siêu dữ liệu quan trọng từ AndroidManifest.xml (AXML)."""
    parser = AXMLParser(manifest_bytes)
    root = parser.parse()

    manifest_attrs = root.get("attributes", {})
    package_name = manifest_attrs.get("package", "unknown")
    version_code = manifest_attrs.get("versionCode", "unknown")
    version_name = manifest_attrs.get("versionName", "unknown")

    min_sdk = None
    target_sdk = None
    permissions: list[str] = []
    dangerous_permissions: list[dict[str, str]] = []
    main_activity = "unknown"
    activities: list[str] = []
    services: list[str] = []
    receivers: list[str] = []
    providers: list[str] = []

    for child in root.get("children", []):
        tag = child.get("tag", "")
        attrs = child.get("attributes", {})

        if tag == "uses-sdk":
            min_sdk = attrs.get("minSdkVersion")
            target_sdk = attrs.get("targetSdkVersion")

        elif tag in ("uses-permission", "permission"):
            perm_name = attrs.get("name", "")
            if perm_name and perm_name not in permissions:
                permissions.append(perm_name)
                if perm_name in ANDROID_DANGEROUS_PERMISSIONS:
                    info = ANDROID_DANGEROUS_PERMISSIONS[perm_name]
                    dangerous_permissions.append({
                        "name": perm_name,
                        "risk": info["risk"],
                        "description": info["desc"],
                    })

        elif tag == "application":
            for app_child in child.get("children", []):
                app_tag = app_child.get("tag", "")
                app_attrs = app_child.get("attributes", {})
                name = app_attrs.get("name", "")

                if app_tag == "activity":
                    activities.append(name)
                    # Check for main launcher activity
                    is_main = False
                    is_launcher = False
                    for filter_tag in app_child.get("children", []):
                        if filter_tag.get("tag") == "intent-filter":
                            for action_or_cat in filter_tag.get("children", []):
                                a_name = action_or_cat.get("attributes", {}).get("name", "")
                                if a_name == "android.intent.action.MAIN":
                                    is_main = True
                                elif a_name == "android.intent.category.LAUNCHER":
                                    is_launcher = True
                    if is_main and is_launcher:
                        main_activity = name

                elif app_tag == "service":
                    services.append(name)
                elif app_tag == "receiver":
                    receivers.append(name)
                elif app_tag == "provider":
                    providers.append(name)

    if main_activity == "unknown" and activities:
        main_activity = activities[0]

    res = {
        "package_name": package_name,
        "version_code": version_code,
        "version_name": version_name,
        "min_sdk": min_sdk,
        "target_sdk": target_sdk,
        "main_activity": main_activity,
        "permissions_count": len(permissions),
        "permissions": permissions,
        "dangerous_permissions": dangerous_permissions,
        "components_count": {
            "activities": len(activities),
            "services": len(services),
            "receivers": len(receivers),
            "providers": len(providers),
        },
    }

    if parser.error or "error" in root:
        res["error"] = parser.error or root.get("error")
    elif package_name == "unknown":
        res["error"] = "AndroidManifest.xml does not declare a valid package attribute"

    return res


MAX_MANIFEST_SIZE = 30 * 1024 * 1024       # 30 MiB
MAX_INNER_APK_SIZE = 200 * 1024 * 1024     # 200 MiB
MAX_JSON_META_SIZE = 10 * 1024 * 1024      # 10 MiB
MAX_ZIP_ENTRIES = 20_000                   # 20,000 entries max per archive
MAX_ZIP_CD_SIZE = 16 * 1024 * 1024         # 16 MiB max central directory size
MAX_SPLIT_APKS = 50                        # 50 split APKs max per bundle
MAX_BUNDLE_TOTAL_INNER_APK_SIZE = 500 * 1024 * 1024  # 500 MiB max total inner APK bytes across bundle


def _inspect_zip_bounds(
    source: Union[str, os.PathLike, bytes, bytearray, memoryview, Any],
    include_offset: bool = False,
) -> Union[tuple[int, int], tuple[int, int, int]]:
    """
    Trích xuất (entry_count, cd_size) hoặc (entry_count, cd_size, cd_offset) từ End of Central Directory (EOCD/ZIP64)
    mà KHÔNG materialize hay nạp các đối tượng ZipInfo vào bộ nhớ.
    """
    filesize: Optional[int] = None
    if isinstance(source, (str, os.PathLike)):
        filesize = os.path.getsize(source)
        with open(source, "rb") as fp:
            endrec = zipfile._EndRecData(fp)
    elif isinstance(source, (bytes, bytearray, memoryview)):
        filesize = len(source)
        endrec = zipfile._EndRecData(io.BytesIO(source))
    elif hasattr(source, "seek") and hasattr(source, "read"):
        orig_pos = source.tell()
        source.seek(0, io.SEEK_END)
        filesize = source.tell()
        source.seek(0)
        try:
            endrec = zipfile._EndRecData(source)
        finally:
            source.seek(orig_pos)
    else:
        raise TypeError(f"Unsupported zip source type: {type(source)}")

    if not endrec:
        raise zipfile.BadZipFile("File is not a zip file or End of Central Directory record not found")

    entry_count = endrec[zipfile._ECD_ENTRIES_TOTAL]
    cd_size = endrec[zipfile._ECD_SIZE]
    cd_offset = endrec[zipfile._ECD_OFFSET]

    if filesize is not None and (cd_offset < 0 or cd_size < 0 or cd_offset + cd_size > filesize):
        raise zipfile.BadZipFile("Central directory offset and size exceed archive file bounds")

    if include_offset:
        return entry_count, cd_size, cd_offset
    return entry_count, cd_size


def _count_cd_entries(
    source: Union[str, os.PathLike, bytes, bytearray, memoryview, Any],
    cd_offset: int,
    cd_size: int,
    limit: int,
) -> int:
    """
    Đếm số lượng entry thực tế trong Central Directory có early exit tại limit + 1.
    Chạy ở tốc độ cao (< 5ms) và bộ nhớ cực thấp (< 2 KB), không khởi tạo ZipInfo hay chuỗi tên file.
    Ngăn chặn việc giả mạo _ECD_ENTRIES_TOTAL trong EOCD để gây memory amplification.
    """
    def _scan_stream(fp) -> int:
        fp.seek(cd_offset)
        count = 0
        bytes_read = 0
        while bytes_read + 46 <= cd_size:
            hdr = fp.read(46)
            if len(hdr) < 46 or hdr[:4] != b"PK\x01\x02":
                break
            fn_len, extra_len, com_len = struct.unpack_from("<HHH", hdr, 28)
            skip = fn_len + extra_len + com_len
            fp.seek(skip, io.SEEK_CUR)
            bytes_read += 46 + skip
            count += 1
            if count > limit:
                return count
        return count

    if isinstance(source, (str, os.PathLike)):
        with open(source, "rb") as fp:
            return _scan_stream(fp)
    elif isinstance(source, (bytes, bytearray, memoryview)):
        mv = memoryview(source)
        pos = cd_offset
        end = cd_offset + cd_size
        count = 0
        while pos + 46 <= end:
            if mv[pos : pos + 4] != b"PK\x01\x02":
                break
            fn_len, extra_len, com_len = struct.unpack_from("<HHH", mv, pos + 28)
            pos += 46 + fn_len + extra_len + com_len
            count += 1
            if count > limit:
                return count
        return count
    elif hasattr(source, "seek") and hasattr(source, "read"):
        orig_pos = source.tell()
        try:
            return _scan_stream(source)
        finally:
            source.seek(orig_pos)
    else:
        raise TypeError(f"Unsupported zip source type: {type(source)}")


def _validate_zip_archive(
    source: Union[str, os.PathLike, bytes, bytearray, memoryview, Any],
    max_entries: Optional[int] = None,
    max_cd_size: Optional[int] = None,
) -> tuple[int, int]:
    """
    Kiểm tra an toàn cấu trúc ZIP trước khi nạp metadata archive.
    Ngăn chặn amplification DoS từ archive có số lượng entry hoặc central directory vượt ngưỡng an toàn.
    """
    limit_entries = MAX_ZIP_ENTRIES if max_entries is None else max_entries
    limit_cd_size = MAX_ZIP_CD_SIZE if max_cd_size is None else max_cd_size
    entry_count, cd_size, cd_offset = _inspect_zip_bounds(source, include_offset=True)

    if entry_count > limit_entries:
        raise ValueError(
            f"Zip archive entry count ({entry_count}) exceeds safety limit ({limit_entries})"
        )
    if cd_size > limit_cd_size:
        raise ValueError(
            f"Zip archive central directory size ({cd_size} bytes) exceeds safety limit ({limit_cd_size})"
        )

    # Nếu Central Directory có kích thước đủ lớn để chứa nhiều hơn limit_entries records (mỗi record >= 46 bytes),
    # quét nhanh Central Directory để xác minh số entry thực tế, ngăn ngừa EOCD bị giả mạo entry_count.
    if cd_size // 46 > limit_entries:
        actual_entries = _count_cd_entries(source, cd_offset, cd_size, limit_entries)
        if actual_entries > limit_entries:
            raise ValueError(
                f"Zip archive entry count ({actual_entries}) exceeds safety limit ({limit_entries})"
            )

    return entry_count, cd_size


@contextlib.contextmanager
def _safe_open_zip(
    source: Union[str, os.PathLike, bytes, bytearray, memoryview, Any],
    max_entries: Optional[int] = None,
    max_cd_size: Optional[int] = None,
):
    """
    Mở zipfile.ZipFile an toàn có kiểm tra giới hạn entry count và metadata CD
    cả TRƯỚC khi khởi tạo ZipFile và SAU khi đọc Central Directory (defense-in-depth).
    """
    limit_entries = MAX_ZIP_ENTRIES if max_entries is None else max_entries
    limit_cd_size = MAX_ZIP_CD_SIZE if max_cd_size is None else max_cd_size
    _validate_zip_archive(source, max_entries=limit_entries, max_cd_size=limit_cd_size)

    if isinstance(source, (bytes, bytearray, memoryview)):
        zf = zipfile.ZipFile(io.BytesIO(source), "r")
    else:
        zf = zipfile.ZipFile(source, "r")

    try:
        if len(zf.filelist) > limit_entries:
            raise ValueError(
                f"Zip archive entry count ({len(zf.filelist)}) exceeds safety limit ({limit_entries})"
            )
        yield zf
    finally:
        zf.close()



def _safe_read_entry(zf: zipfile.ZipFile, name: str, max_size: int) -> bytes:
    """Đọc zip entry an toàn có giới hạn kích thước chống zip bomb và cạn kiệt bộ nhớ."""
    info = zf.getinfo(name)
    if info.file_size > max_size:
        raise ValueError(
            f"Zip entry '{name}' uncompressed size ({info.file_size} bytes) exceeds safety limit ({max_size} bytes)"
        )
    with zf.open(name) as stream:
        data = stream.read(max_size + 1)
        if len(data) > max_size:
            raise ValueError(f"Zip entry '{name}' exceeds safety limit ({max_size} bytes)")
        return data


def _find_base_apk(namelist: list[str]) -> Optional[str]:
    """
    Tìm base APK trong danh sách file của container (.apks / .xapk) độc lập hoàn toàn với thứ tự archive.
    Ưu tiên:
    1. Trùng khớp chính xác các tên chuẩn ('base.apk', 'splits/base-master.apk', 'base-master.apk', 'splits/base.apk', 'master.apk', 'standalone.apk', 'universal.apk')
       hoặc basename là một trong các tên này.
    2. Basename chứa chữ 'base', 'master', 'standalone', hoặc 'universal' (không bắt đầu bằng 'config.', 'split_config.', hoặc 'split_').
    (Tuyệt đối không nhận config.*.apk, split_config.*.apk, hoặc feature splits làm base APK).
    """
    if len(namelist) > MAX_ZIP_ENTRIES:
        namelist = namelist[:MAX_ZIP_ENTRIES]
    exact_names = {
        "base.apk",
        "splits/base-master.apk",
        "base-master.apk",
        "splits/base.apk",
        "master.apk",
        "standalone.apk",
        "universal.apk",
    }
    for f in namelist:
        if f.lower() in exact_names:
            return f

    valid_basenames = {
        "base.apk",
        "base-master.apk",
        "master.apk",
        "standalone.apk",
        "universal.apk",
    }
    for f in namelist:
        base = os.path.basename(f).lower()
        if base in valid_basenames:
            return f

    base_keywords = ("base", "master", "standalone", "universal")
    for f in namelist:
        base = os.path.basename(f).lower()
        if (
            base.endswith(".apk")
            and any(k in base for k in base_keywords)
            and not base.startswith("config.")
            and not base.startswith("split_config.")
            and not base.startswith("split_")
        ):
            return f

    return None


def _apply_manifest_info(result: dict[str, Any], m_info: dict[str, Any], is_base: bool = True):
    """Áp dụng và tổng hợp thông tin trích xuất từ AndroidManifest.xml vào kết quả container."""
    if "error" in m_info and m_info["error"]:
        if not result.get("error"):
            result["error"] = m_info["error"]

    if is_base:
        if result.get("package_name") == "unknown" and m_info.get("package_name") != "unknown":
            result["package_name"] = m_info["package_name"]
        if result.get("version_code") == "unknown" and m_info.get("version_code") != "unknown":
            result["version_code"] = str(m_info["version_code"])
        if result.get("version_name") == "unknown" and m_info.get("version_name") != "unknown":
            result["version_name"] = str(m_info["version_name"])

        if result.get("min_sdk") is None:
            result["min_sdk"] = m_info.get("min_sdk")
        if result.get("target_sdk") is None:
            result["target_sdk"] = m_info.get("target_sdk")
        if result.get("main_activity") == "unknown" and m_info.get("main_activity") != "unknown":
            result["main_activity"] = m_info.get("main_activity", "unknown")

    # Permissions aggregation
    current_perms = set(result.get("permissions", []))
    for p in m_info.get("permissions", []):
        current_perms.add(p)
    result["permissions"] = sorted(list(current_perms))
    result["permissions_count"] = len(result["permissions"])

    # Dangerous permissions aggregation (keyed by permission name)
    current_dang = {
        dp["name"]: dp
        for dp in result.get("dangerous_permissions", [])
        if isinstance(dp, dict) and "name" in dp
    }
    for dp in m_info.get("dangerous_permissions", []):
        if isinstance(dp, dict) and "name" in dp:
            current_dang[dp["name"]] = dp
    result["dangerous_permissions"] = list(current_dang.values())

    # Components count aggregation
    comp = result.setdefault("components_count", {
        "activities": 0, "services": 0, "receivers": 0, "providers": 0
    })
    m_comp = m_info.get("components_count", {})
    comp["activities"] = comp.get("activities", 0) + m_comp.get("activities", 0)
    comp["services"] = comp.get("services", 0) + m_comp.get("services", 0)
    comp["receivers"] = comp.get("receivers", 0) + m_comp.get("receivers", 0)
    comp["providers"] = comp.get("providers", 0) + m_comp.get("providers", 0)


def parse_android_container(path: str) -> dict[str, Any]:
    """
    Phân tích toàn diện container Android (.apk, .xapk, .apks).
    Tự động xử lý multi-APK trong XAPK/APKS và trích xuất base.apk độc lập với thứ tự archive.
    """
    ext = os.path.splitext(path)[1].lower()
    result = {
        "format": ext.strip("."),
        "package_name": "unknown",
        "version_code": "unknown",
        "version_name": "unknown",
        "main_activity": "unknown",
        "min_sdk": None,
        "target_sdk": None,
        "permissions_count": 0,
        "permissions": [],
        "dangerous_permissions": [],
        "dex_count": 0,
        "native_architectures": [],
        "native_libraries": [],
        "engine": "unknown",
        "packer": "none",
        "is_split_package": ext in (".xapk", ".apks"),
        "split_apks": [],
        "components_count": {
            "activities": 0, "services": 0, "receivers": 0, "providers": 0
        },
    }

    if not os.path.exists(path) or not os.path.isfile(path):
        result["error"] = f"Container file not found or is not a regular file: {path}"
        return result

    try:
        with _safe_open_zip(path) as zf:
            namelist = zf.namelist()

            manifest_bytes = None

            # 1. Handle XAPK / APKS
            if ext in (".xapk", ".apks"):
                if ext == ".xapk" and "manifest.json" in namelist:
                    try:
                        xapk_meta = json.loads(_safe_read_entry(zf, "manifest.json", MAX_JSON_META_SIZE).decode("utf-8", errors="ignore"))
                        result["package_name"] = xapk_meta.get("package_name", "unknown")
                        result["version_code"] = str(xapk_meta.get("version_code", "unknown"))
                        result["version_name"] = str(xapk_meta.get("version_name", "unknown"))
                    except Exception:
                        pass

                all_apks = [f for f in namelist if f.endswith(".apk")]
                if len(all_apks) > MAX_SPLIT_APKS:
                    raise ValueError(
                        f"Split APK entries count ({len(all_apks)}) exceeds safety limit ({MAX_SPLIT_APKS})"
                    )
                result["split_apks"] = all_apks
                base_apk_name = _find_base_apk(namelist)

                cumulative_entries = len(namelist)
                cumulative_inner_bytes = 0

                if base_apk_name:
                    base_bytes = _safe_read_entry(zf, base_apk_name, MAX_INNER_APK_SIZE)
                    cumulative_inner_bytes += len(base_bytes)
                    with _safe_open_zip(base_bytes) as inner_zf:
                        cumulative_entries += len(inner_zf.filelist)
                        if cumulative_entries > MAX_ZIP_ENTRIES:
                            raise ValueError(
                                f"Cumulative bundle archive entries count ({cumulative_entries}) exceeds safety limit ({MAX_ZIP_ENTRIES})"
                            )
                        if "AndroidManifest.xml" in inner_zf.namelist():
                            manifest_bytes = _safe_read_entry(inner_zf, "AndroidManifest.xml", MAX_MANIFEST_SIZE)
                        _scan_apk_structure(inner_zf.namelist(), result)

                    if manifest_bytes:
                        m_info = extract_manifest_info(manifest_bytes)
                        _apply_manifest_info(result, m_info, is_base=True)
                        manifest_bytes = None
                    else:
                        result["error"] = f"AndroidManifest.xml not found in base APK '{base_apk_name}'"

                    # Multi-split manifest and structure scanning
                    for split_name in all_apks:
                        if split_name == base_apk_name:
                            continue
                        split_bytes = _safe_read_entry(zf, split_name, MAX_INNER_APK_SIZE)
                        cumulative_inner_bytes += len(split_bytes)
                        if cumulative_inner_bytes > MAX_BUNDLE_TOTAL_INNER_APK_SIZE:
                            raise ValueError(
                                f"Cumulative split APK uncompressed size ({cumulative_inner_bytes} bytes) exceeds safety limit ({MAX_BUNDLE_TOTAL_INNER_APK_SIZE} bytes)"
                            )
                        with _safe_open_zip(split_bytes) as split_zf:
                            cumulative_entries += len(split_zf.filelist)
                            if cumulative_entries > MAX_ZIP_ENTRIES:
                                raise ValueError(
                                    f"Cumulative split archive entries count ({cumulative_entries}) exceeds safety limit ({MAX_ZIP_ENTRIES})"
                                )
                            _scan_apk_structure(split_zf.namelist(), result)
                            if "AndroidManifest.xml" in split_zf.namelist():
                                split_m_bytes = _safe_read_entry(split_zf, "AndroidManifest.xml", MAX_MANIFEST_SIZE)
                                split_m_info = extract_manifest_info(split_m_bytes)
                                _apply_manifest_info(result, split_m_info, is_base=False)
                else:
                    if not all_apks:
                        result["error"] = "No valid APK entries found in bundle archive"
                    else:
                        result["error"] = "No base APK found in bundle archive (missing base.apk or master APK)"

            else:
                # Standard APK
                if "AndroidManifest.xml" in namelist:
                    manifest_bytes = _safe_read_entry(zf, "AndroidManifest.xml", MAX_MANIFEST_SIZE)
                _scan_apk_structure(namelist, result)

                if manifest_bytes:
                    m_info = extract_manifest_info(manifest_bytes)
                    _apply_manifest_info(result, m_info, is_base=True)
                else:
                    result["error"] = "AndroidManifest.xml not found in archive"

    except Exception as e:
        result["error"] = str(e)

    return result


def _scan_apk_structure(namelist: list[str], result: dict[str, Any]):
    """Quét các thành phần cấu trúc của file APK (DEX, lib, assets, Engine, Packer)."""
    if len(namelist) > MAX_ZIP_ENTRIES:
        namelist = namelist[:MAX_ZIP_ENTRIES]
    dex_files = [f for f in namelist if f.startswith("classes") and f.endswith(".dex")]
    result["dex_count"] = max(result.get("dex_count", 0), len(dex_files))

    native_archs = set(result.get("native_architectures", []))
    native_libs = set(result.get("native_libraries", []))
    all_names = set(namelist)

    for item in namelist:
        if item.startswith("lib/"):
            parts = item.split("/")
            if len(parts) >= 2:
                native_archs.add(parts[1])
            if len(parts) >= 3 and parts[2].endswith(".so"):
                so_name = parts[2]
                native_libs.add(so_name)

                # Check Packer Signatures
                if so_name in ANDROID_PACKER_SIGNATURES:
                    result["packer"] = ANDROID_PACKER_SIGNATURES[so_name]

    result["native_architectures"] = sorted(list(native_archs))
    result["native_libraries"] = sorted(list(native_libs))[:20]  # limit to top 20 to avoid token bloat

    # Detect Engine
    if "libil2cpp.so" in native_libs or "assets/bin/Data/global-metadata.dat" in all_names or "assets/bin/Data/Managed" in all_names or any("libil2cpp.so" in f for f in namelist):
        result["engine"] = "unity_il2cpp"
    elif "libflutter.so" in native_libs or "libapp.so" in native_libs or any("libflutter.so" in f or "libapp.so" in f for f in namelist):
        result["engine"] = "flutter_dart"
    elif "assets/index.android.bundle" in all_names or "libhermes.so" in native_libs or any("libhermes.so" in f for f in namelist):
        result["engine"] = "react_native"
    elif "libcocos2djs.so" in native_libs or "libcocos2dlua.so" in native_libs or any("libcocos2djs.so" in f or "libcocos2dlua.so" in f for f in namelist):
        result["engine"] = "cocos2d"
    elif "libmonosgen-2.0.so" in native_libs or "libmono-native.so" in native_libs or any("libmonosgen-2.0.so" in f or "libmono-native.so" in f for f in namelist):
        result["engine"] = "unity_mono"
