#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""
Command-line SAVE editor and inspector for Final Fantasy IX.

Supports three on-disk formats:

  legacy   PS1-era save data: a single 8192-byte block (raw, or wrapped in a
           128-byte header for .mcs/.ps1 files), or a full 131072-byte
           PS1 memory-card image (.mcr/.mcd/.bin) holding up to 15 blocks.
           Used by emulators (DuckStation, PCSX, ePSXe, RetroArch, ...).

  rr2016   The AES-encrypted container used by the 2016 Steam/PC and mobile
           re-release (.dat on PC, .sav on iOS/Android). Holds up to 9 save
           "slots" x 15 files each, all inside one file.

  memoria  The unencrypted, tagged-tree format written by the Memoria mod.

The legacy and rr2016 layouts were reverse-engineered by the "Memoria" FF9
save editor project (Gjoerulv); see NOTICES.md for attribution and validation
details. The separate Memoria-mod format was derived from real files.

This file has no UI code. ffix_save_tui.py imports from it directly so the
CLI and TUI can never drift apart.
"""
from __future__ import annotations

import argparse
import hashlib
import os
import stat
import struct
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

from Crypto.Cipher import AES
from Crypto.Util.Padding import pad, unpad

import ffix_save_data as data
import ffix_save_memoria as memoria


# --------------------------------------------------------------------------- byte helpers

def read_uint(buf: bytes, offset: int, size: int) -> int:
    return int.from_bytes(buf[offset:offset + size], "little")


def write_uint(buf: bytearray, offset: int, size: int, value: int) -> None:
    value = max(0, min(int(value), (1 << (size * 8)) - 1))
    buf[offset:offset + size] = value.to_bytes(size, "little")


@dataclass(frozen=True)
class FieldSpec:
    offset: int
    size: int


def get_field(buf: bytes, base: int, spec: FieldSpec) -> int:
    return read_uint(buf, base + spec.offset, spec.size)


def set_field(buf: bytearray, base: int, spec: FieldSpec, value: int) -> None:
    write_uint(buf, base + spec.offset, spec.size, value)


def field_max(spec: FieldSpec) -> int:
    return (1 << (spec.size * 8)) - 1


# --------------------------------------------------------------------------- legacy: CRC16-CCITT checksum

class _Crc16Ccitt:
    def __init__(self, init: int, poly: int) -> None:
        self.init = init
        table = []
        for i in range(256):
            value = i
            for _ in range(8):
                if value & 1:
                    value = (value >> 1) ^ poly
                else:
                    value >>= 1
            table.append(value)
        self.table = table

    def compute(self, buf: bytes) -> int:
        crc = self.init
        for b in buf:
            crc = (crc >> 8) ^ self.table[(crc ^ b) & 0xFF]
        return crc & 0xFFFF


_LEGACY_CRC = _Crc16Ccitt(0xFFFF, 0x8408)

LEGACY_CHECKSUM_OFFSET = 0x13FE


def legacy_checksum(block: bytes) -> int:
    return _LEGACY_CRC.compute(block[:LEGACY_CHECKSUM_OFFSET])


def legacy_fix_checksum(block: bytearray) -> None:
    write_uint(block, LEGACY_CHECKSUM_OFFSET, 2, legacy_checksum(block))


# --------------------------------------------------------------------------- legacy: layout constants

LEGACY_BLOCK_HEADER_SIZE = 0x80
LEGACY_BLOCK_SIZE = 0x2000
LEGACY_CARD_SIZE = 0x20000
LEGACY_REGION_CODE_OFFSET = 0xA
LEGACY_MAX_BLOCKS = 16  # block 0 is the directory; save data lives in blocks 1-15

LEGACY_PREVIEW_GIL_OFFSET = FieldSpec(0x130, 3)
LEGACY_GIL_OFFSET = FieldSpec(0xEE8, 4)
LEGACY_PLAYTIME_OFFSET = FieldSpec(0x12C, 4)  # video frames (50 Hz PAL / 60 Hz NTSC)
LEGACY_LOCATION_OFFSET = 0x110
LEGACY_LOCATION_LEN = 28
LEGACY_LEADER_NAME_OFFSET = 0x106
LEGACY_LEADER_NAME_LEN = 8
LEGACY_LEADER_LEVEL_OFFSET = 0x105

LEGACY_CHAR_SECTION_START = 0x9D0
LEGACY_CHAR_BLOCK_SIZE = 144
LEGACY_CHAR_COUNT = 9
LEGACY_CHAR_NAME_OFFSET = 0x000
LEGACY_CHAR_NAME_LEN = 8
LEGACY_CHAR_AP_LIST_OFFSET = 0x058
LEGACY_CHAR_AP_LIST_LEN = 48
LEGACY_CHAR_SUPPORT_BITMAP_OFFSET = 0x088
LEGACY_CHAR_SUPPORT_BITMAP_LEN = 8

LEGACY_CHAR_FIELDS: dict[str, FieldSpec] = {
    "level": FieldSpec(0x00B, 1),
    "exp": FieldSpec(0x00C, 4),
    "cur_hp": FieldSpec(0x010, 2),
    "cur_mp": FieldSpec(0x012, 2),
    "cur_magic_stones": FieldSpec(0x017, 1),
    "max_hp": FieldSpec(0x018, 2),
    "max_mp": FieldSpec(0x01A, 2),
    "max_magic_stones": FieldSpec(0x01F, 1),
    "trance": FieldSpec(0x020, 1),
    "speed": FieldSpec(0x024, 1),
    "strength": FieldSpec(0x025, 1),
    "magic": FieldSpec(0x026, 1),
    "spirit": FieldSpec(0x027, 1),
    "defence": FieldSpec(0x028, 1),
    "evade": FieldSpec(0x029, 1),
    "magic_defence": FieldSpec(0x02A, 1),
    "magic_evade": FieldSpec(0x02B, 1),
    "max_hp_bonus": FieldSpec(0x02C, 2),
    "max_mp_bonus": FieldSpec(0x02E, 2),
    "speed_base": FieldSpec(0x030, 1),
    "strength_base": FieldSpec(0x031, 1),
    "magic_base": FieldSpec(0x032, 1),
    "spirit_base": FieldSpec(0x033, 1),
    "status": FieldSpec(0x038, 1),
    "weapon": FieldSpec(0x039, 1),
    "head": FieldSpec(0x03A, 1),
    "arm": FieldSpec(0x03B, 1),
    "armor": FieldSpec(0x03C, 1),
    "accessory": FieldSpec(0x03D, 1),
}

LEGACY_ITEM_SECTION_START = 0xF20
LEGACY_ITEM_SLOT_COUNT = 256

LEGACY_CARD_WINS_OFFSET = FieldSpec(0x1178, 2)
LEGACY_CARD_LOSSES_OFFSET = FieldSpec(0x117A, 2)
LEGACY_CARD_DRAWS_OFFSET = FieldSpec(0x117C, 2)
LEGACY_CARD_SECTION_START = 0x117E
LEGACY_CARD_RECORD_SIZE = 6
LEGACY_CARD_COUNT = 105
# (type, arrows, attack, attack_type, p_def, m_def) byte offsets within a card record
LEGACY_CARD_LAYOUT = (0, 1, 2, 3, 4, 5)

EQUIP_SLOT_NAMES = ("weapon", "head", "arm", "armor", "accessory")
MAX_GIL = 9_999_999

# Product/disc codes stored in PS1 memory-card directory frames. The final
# "-NN" portion is the save number and is checked separately.
LEGACY_FFIX_DISC_CODES = frozenset(
    [f"BASLUS-{disc}00000" for disc in ("01251", "01295", "01296", "01297")]
    + [f"BISLPS-{disc}00000" for disc in ("02000", "02001", "02002", "02003")]
    + [
        f"BESLES-{disc}{language}00000"
        for disc in ("0", "1", "2", "3")
        for language in ("2965", "2966", "2967", "2968", "2969")
    ]
)


# --------------------------------------------------------------------------- rr2016: layout constants

RR_CONTAINER_SIZE = 0x2CD140
RR_CHUNK_SIZE = 18432  # 0x4800, reserved space per (slot, save) pair
RR_CHUNK_BASE = 153920  # first chunk starts here, right after the fixed "system" region
RR_META_PLAINTEXT_SIZE = 288
RR_MAX_SLOTS = 9
RR_MAX_SAVES = 15

RR_GIL_OFFSET = FieldSpec(0x1473, 4)
RR_ITEM_SECTION_START = 0x1477
RR_ITEM_SLOT_COUNT = 256

RR_CARD_SECTION_START = 0x101B
RR_CARD_RECORD_SIZE = 11
RR_CARD_COUNT = 100
# (type, arrows, attack, attack_type, p_def, m_def) byte offsets within an 11-byte record
RR_CARD_LAYOUT = (3, 0, 1, 7, 5, 4)
RR_CARD_DRAWS_OFFSET = FieldSpec(0x1467, 2)
RR_CARD_LOSSES_OFFSET = FieldSpec(0x1469, 2)
RR_CARD_WINS_OFFSET = FieldSpec(0x146B, 2)

RR_CHAR_SECTION_START = 0x1677
RR_CHAR_BLOCK_SIZE = 0xF4
RR_CHAR_COUNT = 9
RR_CHAR_NAME_OFFSET = 0x39
RR_CHAR_NAME_LEN = 8

RR_CHAR_FIELDS: dict[str, FieldSpec] = {
    "speed_base": FieldSpec(0x00, 1),
    "max_hp_base": FieldSpec(0x01, 2),
    "max_mp_base": FieldSpec(0x03, 2),
    "magic_base": FieldSpec(0x05, 1),
    "strength_base": FieldSpec(0x06, 1),
    "spirit_base": FieldSpec(0x07, 1),
    "cur_hp": FieldSpec(0x15, 2),
    "cur_mp": FieldSpec(0x17, 2),
    "magic_defence": FieldSpec(0x19, 1),
    "magic_evade": FieldSpec(0x1A, 1),
    "defence": FieldSpec(0x1B, 1),
    "evade": FieldSpec(0x1C, 1),
    "speed_bonus": FieldSpec(0x1D, 1),
    "magic_bonus": FieldSpec(0x1E, 1),
    "strength_bonus": FieldSpec(0x1F, 1),
    "spirit_bonus": FieldSpec(0x20, 1),
    # Confirmed by the reference editor's live control mappings and real saves.
    "weapon": FieldSpec(0x21, 1),
    "head": FieldSpec(0x22, 1),
    "arm": FieldSpec(0x23, 1),
    "armor": FieldSpec(0x24, 1),
    "accessory": FieldSpec(0x25, 1),
    "exp": FieldSpec(0x26, 4),
    "magic_stones": FieldSpec(0x34, 1),
    "max_hp": FieldSpec(0x35, 2),
    "max_mp": FieldSpec(0x37, 2),
    "level": FieldSpec(0x30, 1),
}

RR_PARTY_OFFSET = 0x1F4B
RR_PARTY_SLOTS = 4
RR_PLAYTIME_OFFSET = 0x3832  # 8-byte float64, seconds
RR_SLOT_PLAINTEXT_SIZE = 0x4632
RR_OCCUPIED_HEADER = b"SAVE"
RR_EMPTY_HEADER = b"NONE"


def _rr_aes_key_iv() -> tuple[bytes, bytes]:
    # The reference program appends two UUIDs to a .NET SecureString, then
    # mistakenly calls SecureString.ToString(). That method returns this type
    # name rather than the protected contents. Vanilla saves therefore use
    # this literal string as the PBKDF2 password.
    password = b"System.Security.SecureString"
    salt = bytes([3, 3, 1, 4, 7, 0, 9, 7])
    derived = hashlib.pbkdf2_hmac("sha1", password, salt, 1000, dklen=48)
    return derived[:32], derived[32:48]


def rr_cipher_size(plaintext_size: int) -> int:
    return plaintext_size + 16 - (plaintext_size % 16)


def rr_decrypt(ciphertext: bytes) -> bytes:
    key, iv = _rr_aes_key_iv()
    cipher = AES.new(key, AES.MODE_CBC, iv)
    return unpad(cipher.decrypt(ciphertext), 16)


def rr_encrypt(plaintext: bytes) -> bytes:
    key, iv = _rr_aes_key_iv()
    cipher = AES.new(key, AES.MODE_CBC, iv)
    return cipher.encrypt(pad(plaintext, 16))


@dataclass
class RRMetadata:
    save_version: float
    data_size: int
    latest_slot: int
    latest_save: int
    latest_timestamp: float
    is_game_finished: bool
    selected_language: int

    @property
    def slot_plain_size(self) -> int:
        return self.data_size + 4


def rr_parse_metadata(container: bytes) -> RRMetadata:
    cipher_len = rr_cipher_size(RR_META_PLAINTEXT_SIZE)
    try:
        plain = rr_decrypt(container[0:cipher_len])
    except ValueError as exc:
        raise ValueError(
            f"Could not decrypt the save header ({exc}). The file is the right "
            "size for an rr2016 save, but the fixed AES key this tool uses "
            "doesn't produce valid data from its first bytes. This can happen "
            "if a mod changes the save encryption, if this is a different "
            "game/build than the vanilla 2016 Steam/PC/mobile re-release, or "
            "if the file is corrupted."
        ) from exc
    if plain[0:4] != b"SAVE":
        raise ValueError("RR2016 metadata header mismatch (bad key or corrupt file)")
    (save_version, data_size, latest_slot, latest_save, latest_timestamp,
     is_finish, selected_lang, _is_auto_login, _achievements, _rotation) = struct.unpack_from(
        "<fiiidiibBB", plain, 4
    )
    metadata = RRMetadata(
        save_version=save_version,
        data_size=data_size,
        latest_slot=latest_slot,
        latest_save=latest_save,
        latest_timestamp=latest_timestamp,
        is_game_finished=bool(is_finish),
        selected_language=selected_lang,
    )
    if metadata.slot_plain_size != RR_SLOT_PLAINTEXT_SIZE:
        raise ValueError(
            "Unsupported rr2016 slot size in metadata: "
            f"{metadata.slot_plain_size:,} bytes (expected {RR_SLOT_PLAINTEXT_SIZE:,})"
        )
    return metadata


def rr_chunk_offset(slot_id: int, save_id: int) -> int:
    if not (0 <= slot_id < RR_MAX_SLOTS):
        raise ValueError(f"rr2016 slot must be 1-{RR_MAX_SLOTS}, got {slot_id + 1}")
    if not (0 <= save_id < RR_MAX_SAVES):
        raise ValueError(f"rr2016 file must be 1-{RR_MAX_SAVES}, got {save_id + 1}")
    return RR_CHUNK_BASE + RR_CHUNK_SIZE * (1 + slot_id * RR_MAX_SAVES + save_id)


# --------------------------------------------------------------------------- Character (per-slot view)

class Character:
    """One of the 9 fixed character rows (Zidane .. Beatrix) inside a save slot."""

    def __init__(self, buf: bytearray, base: int, fmt: str, index: int) -> None:
        self.buf = buf
        self.base = base
        self.fmt = fmt
        self.index = index
        self._fields = LEGACY_CHAR_FIELDS if fmt == "legacy" else RR_CHAR_FIELDS

    @property
    def name(self) -> str:
        if self.fmt == "legacy":
            raw = bytes(self.buf[self.base:self.base + LEGACY_CHAR_NAME_LEN])
            return data.decode_legacy_text(raw)
        raw = bytes(self.buf[self.base + RR_CHAR_NAME_OFFSET:self.base + RR_CHAR_NAME_OFFSET + RR_CHAR_NAME_LEN])
        return raw.split(b"\x00", 1)[0].decode("latin-1", errors="replace")

    @name.setter
    def name(self, value: str) -> None:
        if self.fmt == "legacy":
            self.buf[self.base:self.base + LEGACY_CHAR_NAME_LEN] = data.encode_legacy_text(value, LEGACY_CHAR_NAME_LEN)
            return
        encoded = value.encode("latin-1", errors="replace")[:RR_CHAR_NAME_LEN]
        encoded = encoded + b"\x00" * (RR_CHAR_NAME_LEN - len(encoded))
        off = self.base + RR_CHAR_NAME_OFFSET
        self.buf[off:off + RR_CHAR_NAME_LEN] = encoded

    def has(self, field_name: str) -> bool:
        return field_name in self._fields

    def get(self, field_name: str) -> int:
        return get_field(self.buf, self.base, self._fields[field_name])

    def set(self, field_name: str, value: int) -> None:
        set_field(self.buf, self.base, self._fields[field_name], value)

    def max_of(self, field_name: str) -> int:
        return field_max(self._fields[field_name])

    @property
    def is_recruited(self) -> bool:
        return self.get("level") > 0

    def equipment(self) -> dict[str, int]:
        return {slot: self.get(slot) for slot in EQUIP_SLOT_NAMES if self.has(slot)}

    def support_abilities(self) -> list[int]:
        if self.fmt != "legacy":
            return []
        off = self.base + LEGACY_CHAR_SUPPORT_BITMAP_OFFSET
        bitmap = int.from_bytes(self.buf[off:off + LEGACY_CHAR_SUPPORT_BITMAP_LEN], "little")
        return [i for i in range(len(data.SUPPORT_ABILITY_NAMES)) if bitmap & (1 << i)]

    def set_support_ability(self, bit_index: int, enabled: bool) -> None:
        if self.fmt != "legacy":
            raise ValueError("support ability bitmap is only known for legacy saves")
        off = self.base + LEGACY_CHAR_SUPPORT_BITMAP_OFFSET
        bitmap = int.from_bytes(self.buf[off:off + LEGACY_CHAR_SUPPORT_BITMAP_LEN], "little")
        if enabled:
            bitmap |= (1 << bit_index)
        else:
            bitmap &= ~(1 << bit_index)
        self.buf[off:off + LEGACY_CHAR_SUPPORT_BITMAP_LEN] = bitmap.to_bytes(LEGACY_CHAR_SUPPORT_BITMAP_LEN, "little")

    def max_out(self) -> None:
        self.set("level", 99)
        self.set("exp", 9999999)
        for field_name in ("speed", "strength", "magic", "spirit",
                            "speed_base", "strength_base", "magic_base", "spirit_base"):
            if self.has(field_name):
                self.set(field_name, 99)
        if self.has("max_hp"):
            self.set("max_hp", 9999)
        if self.has("max_hp_base"):
            self.set("max_hp_base", 9999)
        if self.has("max_hp_bonus"):
            self.set("max_hp_bonus", 0)
        if self.has("max_mp"):
            self.set("max_mp", 999)
        if self.has("max_mp_base"):
            self.set("max_mp_base", 999)
        if self.has("max_mp_bonus"):
            self.set("max_mp_bonus", 0)
        self.set("cur_hp", 9999)
        self.set("cur_mp", 999)
        if self.has("cur_magic_stones") and self.has("max_magic_stones"):
            self.set("max_magic_stones", self.max_of("max_magic_stones"))
            self.set("cur_magic_stones", self.get("max_magic_stones"))
        if self.has("trance"):
            self.set("trance", self.max_of("trance"))


# --------------------------------------------------------------------------- Slot (one save file's worth of data)

class Slot:
    """The full, editable byte range for a single FFIX save (one memory-card
    block for legacy saves, or one decrypted (slot, save) pair for rr2016)."""

    def __init__(self, buf: bytearray, fmt: str, *, legacy_framerate: int = 60) -> None:
        self.buf = buf
        self.fmt = fmt
        self.legacy_framerate = legacy_framerate
        if fmt == "legacy":
            self._char_start = LEGACY_CHAR_SECTION_START
            self._char_size = LEGACY_CHAR_BLOCK_SIZE
            self._char_count = LEGACY_CHAR_COUNT
            self._item_start = LEGACY_ITEM_SECTION_START
            self._item_count = LEGACY_ITEM_SLOT_COUNT
            self._card_start = LEGACY_CARD_SECTION_START
            self._card_record_size = LEGACY_CARD_RECORD_SIZE
            self._card_count = LEGACY_CARD_COUNT
            self._card_layout = LEGACY_CARD_LAYOUT
        else:
            self._char_start = RR_CHAR_SECTION_START
            self._char_size = RR_CHAR_BLOCK_SIZE
            self._char_count = RR_CHAR_COUNT
            self._item_start = RR_ITEM_SECTION_START
            self._item_count = RR_ITEM_SLOT_COUNT
            self._card_start = RR_CARD_SECTION_START
            self._card_record_size = RR_CARD_RECORD_SIZE
            self._card_count = RR_CARD_COUNT
            self._card_layout = RR_CARD_LAYOUT

    def clone(self) -> Slot:
        return Slot(bytearray(self.buf), self.fmt, legacy_framerate=self.legacy_framerate)

    # -- characters ---------------------------------------------------------

    def character(self, index: int) -> Character:
        if not (0 <= index < self._char_count):
            raise IndexError(f"character index out of range 0-{self._char_count - 1}: {index}")
        base = self._char_start + index * self._char_size
        return Character(self.buf, base, self.fmt, index)

    def characters(self) -> list[Character]:
        return [self.character(i) for i in range(self._char_count)]

    # -- items ----------------------------------------------------------------
    # Fixed 256-slot table, 2 bytes/slot: (item_id byte, count byte). item_id
    # 0xFF ("Nothing") marks an empty slot.

    def items(self) -> list[tuple[int, int, int]]:
        out = []
        for slot_index in range(self._item_count):
            off = self._item_start + slot_index * 2
            item_id, count = self.buf[off], self.buf[off + 1]
            if item_id != data.EMPTY_ITEM_ID and count > 0:
                out.append((item_id, count, slot_index))
        return out

    def find_item_slot(self, item_id: int) -> int | None:
        for slot_index in range(self._item_count):
            off = self._item_start + slot_index * 2
            if self.buf[off] == item_id and self.buf[off + 1] > 0:
                return slot_index
        return None

    def set_item(self, item_id: int, count: int) -> bool:
        """Upserts item_id to at least `count`. Returns False if inventory is full."""
        if not (0 <= item_id <= 0xFF):
            raise ValueError(f"item id out of range: {item_id}")
        count = max(0, min(count, 99))
        existing = self.find_item_slot(item_id)
        if existing is not None:
            off = self._item_start + existing * 2
            if count == 0:
                self.buf[off] = data.EMPTY_ITEM_ID
                self.buf[off + 1] = 0
            else:
                self.buf[off + 1] = max(self.buf[off + 1], count)
            return True
        if count == 0:
            return True
        for slot_index in range(self._item_count):
            off = self._item_start + slot_index * 2
            if self.buf[off] == data.EMPTY_ITEM_ID or self.buf[off + 1] == 0:
                self.buf[off] = item_id
                self.buf[off + 1] = count
                return True
        return False

    def remove_item(self, item_id: int) -> None:
        self.set_item(item_id, 0)

    # -- gil / party / misc ---------------------------------------------------

    @property
    def gil(self) -> int:
        spec = LEGACY_GIL_OFFSET if self.fmt == "legacy" else RR_GIL_OFFSET
        return get_field(self.buf, 0, spec)

    @gil.setter
    def gil(self, value: int) -> None:
        value = max(0, min(int(value), MAX_GIL))
        if self.fmt == "legacy":
            # FFIX stores a gameplay value and a second copy used by the PS1
            # memory-card preview. Keep both in sync.
            set_field(self.buf, 0, LEGACY_GIL_OFFSET, value)
            set_field(self.buf, 0, LEGACY_PREVIEW_GIL_OFFSET, value)
            return
        set_field(self.buf, 0, RR_GIL_OFFSET, value)

    @property
    def leader_name(self) -> str:
        if self.fmt == "legacy":
            raw = bytes(self.buf[LEGACY_LEADER_NAME_OFFSET:LEGACY_LEADER_NAME_OFFSET + LEGACY_LEADER_NAME_LEN])
            return data.decode_legacy_text(raw)
        for char in self.characters():
            if char.is_recruited:
                return char.name
        return ""

    @property
    def location(self) -> str | None:
        if self.fmt != "legacy":
            return None
        raw = bytes(self.buf[LEGACY_LOCATION_OFFSET:LEGACY_LOCATION_OFFSET + LEGACY_LOCATION_LEN])
        return data.decode_legacy_text(raw)

    @property
    def playtime_seconds(self) -> float:
        if self.fmt == "legacy":
            frames = get_field(self.buf, 0, LEGACY_PLAYTIME_OFFSET)
            return frames / self.legacy_framerate
        return struct.unpack_from("<d", self.buf, RR_PLAYTIME_OFFSET)[0]

    def party_member_ids(self) -> list[int] | None:
        if self.fmt != "rr2016":
            return None
        return [b for b in self.buf[RR_PARTY_OFFSET:RR_PARTY_OFFSET + RR_PARTY_SLOTS] if b != 0xFF]

    # -- cards ------------------------------------------------------------------

    def cards(self) -> list[dict]:
        out = []
        for i in range(self._card_count):
            rec = self._card_record(i)
            if rec["type_id"] < len(data.CARD_TYPE_NAMES):
                out.append(rec)
        return out

    def _card_record(self, index: int) -> dict:
        base = self._card_start + index * self._card_record_size
        type_off, arrows_off, attack_off, atktype_off, pdef_off, mdef_off = self._card_layout
        return {
            "index": index,
            "type_id": self.buf[base + type_off],
            "arrows": self.buf[base + arrows_off],
            "attack": self.buf[base + attack_off],
            "attack_type": self.buf[base + atktype_off],
            "p_def": self.buf[base + pdef_off],
            "m_def": self.buf[base + mdef_off],
        }

    def set_card(self, index: int, *, type_id: int, arrows: int = 0, attack: int = 0,
                 attack_type: int = 0, p_def: int = 0, m_def: int = 0) -> None:
        if not (0 <= index < self._card_count):
            raise IndexError(f"card index out of range 0-{self._card_count - 1}: {index}")
        base = self._card_start + index * self._card_record_size
        type_off, arrows_off, attack_off, atktype_off, pdef_off, mdef_off = self._card_layout
        self.buf[base + type_off] = type_id & 0xFF
        self.buf[base + arrows_off] = arrows & 0xFF
        self.buf[base + attack_off] = max(0, min(attack, 0xFF))
        self.buf[base + atktype_off] = attack_type & 0x03
        self.buf[base + pdef_off] = max(0, min(p_def, 0xFF))
        self.buf[base + mdef_off] = max(0, min(m_def, 0xFF))

    def card_record_stats(self) -> tuple[int, int, int]:
        wins_spec = LEGACY_CARD_WINS_OFFSET if self.fmt == "legacy" else RR_CARD_WINS_OFFSET
        losses_spec = LEGACY_CARD_LOSSES_OFFSET if self.fmt == "legacy" else RR_CARD_LOSSES_OFFSET
        draws_spec = LEGACY_CARD_DRAWS_OFFSET if self.fmt == "legacy" else RR_CARD_DRAWS_OFFSET
        return (
            get_field(self.buf, 0, wins_spec),
            get_field(self.buf, 0, losses_spec),
            get_field(self.buf, 0, draws_spec),
        )

    # -- finalize ---------------------------------------------------------------

    def finalize(self) -> None:
        """Call after editing and before writing back out. Recomputes the
        legacy CRC16 checksum; rr2016 saves have no known per-slot checksum."""
        if self.fmt == "legacy":
            legacy_fix_checksum(self.buf)


# --------------------------------------------------------------------------- Document (an open file)

@dataclass
class SlotRef:
    fmt: str
    label: str
    occupied: bool
    summary: str
    block_index: int | None = None
    slot_id: int | None = None
    save_id: int | None = None
    legacy_framerate: int | None = None


class Document:
    def __init__(self, fmt: str, raw: bytearray, *, header: bytes = b"", meta: RRMetadata | None = None,
                 memoria_slot: memoria.MemoriaSlot | None = None) -> None:
        self.fmt = fmt
        self.raw = raw
        self.header = header  # legacy SIMPLE-type 128-byte prefix, preserved verbatim
        self.meta = meta
        self.memoria_slot = memoria_slot

    # -- legacy -------------------------------------------------------------

    def _legacy_block_slice(self, block_index: int) -> slice:
        start = LEGACY_BLOCK_SIZE * block_index
        return slice(start, start + LEGACY_BLOCK_SIZE)

    def _legacy_block_looks_empty(self, block_index: int) -> bool:
        header_off = LEGACY_BLOCK_HEADER_SIZE * block_index
        header = self.raw[header_off:header_off + LEGACY_BLOCK_HEADER_SIZE]
        if len(header) < LEGACY_BLOCK_HEADER_SIZE:
            return True
        region = header[LEGACY_REGION_CODE_OFFSET:LEGACY_REGION_CODE_OFFSET + 4]
        if region == b"\x00\x00\x00\x00" and header[0] == 0xA0:
            return True  # PS1-formatted directory frame explicitly marked unused
        block = self.raw[self._legacy_block_slice(block_index)]
        return not any(block)  # never-written / zero-filled block (common in raw dumps)

    def _legacy_block_is_ffix(self, block_index: int) -> bool:
        header_off = LEGACY_BLOCK_HEADER_SIZE * block_index
        header = bytes(self.raw[header_off:header_off + LEGACY_BLOCK_HEADER_SIZE])
        if len(header) != LEGACY_BLOCK_HEADER_SIZE or header[0] != 0x51:
            return False
        product = header[LEGACY_REGION_CODE_OFFSET:LEGACY_REGION_CODE_OFFSET + 20]
        return (
            product[:17].decode("ascii", errors="ignore") in LEGACY_FFIX_DISC_CODES
            and product[17:18] == b"-"
            and product[18:20].isdigit()
        )

    def _legacy_block_framerate(self, block_index: int | None) -> int:
        if block_index is None:
            header = self.header
        else:
            start = LEGACY_BLOCK_HEADER_SIZE * block_index
            header = bytes(self.raw[start:start + LEGACY_BLOCK_HEADER_SIZE])
        product = header[LEGACY_REGION_CODE_OFFSET:LEGACY_REGION_CODE_OFFSET + 7]
        return 50 if product == b"BESLES-" else 60

    def list_slots(self) -> list[SlotRef]:
        """Universal slot listing, valid for every format this Document might
        hold. Front-ends should always call this rather than the
        format-specific rr_list_slots()/rr_probe_slot() helpers directly,
        so a fix here covers every caller instead of needing to be
        replicated at each call site."""
        if self.fmt == "memoria":
            return [self._memoria_slot_ref()]
        if self.fmt == "rr2016":
            return self.rr_list_slots()
        if self.fmt != "legacy":
            raise ValueError(f"list_slots() does not support format: {self.fmt!r}")
        if len(self.raw) == LEGACY_CARD_SIZE:
            refs = []
            for block_index in range(1, LEGACY_MAX_BLOCKS):
                if self._legacy_block_looks_empty(block_index):
                    continue
                if not self._legacy_block_is_ffix(block_index):
                    continue
                block = bytes(self.raw[self._legacy_block_slice(block_index)])
                refs.append(
                    self._legacy_slot_ref(
                        block_index,
                        block,
                        legacy_framerate=self._legacy_block_framerate(block_index),
                    )
                )
            return refs
        block = bytes(self.raw[:LEGACY_BLOCK_SIZE])
        return [
            self._legacy_slot_ref(
                None,
                block,
                legacy_framerate=self._legacy_block_framerate(None),
            )
        ]

    def _legacy_slot_ref(
        self,
        block_index: int | None,
        block: bytes,
        *,
        legacy_framerate: int,
    ) -> SlotRef:
        leader = data.decode_legacy_text(block[LEGACY_LEADER_NAME_OFFSET:LEGACY_LEADER_NAME_OFFSET + LEGACY_LEADER_NAME_LEN])
        level = block[LEGACY_LEADER_LEVEL_OFFSET]
        location = data.decode_legacy_text(block[LEGACY_LOCATION_OFFSET:LEGACY_LOCATION_OFFSET + LEGACY_LOCATION_LEN])
        stored = read_uint(block, LEGACY_CHECKSUM_OFFSET, 2)
        ok = stored == legacy_checksum(block)
        label = f"Block {block_index}" if block_index is not None else "Save data"
        summary = f"{leader or '?'} Lv{level}  {location}".strip()
        if not ok:
            summary += "  [checksum mismatch]"
        return SlotRef(
            fmt="legacy",
            label=label,
            occupied=True,
            summary=summary,
            block_index=block_index,
            legacy_framerate=legacy_framerate,
        )

    def _memoria_slot_ref(self) -> SlotRef:
        assert self.memoria_slot is not None
        leader = self.memoria_slot.leader_name
        summary = f"{leader or '(no party)'}  gil={self.memoria_slot.gil:,}"
        return SlotRef(fmt="memoria", label="Save data", occupied=True, summary=summary)

    def load_slot(self, ref: SlotRef) -> Slot | memoria.MemoriaSlot:
        if ref.fmt == "legacy":
            if ref.block_index is None:
                block = bytearray(self.raw)
            else:
                block = bytearray(self.raw[self._legacy_block_slice(ref.block_index)])
            return Slot(block, "legacy", legacy_framerate=ref.legacy_framerate or 60)
        if ref.fmt == "memoria":
            assert self.memoria_slot is not None
            return self.memoria_slot.clone()
        return self._rr_load(ref.slot_id, ref.save_id)

    def commit_slot(self, ref: SlotRef, slot: Slot | memoria.MemoriaSlot) -> None:
        slot.finalize()
        if ref.fmt == "legacy":
            if ref.block_index is None:
                self.raw[:] = slot.buf
            else:
                sl = self._legacy_block_slice(ref.block_index)
                self.raw[sl] = slot.buf
            return
        if ref.fmt == "memoria":
            if not isinstance(slot, memoria.MemoriaSlot):
                raise TypeError("expected a MemoriaSlot")
            self.memoria_slot = slot.clone()
            return
        self._rr_commit(ref.slot_id, ref.save_id, slot)

    # -- rr2016 ---------------------------------------------------------------

    def _rr_load(self, slot_id: int, save_id: int) -> Slot:
        assert self.meta is not None
        start = rr_chunk_offset(slot_id, save_id)
        cipher_len = rr_cipher_size(self.meta.slot_plain_size)
        ciphertext = bytes(self.raw[start:start + cipher_len])
        if len(ciphertext) != cipher_len:
            raise ValueError("rr2016 slot extends beyond the end of the container")
        plaintext = rr_decrypt(ciphertext)
        if len(plaintext) != self.meta.slot_plain_size or plaintext[:4] != RR_OCCUPIED_HEADER:
            raise ValueError(f"Slot {slot_id + 1} / File {save_id + 1} is empty or invalid")
        return Slot(bytearray(plaintext), "rr2016")

    def _rr_commit(self, slot_id: int, save_id: int, slot: Slot) -> None:
        assert self.meta is not None
        if len(slot.buf) != self.meta.slot_plain_size or slot.buf[:4] != RR_OCCUPIED_HEADER:
            raise ValueError("refusing to write an invalid rr2016 slot")
        ciphertext = rr_encrypt(bytes(slot.buf))
        start = rr_chunk_offset(slot_id, save_id)
        self.raw[start:start + len(ciphertext)] = ciphertext

    def rr_probe_slot(self, slot_id: int, save_id: int) -> SlotRef | None:
        assert self.meta is not None
        start = rr_chunk_offset(slot_id, save_id)
        cipher_len = rr_cipher_size(self.meta.slot_plain_size)
        ciphertext = bytes(self.raw[start:start + cipher_len])
        if not any(ciphertext):
            return None
        try:
            plaintext = rr_decrypt(ciphertext)
        except (ValueError, KeyError):
            return None
        if len(plaintext) != self.meta.slot_plain_size:
            return None
        if plaintext[:4] == RR_EMPTY_HEADER:
            return None
        if plaintext[:4] != RR_OCCUPIED_HEADER:
            return None
        slot = Slot(bytearray(plaintext), "rr2016")
        leader = slot.leader_name
        summary = f"{leader or '(no party)'}".strip()
        label = f"Slot {slot_id + 1} / File {save_id + 1}"
        return SlotRef(fmt="rr2016", label=label, occupied=True, summary=summary,
                        slot_id=slot_id, save_id=save_id)

    def rr_list_slots(self) -> list[SlotRef]:
        if self.fmt != "rr2016":
            raise ValueError("rr_list_slots() only applies to rr2016 saves")
        found = []
        for slot_id in range(RR_MAX_SLOTS):
            for save_id in range(RR_MAX_SAVES):
                ref = self.rr_probe_slot(slot_id, save_id)
                if ref is not None:
                    found.append(ref)
        return found

    # -- serialize --------------------------------------------------------------

    def to_bytes(self) -> bytes:
        if self.fmt == "memoria":
            assert self.memoria_slot is not None
            return memoria.serialize(self.memoria_slot.tree)
        if self.header:
            return bytes(self.header) + bytes(self.raw)
        return bytes(self.raw)


# --------------------------------------------------------------------------- file detection / open / save

LEGACY_MCR_EXTS = {".mcr", ".mcd", ".bin", ".mc", ".mci", ".ps", ".psm", ".dff"}
LEGACY_SIMPLE_EXTS = {".ps1", ".mcs"}
RR_EXTS = {".dat", ".sav"}


def detect_format(path: Path, raw: bytes) -> str:
    # Exact size matches are unambiguous and take priority over extension,
    # since a wrong extension-based guess here would send the bytes into
    # AES decryption or block-slicing at offsets that don't apply to them,
    # producing a confusing low-level error (e.g. "Padding is incorrect")
    # instead of a clear "this isn't the format you think it is" message.
    ext = path.suffix.lower()
    size = len(raw)
    if size == RR_CONTAINER_SIZE:
        return "rr2016"
    if size == LEGACY_CARD_SIZE:
        return "legacy_mcr"
    if size == LEGACY_BLOCK_SIZE:
        return "legacy_raw"
    if size == LEGACY_BLOCK_HEADER_SIZE + LEGACY_BLOCK_SIZE:
        return "legacy_simple"
    # Not a known fixed size. Before giving up, check whether this is a
    # Memoria-mod save (SavedData_ww_Memoria_*.dat): an entirely different,
    # unencrypted, self-describing format the mod writes alongside the
    # vanilla encrypted container, at no fixed size. It validates itself
    # (the whole file must parse as one well-formed tagged value with zero
    # bytes left over), so a false positive here is effectively impossible.
    if memoria.looks_like_memoria(raw):
        return "memoria"
    if ext in RR_EXTS:
        raise ValueError(
            f"{path.name} has extension {ext!r} (rr2016 Steam/PC/mobile save), "
            f"but is {size:,} bytes instead of the expected {RR_CONTAINER_SIZE:,}, "
            "and it isn't a Memoria-mod save either. This usually means it "
            "isn't actually the encrypted save container (e.g. it's a "
            "settings/system file, a save from a different game version/mod, "
            "or it's been truncated/corrupted) rather than a bug you can work "
            "around from here - please double check which file the game "
            "itself loads as your save."
        )
    if ext in LEGACY_MCR_EXTS | LEGACY_SIMPLE_EXTS:
        raise ValueError(
            f"{path.name} has a legacy memory-card extension but is {size:,} bytes. "
            "Expected exactly 8,192, 8,320, or 131,072 bytes."
        )
    raise ValueError(
        f"Unrecognized save file: {size:,} bytes, extension {ext!r}. "
        "Expected a PS1 memory-card save (8192 / 8320 / 131072 bytes) or an "
        f"rr2016 Steam/PC/mobile save ({RR_CONTAINER_SIZE:,} bytes)."
    )


def open_document(path: Path) -> Document:
    raw = path.read_bytes()
    kind = detect_format(path, raw)
    if kind == "rr2016":
        buf = bytearray(raw)
        meta = rr_parse_metadata(buf)
        return Document("rr2016", buf, meta=meta)
    if kind == "memoria":
        tree = memoria.parse(raw)
        return Document("memoria", bytearray(), memoria_slot=memoria.MemoriaSlot(tree))
    if kind == "legacy_simple":
        header, body = raw[:LEGACY_BLOCK_HEADER_SIZE], raw[LEGACY_BLOCK_HEADER_SIZE:]
        if body[:2] != b"SC":
            raise ValueError("Legacy save-data block is missing the PS1 'SC' header")
        return Document("legacy", bytearray(body), header=header)
    if kind == "legacy_raw" and raw[:2] != b"SC":
        raise ValueError("Legacy save-data block is missing the PS1 'SC' header")
    if kind == "legacy_mcr" and raw[:2] != b"MC":
        raise ValueError("Memory-card image is missing the PS1 'MC' header")
    return Document("legacy", bytearray(raw))


def format_label(fmt: str) -> str:
    return {
        "legacy": "PS1 memory-card save",
        "rr2016": "Steam/PC/mobile (2016) save",
        "memoria": "Memoria mod save (unencrypted)",
    }.get(fmt, fmt)


# --------------------------------------------------------------------------- safe file output

def _same_path(first: Path, second: Path) -> bool:
    try:
        return first.samefile(second)
    except (FileNotFoundError, OSError):
        return first.resolve() == second.resolve()


def atomic_write_bytes(path: Path, contents: bytes) -> None:
    """Write a complete file beside its destination, then atomically replace it."""
    path = Path(path)
    parent = path.parent
    fd, temp_name = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=parent)
    temp_path = Path(temp_name)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(contents)
            stream.flush()
            os.fsync(stream.fileno())
        if path.exists():
            os.chmod(temp_path, stat.S_IMODE(path.stat().st_mode))
        os.replace(temp_path, path)
    except BaseException:
        try:
            temp_path.unlink()
        except FileNotFoundError:
            pass
        raise


def write_new_document(doc: Document, out_path: Path, *, input_path: Path | None = None) -> None:
    if input_path is not None and _same_path(out_path, input_path):
        raise ValueError("output path is the input file; use in-place writing so a backup is created")
    atomic_write_bytes(out_path, doc.to_bytes())


def _available_backup_path(path: Path) -> Path:
    first = path.with_suffix(path.suffix + ".bak")
    if not first.exists():
        return first
    index = 1
    while True:
        candidate = path.with_suffix(path.suffix + f".bak.{index}")
        if not candidate.exists():
            return candidate
        index += 1


def write_document_in_place(doc: Document, input_path: Path) -> Path:
    original = input_path.read_bytes()
    backup = _available_backup_path(input_path)
    atomic_write_bytes(backup, original)
    atomic_write_bytes(input_path, doc.to_bytes())
    return backup


# --------------------------------------------------------------------------- high level operations

def give_item(slot: Slot, token: str, quantity: int) -> tuple[int, bool]:
    item_id = data.resolve_item_id(token)
    ok = slot.set_item(item_id, quantity)
    return item_id, ok


def give_all_items(slot: Slot, quantity: int) -> int:
    given = 0
    for item_id in range(len(data.ITEM_NAMES)):
        if item_id == data.EMPTY_ITEM_ID:
            continue
        if slot.set_item(item_id, quantity):
            given += 1
    return given


def resolve_character(slot: Slot, token: str) -> Character:
    token = token.strip()
    try:
        idx = int(token)
        return slot.character(idx)
    except ValueError:
        pass
    key = token.lower()
    for char in slot.characters():
        if char.name.lower() == key:
            return char
    matches = [c for c in slot.characters() if key in c.name.lower()]
    if len(matches) == 1:
        return matches[0]
    raise ValueError(f"unknown character: {token!r}")


# --------------------------------------------------------------------------- CLI

def _fmt_gil(n: int) -> str:
    return f"{n:,}"


def cmd_inspect(doc: Document, ref: SlotRef | None) -> None:
    print(f"format: {format_label(doc.fmt)}")
    if doc.fmt == "rr2016":
        print(f"metadata: version={doc.meta.save_version} data_size={doc.meta.data_size} "
              f"latest_slot={doc.meta.latest_slot + 1} latest_save={doc.meta.latest_save + 1}")
    if ref is None:
        for r in doc.list_slots():
            print(f"  [{r.label}] {r.summary}")
        return
    slot = doc.load_slot(ref)
    print(f"slot: {ref.label}  gil={_fmt_gil(slot.gil)}  playtime={slot.playtime_seconds / 3600:.1f}h")
    if slot.location:
        print(f"location: {slot.location}")
    party = slot.party_member_ids()
    if party is not None:
        names = [slot.character(i).name for i in party]
        print(f"party: {', '.join(names) if names else '(unknown)'}")
    print(f"{'#':<3}{'name':<10}{'lvl':>4}{'hp':>12}{'mp':>10}{'str':>5}{'spd':>5}{'mag':>5}{'spr':>5}")
    for char in slot.characters():
        if not char.is_recruited:
            continue
        hp = f"{char.get('cur_hp')}/{char.get('max_hp') if char.has('max_hp') else char.get('max_hp_base')}"
        mp = f"{char.get('cur_mp')}/{char.get('max_mp') if char.has('max_mp') else char.get('max_mp_base')}"
        stat = lambda n: char.get(n) if char.has(n) else char.get(n + "_base")
        print(f"{char.index:<3}{char.name:<10}{char.get('level'):>4}{hp:>12}{mp:>10}"
              f"{stat('strength'):>5}{stat('speed'):>5}{stat('magic'):>5}{stat('spirit'):>5}")
    wins, losses, draws = slot.card_record_stats()
    print(f"cards held: {len(slot.cards())}  record: {wins}W-{losses}L-{draws}D")
    items = slot.items()
    print(f"inventory: {len(items)} entries")


def _select_ref(doc: Document, args: argparse.Namespace) -> SlotRef:
    if doc.fmt == "rr2016":
        if args.slot is None or args.save is None:
            refs = doc.rr_list_slots()
            if not refs:
                raise SystemExit("No occupied rr2016 slots found. Pass --slot and --save explicitly.")
            if len(refs) == 1:
                return refs[0]
            raise SystemExit(
                "Multiple rr2016 slots found; pass --slot N --save M to pick one:\n"
                + "\n".join(f"  --slot {r.slot_id + 1} --save {r.save_id + 1}: {r.summary}" for r in refs)
            )
        ref = doc.rr_probe_slot(args.slot - 1, args.save - 1)
        if ref is None:
            raise SystemExit(f"Slot {args.slot} / File {args.save} is empty or unreadable.")
        return ref
    slots = doc.list_slots()
    if args.block is not None:
        for r in slots:
            if r.block_index == args.block:
                return r
        raise SystemExit(f"Block {args.block} is empty or not present.")
    if not slots:
        raise SystemExit("No save data found in this file.")
    if len(slots) > 1:
        raise SystemExit(
            "Multiple blocks found; pass --block N to pick one:\n"
            + "\n".join(f"  --block {r.block_index}: {r.summary}" for r in slots)
        )
    return slots[0]


def _bounded_int(label: str, minimum: int, maximum: int):
    def parse(value: str) -> int:
        try:
            number = int(value)
        except ValueError as exc:
            raise argparse.ArgumentTypeError(f"{label} must be an integer") from exc
        if not (minimum <= number <= maximum):
            raise argparse.ArgumentTypeError(f"{label} must be {minimum}-{maximum}")
        return number

    return parse


def build_arg_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Final Fantasy IX save editor (legacy PS1 + rr2016 Steam/PC/mobile).")
    p.add_argument("path", nargs="?", type=Path, help="save file to open")
    p.add_argument("--slot", type=_bounded_int("slot", 1, RR_MAX_SLOTS), metavar="N",
                   help="rr2016: slot number (1-9)")
    p.add_argument("--save", type=_bounded_int("file", 1, RR_MAX_SAVES), metavar="N",
                   help="rr2016: file number within the slot (1-15)")
    p.add_argument("--block", type=_bounded_int("block", 1, LEGACY_MAX_BLOCKS - 1), metavar="N",
                   help="legacy .mcr: memory-card block number (1-15)")

    p.add_argument("--inspect", action="store_true", help="print a summary and exit")
    p.add_argument("--list-slots", action="store_true", help="list all occupied slots and exit")
    p.add_argument("--list-known", nargs="?", const="", metavar="FILTER", help="list known item names and exit")

    p.add_argument("--character", metavar="NAME_OR_INDEX", help="target character for stat edits")
    p.add_argument("--max-character", action="store_true", help="max out the selected --character")
    p.add_argument("--max-all", action="store_true", help="max out every recruited character in the slot")

    p.add_argument("--set-gil", type=int, metavar="N")
    p.add_argument("--give-item", action="append", default=[], metavar="NAME_OR_ID",
                    help="add an item/piece of gear; repeatable")
    p.add_argument("--give-all-items", action="store_true", help="add every known item/piece of gear")
    p.add_argument("--quantity", type=int, default=99, metavar="N", help="quantity for --give-item / --give-all-items")

    output = p.add_mutually_exclusive_group()
    output.add_argument("--out", type=Path, metavar="PATH", help="write the edited save to a new file")
    output.add_argument("--in-place", action="store_true",
                        help="overwrite the input file (writes a numbered .bak backup first)")
    return p


def main(argv: list[str] | None = None) -> int:
    parser = build_arg_parser()
    args = parser.parse_args(argv)

    if args.list_known is not None:
        needle = args.list_known.lower()
        for item_id, name in enumerate(data.ITEM_NAMES):
            if item_id == data.EMPTY_ITEM_ID:
                continue
            if needle in name.lower():
                print(f"0x{item_id:02X}  {name}")
        return 0

    if args.path is None:
        parser.error("path is required unless --list-known is used")

    try:
        doc = open_document(args.path)
    except (OSError, ValueError) as exc:
        raise SystemExit(f"Could not open {args.path}: {exc}") from None

    if args.list_slots:
        cmd_inspect(doc, None)
        return 0

    ref = _select_ref(doc, args)

    if args.inspect and not (args.max_character or args.max_all or args.give_item
                              or args.give_all_items or args.set_gil is not None):
        cmd_inspect(doc, ref)
        return 0

    try:
        slot = doc.load_slot(ref)
        changed = []

        if args.max_character:
            char = resolve_character(slot, args.character) if args.character else None
            if char is None:
                raise ValueError("--max-character requires --character NAME_OR_INDEX")
            char.max_out()
            changed.append(f"maxed {char.name or char.index}")

        if args.max_all:
            n = 0
            for char in slot.characters():
                if char.is_recruited:
                    char.max_out()
                    n += 1
            changed.append(f"maxed {n} character(s)")

        if args.set_gil is not None:
            slot.gil = args.set_gil
            changed.append(f"gil={slot.gil}")

        quantity = max(0, min(args.quantity, 99))
        for token in args.give_item:
            item_id, ok = give_item(slot, token, quantity)
            changed.append(f"gave {data.item_name(item_id)} x{quantity}" + ("" if ok else " (inventory full!)"))

        if args.give_all_items:
            n = give_all_items(slot, quantity)
            changed.append(f"gave {n} items/gear pieces")
    except (IndexError, KeyError, TypeError, ValueError) as exc:
        raise SystemExit(f"Could not edit {ref.label}: {exc}") from None

    if not changed:
        cmd_inspect(doc, ref)
        return 0

    doc.commit_slot(ref, slot)
    for line in changed:
        print(line)

    try:
        if args.out:
            write_new_document(doc, args.out, input_path=args.path)
            print(f"wrote: {args.out}")
        elif args.in_place:
            backup = write_document_in_place(doc, args.path)
            print(f"wrote in-place; backup: {backup}")
        else:
            print("(no --out/--in-place given; nothing written to disk)")
    except (OSError, ValueError) as exc:
        raise SystemExit(f"Could not write save: {exc}") from None
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
