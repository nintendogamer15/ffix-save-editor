from __future__ import annotations

import struct
import unittest

import ffix_save_memoria as memoria
import ffix_save_tool as tool


def make_tree() -> dict:
    return {
        "95000_Setting": {"00001_time": 12.5},
        "20000_Event": {},
        "40000_Common": {
            "gil": 500,
            "items": [{"id": 236, "count": 2}],
            "players": [
                {
                    "name": "Zidane",
                    "level": 1,
                    "exp": 0,
                    "cur": {"hp": 105, "mp": 36},
                    "max": {"hp": 105, "mp": 36},
                    "basis": {
                        "max_hp": 105,
                        "max_mp": 36,
                        "dex": 23,
                        "str": 21,
                        "mgc": 18,
                        "wpr": 23,
                    },
                    "elem": {"dex": 23, "str": 21, "mgc": 18, "wpr": 23},
                    "trance": 0,
                    "equip": [1, 112, 88, 149, 255],
                }
            ],
        },
        "30000_MiniGame": {"MiniGameCard": [], "sWin": 0, "sLose": 0, "sDraw": 0},
    }


class MemoriaCodecTests(unittest.TestCase):
    def test_round_trip(self) -> None:
        encoded = memoria.serialize(make_tree())
        self.assertEqual(memoria.serialize(memoria.parse(encoded)), encoded)
        self.assertTrue(memoria.looks_like_memoria(encoded))

    def test_negative_collection_count_is_rejected(self) -> None:
        encoded = struct.pack("<ii", 2, -1)
        with self.assertRaisesRegex(memoria.MemoriaFormatError, "invalid dictionary count"):
            memoria.parse(encoded)
        self.assertFalse(memoria.looks_like_memoria(encoded))

    def test_generic_tagged_dictionary_is_not_misdetected_as_a_save(self) -> None:
        self.assertFalse(memoria.looks_like_memoria(struct.pack("<ii", 2, 0)))

    def test_duplicate_dictionary_key_is_rejected(self) -> None:
        encoded = bytearray(struct.pack("<ii", 2, 2))
        for value in (1, 2):
            encoded += b"\x01x"
            encoded += struct.pack("<ii", 4, value)
        with self.assertRaisesRegex(memoria.MemoriaFormatError, "duplicate dictionary key"):
            memoria.parse(bytes(encoded))


class MemoriaDocumentTests(unittest.TestCase):
    def test_gil_is_clamped_to_the_game_limit(self) -> None:
        slot = memoria.MemoriaSlot(make_tree())
        slot.gil = 99_999_999
        self.assertEqual(slot.gil, 9_999_999)

    def test_loaded_slot_is_detached_until_commit(self) -> None:
        document = tool.Document(
            "memoria",
            bytearray(),
            memoria_slot=memoria.MemoriaSlot(make_tree()),
        )
        ref = document.list_slots()[0]
        loaded = document.load_slot(ref)
        loaded.gil = 999

        self.assertEqual(memoria.parse(document.to_bytes())["40000_Common"]["gil"], 500)

        document.commit_slot(ref, loaded)
        self.assertEqual(memoria.parse(document.to_bytes())["40000_Common"]["gil"], 999)


if __name__ == "__main__":
    unittest.main()
