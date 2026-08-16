from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from textual.widgets import Input

import ffix_save_memoria as memoria
from ffix_save_tui import FFIXSaveApp
from tests.test_memoria import make_tree


class TuiTransactionTests(unittest.IsolatedAsyncioTestCase):
    async def test_invalid_character_form_does_not_partially_mutate_slot(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "SavedData_ww_Memoria_0_0.dat"
            path.write_bytes(memoria.serialize(make_tree()))
            app = FFIXSaveApp(str(path))

            async with app.run_test(size=(140, 50)) as pilot:
                await pilot.pause()
                app.query_one("#f_name", Input).value = "MUTATED"
                app.query_one("#eq_accessory", Input).value = "not-a-real-item"

                await app._apply_char_edits()

                self.assertEqual(app.slot.character(0).name, "Zidane")
                self.assertEqual(app.doc.memoria_slot.character(0).name, "Zidane")


if __name__ == "__main__":
    unittest.main()
