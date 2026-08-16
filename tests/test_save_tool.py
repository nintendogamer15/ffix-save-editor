from __future__ import annotations

import contextlib
import io
import struct
import tempfile
import unittest
from pathlib import Path

import ffix_save_data as data
import ffix_save_tool as tool


def make_legacy_block(*, gil: int = 3_435, name: str = "Zidane") -> bytearray:
    block = bytearray(tool.LEGACY_BLOCK_SIZE)
    block[:2] = b"SC"
    block[tool.LEGACY_LEADER_LEVEL_OFFSET] = 7
    block[
        tool.LEGACY_LEADER_NAME_OFFSET:
        tool.LEGACY_LEADER_NAME_OFFSET + tool.LEGACY_LEADER_NAME_LEN
    ] = data.encode_legacy_text(name, tool.LEGACY_LEADER_NAME_LEN)
    block[
        tool.LEGACY_LOCATION_OFFSET:
        tool.LEGACY_LOCATION_OFFSET + tool.LEGACY_LOCATION_LEN
    ] = data.encode_legacy_text("Lindblum/Inn", tool.LEGACY_LOCATION_LEN)
    tool.set_field(block, 0, tool.LEGACY_GIL_OFFSET, gil)
    tool.set_field(block, 0, tool.LEGACY_PREVIEW_GIL_OFFSET, gil)
    tool.set_field(block, 0, tool.LEGACY_PLAYTIME_OFFSET, 60 * 60 * 60)
    char = tool.Slot(block, "legacy").character(0)
    char.name = name
    char.set("level", 7)
    tool.legacy_fix_checksum(block)
    return block


def set_directory_entry(card: bytearray, block_index: int, product: bytes) -> None:
    start = tool.LEGACY_BLOCK_HEADER_SIZE * block_index
    header = bytearray(tool.LEGACY_BLOCK_HEADER_SIZE)
    header[0] = 0x51
    header[5:7] = tool.LEGACY_BLOCK_SIZE.to_bytes(2, "little")
    header[8:10] = b"\xff\xff"
    header[tool.LEGACY_REGION_CODE_OFFSET:tool.LEGACY_REGION_CODE_OFFSET + len(product)] = product
    checksum = 0
    for value in header[:-1]:
        checksum ^= value
    header[-1] = checksum
    card[start:start + len(header)] = header


def make_rr_container() -> bytearray:
    container = bytearray(tool.RR_CONTAINER_SIZE)
    metadata = bytearray(tool.RR_META_PLAINTEXT_SIZE)
    metadata[:4] = b"SAVE"
    struct.pack_into(
        "<fiiidiibBB",
        metadata,
        4,
        1.0,
        tool.RR_SLOT_PLAINTEXT_SIZE - 4,
        0,
        0,
        0.0,
        0,
        -1,
        0,
        0,
        0,
    )
    encrypted_metadata = tool.rr_encrypt(bytes(metadata))
    container[:len(encrypted_metadata)] = encrypted_metadata

    slot = tool.Slot(bytearray(tool.RR_SLOT_PLAINTEXT_SIZE), "rr2016")
    slot.buf[:4] = tool.RR_OCCUPIED_HEADER
    char = slot.character(0)
    char.name = "Zidane"
    char.set("level", 1)
    char.set("cur_hp", 105)
    char.set("max_hp", 105)
    char.set("max_hp_base", 105)
    char.set("cur_mp", 36)
    char.set("max_mp", 36)
    char.set("max_mp_base", 36)
    encrypted_slot = tool.rr_encrypt(bytes(slot.buf))
    start = tool.rr_chunk_offset(0, 0)
    container[start:start + len(encrypted_slot)] = encrypted_slot

    empty = bytearray(tool.RR_SLOT_PLAINTEXT_SIZE)
    empty[:4] = tool.RR_EMPTY_HEADER
    encrypted_empty = tool.rr_encrypt(bytes(empty))
    start = tool.rr_chunk_offset(0, 1)
    container[start:start + len(encrypted_empty)] = encrypted_empty
    return container


class LegacyTests(unittest.TestCase):
    def test_gil_updates_gameplay_preview_and_checksum(self) -> None:
        block = make_legacy_block()
        slot = tool.Slot(block, "legacy")
        slot.gil = 7_654_321
        slot.finalize()

        self.assertEqual(tool.get_field(block, 0, tool.LEGACY_GIL_OFFSET), 7_654_321)
        self.assertEqual(tool.get_field(block, 0, tool.LEGACY_PREVIEW_GIL_OFFSET), 7_654_321)
        self.assertEqual(tool.read_uint(block, tool.LEGACY_CHECKSUM_OFFSET, 2), tool.legacy_checksum(block))

    def test_full_card_lists_only_ffix_blocks(self) -> None:
        card = bytearray(tool.LEGACY_CARD_SIZE)
        card[:2] = b"MC"
        set_directory_entry(card, 1, b"BASLUS-0125100000-00")
        set_directory_entry(card, 2, b"BASLUS-9999900000-00")
        card[tool.LEGACY_BLOCK_SIZE:tool.LEGACY_BLOCK_SIZE * 2] = make_legacy_block()
        card[tool.LEGACY_BLOCK_SIZE * 2:tool.LEGACY_BLOCK_SIZE * 3] = make_legacy_block(name="Other")

        refs = tool.Document("legacy", card).list_slots()

        self.assertEqual([ref.block_index for ref in refs], [1])

    def test_ntsc_playtime_is_converted_from_sixty_hz_frames(self) -> None:
        card = bytearray(tool.LEGACY_CARD_SIZE)
        card[:2] = b"MC"
        set_directory_entry(card, 1, b"BASLUS-0125100000-00")
        card[tool.LEGACY_BLOCK_SIZE:tool.LEGACY_BLOCK_SIZE * 2] = make_legacy_block()
        doc = tool.Document("legacy", card)
        ref = doc.list_slots()[0]
        self.assertEqual(doc.load_slot(ref).playtime_seconds, 3_600)

    def test_pal_playtime_is_converted_from_fifty_hz_frames(self) -> None:
        card = bytearray(tool.LEGACY_CARD_SIZE)
        card[:2] = b"MC"
        set_directory_entry(card, 1, b"BESLES-0296500000-00")
        block = make_legacy_block()
        tool.set_field(block, 0, tool.LEGACY_PLAYTIME_OFFSET, 50 * 60 * 60)
        card[tool.LEGACY_BLOCK_SIZE:tool.LEGACY_BLOCK_SIZE * 2] = block
        doc = tool.Document("legacy", card)
        ref = doc.list_slots()[0]
        self.assertEqual(doc.load_slot(ref).playtime_seconds, 3_600)

    def test_wrong_sized_legacy_extension_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "Expected exactly"):
            tool.detect_format(Path("broken.mcr"), b"not a save")


class RR2016Tests(unittest.TestCase):
    def test_vanilla_key_derivation(self) -> None:
        key, iv = tool._rr_aes_key_iv()
        self.assertEqual(key.hex(), "10b45d06bbc66bedb11a2f44f1911e072e97ffc29ce44b92f97474a710b8a5d5")
        self.assertEqual(iv.hex(), "a83e99c0c895e1acaf11ccd982f9715f")

    def test_empty_encrypted_slots_are_not_listed(self) -> None:
        doc = tool.Document("rr2016", make_rr_container())
        doc.meta = tool.rr_parse_metadata(doc.raw)

        refs = doc.list_slots()

        self.assertEqual([(ref.slot_id, ref.save_id) for ref in refs], [(0, 0)])
        self.assertIsNone(doc.rr_probe_slot(0, 1))

    def test_max_out_updates_basis_and_live_hp_mp(self) -> None:
        slot = tool.Slot(bytearray(tool.RR_SLOT_PLAINTEXT_SIZE), "rr2016")
        slot.buf[:4] = tool.RR_OCCUPIED_HEADER
        char = slot.character(0)
        char.max_out()

        self.assertEqual((char.get("cur_hp"), char.get("max_hp"), char.get("max_hp_base")), (9999, 9999, 9999))
        self.assertEqual((char.get("cur_mp"), char.get("max_mp"), char.get("max_mp_base")), (999, 999, 999))

    def test_character_name_is_eight_bytes_and_preserves_following_byte(self) -> None:
        slot = tool.Slot(bytearray(tool.RR_SLOT_PLAINTEXT_SIZE), "rr2016")
        char = slot.character(0)
        following = char.base + tool.RR_CHAR_NAME_OFFSET + tool.RR_CHAR_NAME_LEN
        slot.buf[following] = 0xA5

        char.name = "123456789"

        self.assertEqual(char.name, "12345678")
        self.assertEqual(slot.buf[following], 0xA5)

    def test_card_record_labels_use_correct_offsets(self) -> None:
        slot = tool.Slot(bytearray(tool.RR_SLOT_PLAINTEXT_SIZE), "rr2016")
        tool.set_field(slot.buf, 0, tool.RR_CARD_WINS_OFFSET, 3)
        tool.set_field(slot.buf, 0, tool.RR_CARD_LOSSES_OFFSET, 2)
        tool.set_field(slot.buf, 0, tool.RR_CARD_DRAWS_OFFSET, 1)
        self.assertEqual(slot.card_record_stats(), (3, 2, 1))

    def test_edit_reencrypts_and_reopens(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            source = Path(temp_dir) / "SavedData_ww.dat"
            source.write_bytes(make_rr_container())
            doc = tool.open_document(source)
            ref = doc.list_slots()[0]
            slot = doc.load_slot(ref)
            slot.gil = 1_234_567
            doc.commit_slot(ref, slot)
            out = Path(temp_dir) / "edited.dat"
            tool.write_new_document(doc, out, input_path=source)

            reopened = tool.open_document(out)
            edited = reopened.load_slot(reopened.list_slots()[0])
            self.assertEqual(edited.gil, 1_234_567)


class OutputAndCliTests(unittest.TestCase):
    def test_write_new_refuses_input_path(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "save.raw"
            path.write_bytes(make_legacy_block())
            doc = tool.open_document(path)
            with self.assertRaisesRegex(ValueError, "use in-place"):
                tool.write_new_document(doc, path, input_path=path)

    def test_in_place_writes_numbered_backups(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "save.raw"
            original = bytes(make_legacy_block())
            path.write_bytes(original)
            doc = tool.open_document(path)
            ref = doc.list_slots()[0]
            slot = doc.load_slot(ref)
            slot.gil = 999
            doc.commit_slot(ref, slot)

            first = tool.write_document_in_place(doc, path)
            second = tool.write_document_in_place(doc, path)

            self.assertEqual(first.name, "save.raw.bak")
            self.assertEqual(first.read_bytes(), original)
            self.assertEqual(second.name, "save.raw.bak.1")

    def test_list_known_does_not_require_a_save_path(self) -> None:
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = tool.main(["--list-known", "ragnarok"])
        self.assertEqual(result, 0)
        self.assertIn("Ragnarok", output.getvalue())


if __name__ == "__main__":
    unittest.main()
