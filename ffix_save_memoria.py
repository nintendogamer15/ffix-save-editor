#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""
Reader/writer for the save format written by the Memoria mod
(https://github.com/Albeoris/Memoria) for Final Fantasy IX, e.g.
`SavedData_ww_Memoria_0_0.dat` / `SavedData_ww_Memoria_Autosave.dat`.

This is an entirely different, *unencrypted* format from the vanilla
Steam/PC/mobile rr2016 save container (`SavedData_ww.dat` itself, without
the "_Memoria_..." suffix, is still the standard AES-encrypted vanilla
container handled by ffix_save_tool.py's rr2016 path) — Memoria writes its
own extra save/autosave slots in a self-describing, generic tagged binary
tree format, apparently a hand-rolled variant of the classic
System.IO.BinaryWriter/BinaryReader convention (7-bit-encoded string length
prefixes, little-endian fixed-width numbers). None of this was covered by
the reference "Memoria FF9 Save Editor" project mentioned in NOTICES.md —
it was reverse-engineered directly from two real save files for this
project, and validated by confirming a parse-then-reserialize round trip is
byte-for-byte identical to the original file for both samples.

Known type tags (the only ones observed in practice):
    1  array       int32 count, then that many tagged values
    2  dictionary  int32 count, then that many (7-bit-len string key, tagged value)
    3  string      7-bit-encoded length prefix + UTF-8 bytes
    4  int32       4 bytes, little-endian
    5  double      8 bytes, little-endian, IEEE754

The tree round-trips through this module as plain Python dict/list/str/
int/float — editing a value in place and re-serializing preserves every
other byte, including original key order, as long as the edited value's
Python type (str/int/float) matches what was there before.
"""
from __future__ import annotations

import copy
import struct

import ffix_save_data as data


class MemoriaFormatError(ValueError):
    pass


class _Reader:
    def __init__(self, buf: bytes) -> None:
        self.buf = buf
        self.pos = 0

    def _take(self, n: int) -> bytes:
        chunk = self.buf[self.pos:self.pos + n]
        if len(chunk) != n:
            raise MemoriaFormatError(f"unexpected end of data at offset {self.pos}")
        self.pos += n
        return chunk

    def i32(self) -> int:
        return struct.unpack("<i", self._take(4))[0]

    def f64(self) -> float:
        return struct.unpack("<d", self._take(8))[0]

    def _net_7bit_len(self) -> int:
        result = 0
        shift = 0
        while True:
            if shift >= 35:
                raise MemoriaFormatError(f"invalid 7-bit length at offset {self.pos}")
            b = self._take(1)[0]
            result |= (b & 0x7F) << shift
            if not (b & 0x80):
                return result
            shift += 7

    def string(self) -> str:
        n = self._net_7bit_len()
        return self._take(n).decode("utf-8")

    def value(self):
        tag = self.i32()
        if tag == 1:
            count = self.i32()
            if count < 0 or count > (len(self.buf) - self.pos) // 4:
                raise MemoriaFormatError(f"invalid array count {count} at offset {self.pos - 4}")
            return [self.value() for _ in range(count)]
        if tag == 2:
            count = self.i32()
            if count < 0 or count > (len(self.buf) - self.pos) // 5:
                raise MemoriaFormatError(f"invalid dictionary count {count} at offset {self.pos - 4}")
            out: dict = {}
            for _ in range(count):
                key = self.string()
                if key in out:
                    raise MemoriaFormatError(f"duplicate dictionary key {key!r}")
                out[key] = self.value()
            return out
        if tag == 3:
            return self.string()
        if tag == 4:
            return self.i32()
        if tag == 5:
            return self.f64()
        raise MemoriaFormatError(f"unknown type tag {tag} at offset {self.pos - 4}")


def _write_7bit_len(n: int, out: bytearray) -> None:
    while True:
        b = n & 0x7F
        n >>= 7
        if n:
            out.append(b | 0x80)
        else:
            out.append(b)
            return


def _write_value(v, out: bytearray) -> None:
    if isinstance(v, dict):
        out += struct.pack("<i", 2)
        out += struct.pack("<i", len(v))
        for key, val in v.items():
            key_bytes = key.encode("utf-8")
            _write_7bit_len(len(key_bytes), out)
            out += key_bytes
            _write_value(val, out)
    elif isinstance(v, list):
        out += struct.pack("<i", 1)
        out += struct.pack("<i", len(v))
        for item in v:
            _write_value(item, out)
    elif isinstance(v, str):
        out += struct.pack("<i", 3)
        str_bytes = v.encode("utf-8")
        _write_7bit_len(len(str_bytes), out)
        out += str_bytes
    elif isinstance(v, bool):
        raise TypeError("boolean leaf values are not part of the observed format")
    elif isinstance(v, float):
        out += struct.pack("<i", 5)
        out += struct.pack("<d", v)
    elif isinstance(v, int):
        out += struct.pack("<i", 4)
        out += struct.pack("<i", v)
    else:
        raise TypeError(f"cannot serialize value of type {type(v)!r}")


def parse(raw: bytes) -> dict:
    reader = _Reader(raw)
    tree = reader.value()
    if reader.pos != len(raw):
        raise MemoriaFormatError(
            f"parsed {reader.pos} of {len(raw)} bytes; {len(raw) - reader.pos} trailing bytes unconsumed"
        )
    if not isinstance(tree, dict):
        raise MemoriaFormatError("top-level value is not a dictionary")
    return tree


def serialize(tree: dict) -> bytes:
    out = bytearray()
    _write_value(tree, out)
    return bytes(out)


def looks_like_memoria(raw: bytes) -> bool:
    try:
        tree = parse(raw)
    except (MemoriaFormatError, UnicodeDecodeError, struct.error):
        return False
    common = tree.get("40000_Common")
    return isinstance(common, dict) and isinstance(common.get("players"), list)


# --------------------------------------------------------------------------- character view

# key -> path of dict keys to walk from a player record, or ("equip", index) for gear.
# Stat names (dex/str/mgc/wpr) are Memoria's own internal names; the dex<->speed and
# wpr<->spirit mapping is inferred from FF9's four-stat model (Strength/Magic/Spirit/
# Speed) and is not independently confirmed the way the legacy/rr2016 offsets are.
_CHAR_FIELD_PATHS: dict[str, tuple[str, ...]] = {
    "level": ("level",),
    "exp": ("exp",),
    "cur_hp": ("cur", "hp"),
    "max_hp": ("max", "hp"),
    "cur_mp": ("cur", "mp"),
    "max_mp": ("max", "mp"),
    "strength": ("elem", "str"),
    "speed": ("elem", "dex"),
    "magic": ("elem", "mgc"),
    "spirit": ("elem", "wpr"),
    "trance": ("trance",),
}
_EQUIP_SLOT_INDEX = {"weapon": 0, "head": 1, "arm": 2, "armor": 3, "accessory": 4}

STAT_MAX = {
    "level": 99, "exp": 9999999, "cur_hp": 9999, "max_hp": 9999,
    "cur_mp": 999, "max_mp": 999, "strength": 99, "speed": 99,
    "magic": 99, "spirit": 99, "trance": 100,
}


class MemoriaCharacter:
    fmt = "memoria"

    def __init__(self, record: dict, index: int) -> None:
        self.record = record
        self.index = index

    @property
    def name(self) -> str:
        return self.record.get("name", "")

    @name.setter
    def name(self, value: str) -> None:
        self.record["name"] = value

    def has(self, field_name: str) -> bool:
        return field_name in _CHAR_FIELD_PATHS or field_name in _EQUIP_SLOT_INDEX

    def _walk(self, path: tuple[str, ...]):
        node = self.record
        for key in path[:-1]:
            node = node[key]
        return node, path[-1]

    def get(self, field_name: str) -> int:
        if field_name in _EQUIP_SLOT_INDEX:
            equip = self.record.get("equip", [])
            idx = _EQUIP_SLOT_INDEX[field_name]
            return equip[idx] if idx < len(equip) else 0xFF
        node, key = self._walk(_CHAR_FIELD_PATHS[field_name])
        return node.get(key, 0)

    def set(self, field_name: str, value: int) -> None:
        if field_name in _EQUIP_SLOT_INDEX:
            equip = self.record.setdefault("equip", [0xFF] * 5)
            idx = _EQUIP_SLOT_INDEX[field_name]
            while len(equip) <= idx:
                equip.append(0xFF)
            equip[idx] = int(value) & 0xFF
            return
        node, key = self._walk(_CHAR_FIELD_PATHS[field_name])
        # Preserve int vs. float type of whatever was already there (the
        # writer picks the on-disk tag from the Python type).
        existing = node.get(key, 0)
        node[key] = float(value) if isinstance(existing, float) else int(value)

    def max_of(self, field_name: str) -> int:
        return STAT_MAX.get(field_name, 0xFFFFFFFF)

    @property
    def is_recruited(self) -> bool:
        return self.get("level") > 0

    def equipment(self) -> dict[str, int]:
        return {slot: self.get(slot) for slot in _EQUIP_SLOT_INDEX}

    def support_abilities(self) -> list[int]:
        return []  # Memoria's ability data (sa_extended) isn't decoded yet; see NOTICES.md.

    def set_support_ability(self, bit_index: int, enabled: bool) -> None:
        raise ValueError("support ability editing is not available for Memoria mod saves")

    def max_out(self) -> None:
        for field_name in ("level", "exp", "cur_hp", "max_hp", "cur_mp", "max_mp",
                            "strength", "speed", "magic", "spirit", "trance"):
            self.set(field_name, self.max_of(field_name))
        basis = self.record.get("basis")
        if isinstance(basis, dict):
            if "max_hp" in basis:
                basis["max_hp"] = self.get("max_hp")
            if "max_mp" in basis:
                basis["max_mp"] = self.get("max_mp")
            for key, field_name in (("dex", "speed"), ("str", "strength"), ("mgc", "magic"), ("wpr", "spirit")):
                if key in basis:
                    basis[key] = self.get(field_name)


# --------------------------------------------------------------------------- slot view

class MemoriaSlot:
    fmt = "memoria"

    def __init__(self, tree: dict) -> None:
        self.tree = tree

    def clone(self) -> MemoriaSlot:
        return MemoriaSlot(copy.deepcopy(self.tree))

    def _common(self) -> dict:
        return self.tree.setdefault("40000_Common", {})

    def _players(self) -> list:
        return self._common().setdefault("players", [])

    def characters(self) -> list[MemoriaCharacter]:
        return [MemoriaCharacter(p, i) for i, p in enumerate(self._players()) if isinstance(p, dict)]

    def character(self, index: int) -> MemoriaCharacter:
        players = self._players()
        if not (0 <= index < len(players)):
            raise IndexError(f"character index out of range 0-{len(players) - 1}: {index}")
        return MemoriaCharacter(players[index], index)

    # -- items --------------------------------------------------------------
    # Unlike the fixed 256-slot legacy/rr2016 table, this is a plain growable
    # list of {"id": int, "count": int} records, so there's no "inventory
    # full" failure mode here.

    def items(self) -> list[tuple[int, int, int]]:
        out = []
        for i, entry in enumerate(self._common().get("items", [])):
            if isinstance(entry, dict) and entry.get("count", 0) > 0:
                out.append((entry["id"], entry["count"], i))
        return out

    def find_item_slot(self, item_id: int) -> int | None:
        for i, entry in enumerate(self._common().get("items", [])):
            if isinstance(entry, dict) and entry.get("id") == item_id:
                return i
        return None

    def set_item(self, item_id: int, count: int) -> bool:
        if not (0 <= item_id <= 0xFF):
            raise ValueError(f"item id out of range: {item_id}")
        count = max(0, min(count, 99))
        items = self._common().setdefault("items", [])
        existing = self.find_item_slot(item_id)
        if existing is not None:
            if count == 0:
                del items[existing]
            else:
                items[existing]["count"] = max(items[existing].get("count", 0), count)
            return True
        if count > 0:
            items.append({"id": item_id, "count": count})
        return True

    def remove_item(self, item_id: int) -> None:
        self.set_item(item_id, 0)

    # -- gil / misc -----------------------------------------------------------

    @property
    def gil(self) -> int:
        return self._common().get("gil", 0)

    @gil.setter
    def gil(self, value: int) -> None:
        self._common()["gil"] = max(0, min(int(value), 9_999_999))

    @property
    def leader_name(self) -> str:
        for char in self.characters():
            if char.is_recruited and char.name:
                return char.name
        return ""

    @property
    def location(self) -> str | None:
        return None  # not identified in this format yet

    @property
    def playtime_seconds(self) -> float:
        return self.tree.get("95000_Setting", {}).get("00001_time", 0.0)

    def party_member_ids(self) -> list[int] | None:
        return None  # not identified in this format yet

    # -- cards ------------------------------------------------------------------

    def cards(self) -> list[dict]:
        out = []
        for rec in self.tree.get("30000_MiniGame", {}).get("MiniGameCard", []):
            if isinstance(rec, dict) and rec.get("type", 0xFF) < len(data.CARD_TYPE_NAMES):
                out.append({
                    "index": rec.get("id", 0),
                    "type_id": rec.get("type", 0xFF),
                    "arrows": rec.get("arrow", 0),
                    "attack": rec.get("atk", 0),
                    "attack_type": 0,  # not stored separately in this format
                    "p_def": rec.get("pdef", 0),
                    "m_def": rec.get("mdef", 0),
                })
        return out

    def card_record_stats(self) -> tuple[int, int, int]:
        mini = self.tree.get("30000_MiniGame", {})
        return (mini.get("sWin", 0), mini.get("sLose", 0), mini.get("sDraw", 0))

    def finalize(self) -> None:
        pass  # no checksum in this format
