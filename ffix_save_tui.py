#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""
Textual TUI front-end for ffix_save_tool.py.

This file contains no save-format knowledge of its own. All offsets, the
AES/checksum math, and editing logic live in ffix_save_tool.py (and
ffix_save_data.py) and are imported here so the CLI and TUI never drift
apart.

Run:
    python3 ffix_save_tui.py [SAVE_FILE]

Requires the third-party "textual" and "pycryptodome" packages:
    pip install textual pycryptodome
"""
from __future__ import annotations

import sys
from pathlib import Path

from textual.app import App, ComposeResult
from textual.binding import Binding
from textual.containers import Horizontal, Vertical, VerticalScroll
from textual.screen import ModalScreen
from textual.widgets import (
    Button,
    DataTable,
    Footer,
    Header,
    Input,
    Label,
    RichLog,
    Select,
    SelectionList,
    Static,
    TabbedContent,
    TabPane,
)
from textual.widgets.selection_list import Selection

import ffix_save_data as data
import ffix_save_tool as tool


STAT_FIELDS = [
    ("level", "Level"),
    ("exp", "EXP"),
    ("cur_hp", "Cur HP"),
    ("max_hp", "Max HP"),
    ("cur_mp", "Cur MP"),
    ("max_mp", "Max MP"),
    ("strength", "Strength"),
    ("speed", "Speed"),
    ("magic", "Magic"),
    ("spirit", "Spirit"),
]
EQUIP_FIELDS = [
    ("weapon", "Weapon"),
    ("head", "Head"),
    ("arm", "Arm"),
    ("armor", "Armor"),
    ("accessory", "Accessory"),
]


def _field_name(char: tool.Character, key: str) -> str | None:
    if char.has(key):
        return key
    based = key + "_base"
    if char.has(based):
        return based
    return None


class ConfirmScreen(ModalScreen[bool]):
    CSS = """
    ConfirmScreen { align: center middle; }
    #dialog {
        width: 60; height: auto; border: thick $warning;
        background: $surface; padding: 1 2;
    }
    #buttons { height: auto; align: right middle; padding-top: 1; }
    """

    def __init__(self, message: str) -> None:
        super().__init__()
        self.message = message

    def compose(self) -> ComposeResult:
        with Vertical(id="dialog"):
            yield Label(self.message)
            with Horizontal(id="buttons"):
                yield Button("Cancel", id="cancel")
                yield Button("Overwrite", id="confirm", variant="warning")

    def on_button_pressed(self, event: Button.Pressed) -> None:
        self.dismiss(event.button.id == "confirm")


class FFIXSaveApp(App):
    TITLE = "Final Fantasy IX Save Editor"
    CSS = """
    #sidebar { width: 46; border: round $primary; padding: 1; }
    #main { border: round $primary; }
    #log { height: 10; border: round $primary; }
    .section-title { text-style: bold; color: $accent; margin-top: 1; }
    #char_editor { border: round $accent; padding: 1; height: 1fr; }
    #party_table { height: 12; }
    .stat-row { height: 3; }
    .stat-row Label { width: 12; content-align: left middle; }
    .stat-row Input { width: 1fr; }
    """
    BINDINGS = [
        Binding("q", "quit", "Quit"),
        Binding("r", "refresh_views", "Refresh"),
    ]

    def __init__(self, save_path: str | None = None) -> None:
        super().__init__()
        self.save_path = Path(save_path) if save_path else None
        self.doc: tool.Document | None = None
        self.ref: tool.SlotRef | None = None
        self.slot: tool.Slot | None = None
        self.current_char_index: int = 0
        self._slot_refs: list[tool.SlotRef] = []

    # ------------------------------------------------------------------ UI

    def compose(self) -> ComposeResult:
        yield Header()
        with Horizontal():
            with VerticalScroll(id="sidebar"):
                yield Label("Save file")
                yield Input(
                    value=str(self.save_path) if self.save_path else "",
                    placeholder="path/to/save (.dat/.sav/.mcr/.mcs/.ps1/raw)",
                    id="path_input",
                )
                yield Button("Load", id="load_btn", variant="primary")

                yield Static("Slot", classes="section-title")
                yield Select([], prompt="Load a file first…", id="slot_select")

                yield Static("Actions", classes="section-title")
                yield Button("Max Selected Character", id="max_char_btn")
                yield Button("Max All Characters", id="max_all_btn")

                yield Static("Gil", classes="section-title")
                yield Input(placeholder="gil amount", id="gil_input")
                yield Button("Set Gil", id="set_gil_btn")

                yield Static("Add item / gear", classes="section-title")
                yield Select(
                    [(name, str(i)) for i, name in enumerate(data.ITEM_NAMES) if i != data.EMPTY_ITEM_ID],
                    prompt="Pick from list…",
                    id="add_item_select",
                )
                yield Input(placeholder="or type name/0xID, e.g. Ragnarok", id="add_item_input")
                yield Input(value="99", id="quantity_input")
                yield Button("Add Item", id="add_item_btn")
                yield Button("Give All Items", id="give_all_btn")

                yield Static("Save changes", classes="section-title")
                yield Input(placeholder="output path (blank = auto name)", id="out_input")
                yield Button("Write New File", id="write_out_btn", variant="success")
                yield Button("Write In-Place (.bak backup)", id="write_inplace_btn", variant="warning")

            with Vertical():
                with TabbedContent(id="main"):
                    with TabPane("Party", id="tab_party"):
                        yield DataTable(id="party_table")
                        with VerticalScroll(id="char_editor"):
                            yield Static("Load a save to edit characters.", id="char_editor_title")
                    with TabPane("Items", id="tab_items"):
                        yield DataTable(id="items_table")
                    with TabPane("Cards", id="tab_cards"):
                        yield Static("", id="cards_status")
                        yield DataTable(id="cards_table")
                    with TabPane("Overview", id="tab_overview"):
                        yield Static("No file loaded.", id="overview_status")
                yield RichLog(id="log", wrap=True, markup=True)
        yield Footer()

    async def on_mount(self) -> None:
        party = self.query_one("#party_table", DataTable)
        party.cursor_type = "row"
        party.add_columns("#", "Name", "Lvl", "HP", "MP", "Str", "Spd", "Mag", "Spr")

        items = self.query_one("#items_table", DataTable)
        items.add_columns("ID", "Name", "Qty")

        cards = self.query_one("#cards_table", DataTable)
        cards.add_columns("#", "Type", "Attack", "AtkType", "P.Def", "M.Def", "Arrows")

        log = self.query_one("#log", RichLog)
        log.write("[bold]Ready.[/bold] Enter a save file path and press Load.")

        if self.save_path:
            await self._load_file(str(self.save_path))

    # --------------------------------------------------------------- helpers

    def log_line(self, text: str, style: str = "") -> None:
        log = self.query_one("#log", RichLog)
        log.write(f"[{style}]{text}[/{style}]" if style else text)

    def require_slot(self) -> bool:
        if self.slot is None:
            self.log_line("No slot loaded yet. Load a file and pick a slot.", "bold red")
            return False
        return True

    # --------------------------------------------------------------- loading

    async def _load_file(self, path_str: str) -> None:
        path = Path(path_str.strip())
        if not path_str.strip():
            self.log_line("Enter a save file path first.", "bold red")
            return
        try:
            doc = tool.open_document(path)
        except (OSError, ValueError) as exc:
            self.log_line(f"Could not open {path}: {exc}", "bold red")
            return

        self.save_path = path
        self.doc = doc
        self.slot = None
        self.ref = None
        self.log_line(f"Loaded {path} — {tool.format_label(doc.fmt)}.", "bold green")

        try:
            refs = doc.list_slots()
        except Exception as exc:  # noqa: BLE001 - surfacing to the log is the point
            self.log_line(f"Could not scan slots: {exc}", "bold red")
            refs = []
        self._slot_refs = refs

        select = self.query_one("#slot_select", Select)
        options = [(f"{r.label}: {r.summary}", str(i)) for i, r in enumerate(refs)]
        select.set_options(options)
        if refs:
            select.value = "0"
            await self._open_slot(0)
        else:
            self.log_line("No occupied slots found in this file.", "yellow")

    async def _open_slot(self, index: int) -> None:
        if self.doc is None or not (0 <= index < len(self._slot_refs)):
            return
        ref = self._slot_refs[index]
        self.slot = self.doc.load_slot(ref)
        self.ref = ref
        self.current_char_index = 0
        self.log_line(f"Opened {ref.label}.", "green")
        await self.refresh_views()

    async def on_select_changed(self, event: Select.Changed) -> None:
        if event.select.id == "slot_select" and event.value not in (None, Select.BLANK):
            await self._open_slot(int(event.value))
            return
        if event.select.id == "add_item_select" and event.value not in (None, Select.BLANK):
            self.query_one("#add_item_input", Input).value = data.ITEM_NAMES[int(event.value)]

    # --------------------------------------------------------------- views

    async def action_refresh_views(self) -> None:
        await self.refresh_views()

    async def refresh_views(self) -> None:
        if self.slot is None:
            return
        slot = self.slot

        party = self.query_one("#party_table", DataTable)
        party.clear()
        for char in slot.characters():
            hp_field = _field_name(char, "max_hp")
            mp_field = _field_name(char, "max_mp")
            max_hp = char.get(hp_field) if hp_field else "-"
            max_mp = char.get(mp_field) if mp_field else "-"
            party.add_row(
                str(char.index),
                char.name or "(empty)",
                str(char.get("level")),
                f"{char.get('cur_hp')}/{max_hp}",
                f"{char.get('cur_mp')}/{max_mp}",
                str(char.get(_field_name(char, "strength") or "level")),
                str(char.get(_field_name(char, "speed") or "level")),
                str(char.get(_field_name(char, "magic") or "level")),
                str(char.get(_field_name(char, "spirit") or "level")),
                key=str(char.index),
            )

        items_table = self.query_one("#items_table", DataTable)
        items_table.clear()
        for item_id, count, _slot_idx in sorted(slot.items(), key=lambda e: data.item_name(e[0])):
            items_table.add_row(f"0x{item_id:02X}", data.item_name(item_id), str(count))

        cards_table = self.query_one("#cards_table", DataTable)
        cards_table.clear()
        for rec in slot.cards():
            cards_table.add_row(
                str(rec["index"]),
                data.card_type_name(rec["type_id"]),
                str(rec["attack"]),
                "PMXA"[rec["attack_type"] % 4],
                str(rec["p_def"]),
                str(rec["m_def"]),
                f"0x{rec['arrows']:02X}",
            )
        wins, losses, draws = slot.card_record_stats()
        self.query_one("#cards_status", Static).update(f"Tetra Master record: {wins}W - {losses}L - {draws}D")

        overview = self.query_one("#overview_status", Static)
        lines = [
            f"format: {tool.format_label(slot.fmt)}",
            f"slot: {self.ref.label if self.ref else '?'}",
            f"gil: {slot.gil:,}",
            f"playtime: {slot.playtime_seconds / 3600:.1f} hours",
        ]
        if slot.location:
            lines.append(f"location: {slot.location}")
        party_ids = slot.party_member_ids()
        if party_ids is not None:
            names = ", ".join(slot.character(i).name for i in party_ids) or "(none detected)"
            lines.append(f"current party: {names}")
        overview.update("\n".join(lines))

        await self._refresh_char_editor()

    async def on_data_table_row_highlighted(self, event: DataTable.RowHighlighted) -> None:
        if event.data_table.id == "party_table" and event.row_key is not None and event.row_key.value is not None:
            self.current_char_index = int(event.row_key.value)
            await self._refresh_char_editor()

    # --------------------------------------------------------- character editor

    async def _refresh_char_editor(self) -> None:
        if self.slot is None:
            return
        char = self.slot.character(self.current_char_index)
        editor = self.query_one("#char_editor", VerticalScroll)
        await editor.remove_children()

        editor.mount(Static(f"[bold]{char.name or '(unnamed)'}[/bold]  (row {char.index})", id="char_editor_title"))
        name_row = Horizontal(Label("Name"), Input(value=char.name, id="f_name"), classes="stat-row")
        editor.mount(name_row)

        for key, label in STAT_FIELDS:
            field = _field_name(char, key)
            if field is None:
                continue
            row = Horizontal(Label(label), Input(value=str(char.get(field)), id=f"f_{key}"), classes="stat-row")
            editor.mount(row)

        for key, label in EQUIP_FIELDS:
            if not char.has(key):
                continue
            current = data.item_name(char.get(key))
            row = Horizontal(Label(label), Input(value=current, id=f"eq_{key}"), classes="stat-row")
            editor.mount(row)
        if char.fmt == "memoria":
            editor.mount(Static("[dim]Strength/Speed/Magic/Spirit are inferred from Memoria's own "
                                 "internal stat names — see NOTICES.md.[/dim]"))

        editor.mount(Button("Apply Changes", id="apply_char_btn", variant="primary"))

        if char.fmt == "legacy":
            equipped = set(char.support_abilities())
            selections = [
                Selection(name, str(i), i in equipped) for i, name in enumerate(data.SUPPORT_ABILITY_NAMES)
            ]
            editor.mount(Static("Support abilities", classes="section-title"))
            editor.mount(SelectionList[str](*selections, id="support_list"))
            editor.mount(Button("Apply Support Abilities", id="apply_support_btn"))

    async def on_button_pressed(self, event: Button.Pressed) -> None:  # noqa: C901 - dispatch table
        button_id = event.button.id

        if button_id == "load_btn":
            await self._load_file(self.query_one("#path_input", Input).value)
            return

        if button_id == "apply_char_btn":
            await self._apply_char_edits()
            return
        if button_id == "apply_support_btn":
            await self._apply_support_abilities()
            return

        if not self.require_slot():
            return

        if button_id == "max_char_btn":
            char = self.slot.character(self.current_char_index)
            char.max_out()
            await self._commit(f"maxed {char.name or char.index}")
        elif button_id == "max_all_btn":
            n = 0
            for char in self.slot.characters():
                if char.is_recruited:
                    char.max_out()
                    n += 1
            await self._commit(f"maxed {n} character(s)")
        elif button_id == "set_gil_btn":
            raw = self.query_one("#gil_input", Input).value.strip()
            try:
                amount = int(raw)
            except ValueError:
                self.log_line(f"Not a number: {raw!r}", "yellow")
                return
            self.slot.gil = amount
            await self._commit(f"gil set to {self.slot.gil:,}")
        elif button_id == "add_item_btn":
            token = self.query_one("#add_item_input", Input).value.strip()
            if not token:
                self.log_line("Enter an item/gear name or hex ID first.", "yellow")
                return
            try:
                item_id = data.resolve_item_id(token)
            except ValueError as exc:
                self.log_line(f"error: {exc}", "bold red")
                return
            qty = self._quantity()
            ok = self.slot.set_item(item_id, qty)
            await self._commit(f"{'gave' if ok else 'FAILED to give (inventory full)'} {data.item_name(item_id)} x{qty}")
        elif button_id == "give_all_btn":
            qty = self._quantity()
            n = tool.give_all_items(self.slot, qty)
            await self._commit(f"gave {n} items/gear pieces")
        elif button_id == "write_out_btn":
            self._write_out()
        elif button_id == "write_inplace_btn":
            self.push_screen(
                ConfirmScreen(
                    f"Overwrite {self.save_path} in place?\nA new numbered .bak backup will be written first."
                ),
                self._on_inplace_confirmed,
            )

    def _quantity(self) -> int:
        raw = self.query_one("#quantity_input", Input).value.strip()
        try:
            return max(0, min(int(raw), 99))
        except ValueError:
            return 99

    async def _apply_char_edits(self) -> None:
        if not self.require_slot():
            return
        candidate = self.slot.clone()
        char = candidate.character(self.current_char_index)
        try:
            char.name = self.query_one("#f_name", Input).value
            for key, _label in STAT_FIELDS:
                field = _field_name(char, key)
                if field is None:
                    continue
                widget = self.query_one(f"#f_{key}", Input)
                value = int(widget.value.strip())
                char.set(field, value)
            for key, _label in EQUIP_FIELDS:
                if not char.has(key):
                    continue
                widget = self.query_one(f"#eq_{key}", Input)
                token = widget.value.strip()
                if not token:
                    continue
                item_id = data.resolve_item_id(token)
                char.set(key, item_id)
        except ValueError as exc:
            self.log_line(f"error: {exc}", "bold red")
            return
        self.slot = candidate
        await self._commit(f"applied edits to {char.name or char.index}")

    async def _apply_support_abilities(self) -> None:
        if not self.require_slot():
            return
        char = self.slot.character(self.current_char_index)
        if char.fmt != "legacy":
            self.log_line("Support abilities are only editable for legacy saves.", "yellow")
            return
        selection_list = self.query_one("#support_list", SelectionList)
        selected = set(int(v) for v in selection_list.selected)
        for bit_index in range(len(data.SUPPORT_ABILITY_NAMES)):
            char.set_support_ability(bit_index, bit_index in selected)
        await self._commit(f"applied support abilities to {char.name or char.index}")

    async def _commit(self, message: str) -> None:
        assert self.doc is not None and self.ref is not None and self.slot is not None
        self.doc.commit_slot(self.ref, self.slot)
        self.slot = self.doc.load_slot(self.ref)
        self.log_line(message, "green")
        await self.refresh_views()

    def _write_out(self) -> None:
        if self.doc is None:
            self.log_line("No file loaded.", "bold red")
            return
        out_value = self.query_one("#out_input", Input).value.strip()
        if out_value:
            out_path = Path(out_value)
        elif self.save_path is not None:
            out_path = self.save_path.with_name(self.save_path.stem + ".edited" + self.save_path.suffix)
        else:
            self.log_line("No output path and no loaded file path to derive one from.", "bold red")
            return
        try:
            tool.write_new_document(self.doc, out_path, input_path=self.save_path)
        except (OSError, ValueError) as exc:
            self.log_line(f"Could not write {out_path}: {exc}", "bold red")
            return
        self.log_line(f"wrote: {out_path}", "bold green")

    def _on_inplace_confirmed(self, confirmed: bool | None) -> None:
        if not confirmed or self.doc is None or self.save_path is None:
            return
        try:
            backup = tool.write_document_in_place(self.doc, self.save_path)
        except (OSError, ValueError) as exc:
            self.log_line(f"In-place write failed: {exc}", "bold red")
            return
        self.log_line(f"wrote in-place; backup: {backup}", "bold green")


def main(argv: list[str] | None = None) -> int:
    argv = argv if argv is not None else sys.argv[1:]
    save_path = argv[0] if argv else None
    FFIXSaveApp(save_path).run()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
