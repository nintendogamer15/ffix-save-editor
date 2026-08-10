#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""
Simple Qt (PySide6) GUI front-end for ffix_save_tool.py.

Like ffix_save_tui.py, this file contains no save-format knowledge of its
own — everything comes from ffix_save_tool.py / ffix_save_data.py /
ffix_save_memoria.py, so all three front-ends (CLI, TUI, GUI) stay in sync.

Why Qt instead of Tkinter: Tk has no desktop-portal integration (its file
picker is a dated fallback dialog, not the host's native one) and computes
font scaling from the display server's self-reported DPI, which is wrong
often enough in practice (virtual displays, some multi-monitor setups) to
produce illegibly small text. Qt uses the platform's native file dialog
(the actual KDE/GNOME/Windows/macOS picker) and handles HiDPI scaling
automatically and far more reliably.

Run:
    python3 ffix_save_gui.py [SAVE_FILE]

Requires PySide6 and pycryptodome (for rr2016 saves). This is the file
packaged into the portable Windows .exe and the Linux AppImage — see
windows/ and linux/.
"""
from __future__ import annotations

import sys
from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QIcon, QPalette
from PySide6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QComboBox,
    QFileDialog,
    QGridLayout,
    QGroupBox,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QLineEdit,
    QMainWindow,
    QMessageBox,
    QPlainTextEdit,
    QPushButton,
    QTableWidget,
    QTableWidgetItem,
    QTabWidget,
    QVBoxLayout,
    QWidget,
)

import ffix_save_data as data
import ffix_save_tool as tool

ASSETS_DIR = Path(__file__).resolve().parent / "assets"

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
PARTY_HEADINGS = ("#", "Name", "Lvl", "HP", "MP", "Str", "Spd", "Mag", "Spr")
ITEMS_HEADINGS = ("ID", "Name", "Qty")
CARDS_HEADINGS = ("#", "Type", "Attack", "AtkType", "P.Def", "M.Def", "Arrows")


def _field_name(char: tool.Character, key: str) -> str | None:
    if char.has(key):
        return key
    based = key + "_base"
    if char.has(based):
        return based
    return None


def _dark_palette() -> QPalette:
    p = QPalette()
    window = QColor(37, 37, 38)
    base = QColor(30, 30, 30)
    alt_base = QColor(45, 45, 48)
    text = QColor(240, 240, 240)
    disabled_text = QColor(127, 127, 127)
    highlight = QColor(42, 130, 218)
    p.setColor(QPalette.Window, window)
    p.setColor(QPalette.WindowText, text)
    p.setColor(QPalette.Base, base)
    p.setColor(QPalette.AlternateBase, alt_base)
    p.setColor(QPalette.ToolTipBase, window)
    p.setColor(QPalette.ToolTipText, text)
    p.setColor(QPalette.Text, text)
    p.setColor(QPalette.Disabled, QPalette.Text, disabled_text)
    p.setColor(QPalette.Button, window)
    p.setColor(QPalette.ButtonText, text)
    p.setColor(QPalette.Disabled, QPalette.ButtonText, disabled_text)
    p.setColor(QPalette.BrightText, QColor(255, 80, 80))
    p.setColor(QPalette.Link, highlight)
    p.setColor(QPalette.Highlight, highlight)
    p.setColor(QPalette.HighlightedText, QColor(0, 0, 0))
    return p


def _make_table(headings: tuple[str, ...]) -> QTableWidget:
    table = QTableWidget(0, len(headings))
    table.setHorizontalHeaderLabels(headings)
    table.setEditTriggers(QAbstractItemView.NoEditTriggers)
    table.setSelectionBehavior(QAbstractItemView.SelectRows)
    table.setSelectionMode(QAbstractItemView.SingleSelection)
    table.verticalHeader().setVisible(False)
    table.horizontalHeader().setSectionResizeMode(QHeaderView.Stretch)
    return table


def _set_row(table: QTableWidget, row: int, values: tuple) -> None:
    for col, value in enumerate(values):
        item = QTableWidgetItem(str(value))
        item.setTextAlignment(Qt.AlignCenter)
        table.setItem(row, col, item)


class FFIXSaveGUI(QMainWindow):
    def __init__(self, save_path: str | None = None) -> None:
        super().__init__()
        self.setWindowTitle("Final Fantasy IX Save Editor")
        icon_path = ASSETS_DIR / "icon.png"
        if icon_path.exists():
            self.setWindowIcon(QIcon(str(icon_path)))
        self.resize(1200, 860)

        self._dark = True
        self._light_palette = QApplication.palette()

        self.doc: tool.Document | None = None
        self.ref: tool.SlotRef | None = None
        self.slot: tool.Slot | object | None = None
        self.save_path: Path | None = Path(save_path) if save_path else None
        self._slot_refs: list[tool.SlotRef] = []
        self._current_char_index = 0
        self._stat_edits: dict[str, QLineEdit] = {}
        self._equip_edits: dict[str, QLineEdit] = {}
        self._name_edit: QLineEdit | None = None

        self._build_ui()
        self._apply_theme()
        if self.save_path:
            self._load_file(str(self.save_path))

    # ------------------------------------------------------------------ layout

    def _build_ui(self) -> None:
        central = QWidget()
        self.setCentralWidget(central)
        root = QVBoxLayout(central)

        top = QHBoxLayout()
        root.addLayout(top)
        top.addWidget(QLabel("Save file:"))
        self.path_edit = QLineEdit(str(self.save_path) if self.save_path else "")
        top.addWidget(self.path_edit, 1)
        browse_btn = QPushButton("Browse...")
        browse_btn.clicked.connect(self._browse)
        top.addWidget(browse_btn)
        load_btn = QPushButton("Load")
        load_btn.clicked.connect(lambda: self._load_file(self.path_edit.text()))
        top.addWidget(load_btn)
        top.addWidget(QLabel("Slot:"))
        self.slot_combo = QComboBox()
        self.slot_combo.setMinimumWidth(320)
        self.slot_combo.currentIndexChanged.connect(self._on_slot_changed)
        top.addWidget(self.slot_combo)
        theme_btn = QPushButton("Toggle Theme")
        theme_btn.clicked.connect(self._toggle_theme)
        top.addWidget(theme_btn)

        self.tabs = QTabWidget()
        root.addWidget(self.tabs, 1)
        self._build_party_tab()
        self._build_items_tab()
        self._build_cards_tab()
        self._build_overview_tab()

        bottom = QHBoxLayout()
        root.addLayout(bottom)
        self._build_save_controls(bottom)

        log_group = QGroupBox("Log")
        root.addWidget(log_group)
        log_layout = QVBoxLayout(log_group)
        self.log_text = QPlainTextEdit()
        self.log_text.setReadOnly(True)
        self.log_text.setFixedHeight(120)
        log_layout.addWidget(self.log_text)
        self.log_line("Ready. Choose a save file and press Load.")

    def _build_party_tab(self) -> None:
        tab = QWidget()
        self.tabs.addTab(tab, "Party")
        layout = QVBoxLayout(tab)

        self.party_table = _make_table(PARTY_HEADINGS)
        self.party_table.itemSelectionChanged.connect(self._on_party_select)
        layout.addWidget(self.party_table, 1)

        actions = QHBoxLayout()
        layout.addLayout(actions)
        max_char_btn = QPushButton("Max Selected Character")
        max_char_btn.clicked.connect(self._max_selected_character)
        actions.addWidget(max_char_btn)
        max_all_btn = QPushButton("Max All Characters")
        max_all_btn.clicked.connect(self._max_all_characters)
        actions.addWidget(max_all_btn)
        actions.addStretch(1)

        editor_group = QGroupBox("Character editor")
        layout.addWidget(editor_group)
        editor = QVBoxLayout(editor_group)
        self.editor_title = QLabel("Load a save to edit characters.")
        self.editor_title.setStyleSheet("font-weight: bold;")
        editor.addWidget(self.editor_title)

        name_row = QHBoxLayout()
        editor.addLayout(name_row)
        name_row.addWidget(QLabel("Name"))
        self._name_edit = QLineEdit()
        self._name_edit.setMaximumWidth(160)
        name_row.addWidget(self._name_edit)
        name_row.addStretch(1)

        stat_grid = QGridLayout()
        editor.addLayout(stat_grid)
        for i, (key, label) in enumerate(STAT_FIELDS):
            edit = QLineEdit()
            edit.setMaximumWidth(90)
            self._stat_edits[key] = edit
            r, c = divmod(i, 5)
            stat_grid.addWidget(QLabel(label), r, c * 2)
            stat_grid.addWidget(edit, r, c * 2 + 1)

        equip_row = QHBoxLayout()
        editor.addLayout(equip_row)
        for key, label in EQUIP_FIELDS:
            edit = QLineEdit()
            self._equip_edits[key] = edit
            equip_row.addWidget(QLabel(label))
            equip_row.addWidget(edit)

        self.editor_note = QLabel("")
        self.editor_note.setStyleSheet("color: #888888;")
        editor.addWidget(self.editor_note)

        apply_btn = QPushButton("Apply Changes")
        apply_btn.clicked.connect(self._apply_char_edits)
        editor.addWidget(apply_btn, alignment=Qt.AlignLeft)

    def _build_items_tab(self) -> None:
        tab = QWidget()
        self.tabs.addTab(tab, "Items")
        layout = QVBoxLayout(tab)

        self.items_table = _make_table(ITEMS_HEADINGS)
        layout.addWidget(self.items_table, 1)

        add_group = QGroupBox("Add item / gear")
        layout.addWidget(add_group)
        add_row = QHBoxLayout(add_group)
        self.item_combo = QComboBox()
        self.item_combo.setEditable(True)
        self.item_combo.addItems(sorted(n for i, n in enumerate(data.ITEM_NAMES) if i != data.EMPTY_ITEM_ID))
        self.item_combo.setCurrentText("")
        self.item_combo.setMinimumWidth(240)
        add_row.addWidget(self.item_combo)
        add_row.addWidget(QLabel("Qty"))
        self.item_qty_edit = QLineEdit("99")
        self.item_qty_edit.setMaximumWidth(60)
        add_row.addWidget(self.item_qty_edit)
        add_item_btn = QPushButton("Add Item")
        add_item_btn.clicked.connect(self._add_item)
        add_row.addWidget(add_item_btn)
        give_all_btn = QPushButton("Give All Items")
        give_all_btn.clicked.connect(self._give_all_items)
        add_row.addWidget(give_all_btn)
        add_row.addStretch(1)

    def _build_cards_tab(self) -> None:
        tab = QWidget()
        self.tabs.addTab(tab, "Cards")
        layout = QVBoxLayout(tab)
        self.cards_status = QLabel("")
        layout.addWidget(self.cards_status)
        self.cards_table = _make_table(CARDS_HEADINGS)
        layout.addWidget(self.cards_table, 1)

    def _build_overview_tab(self) -> None:
        tab = QWidget()
        self.tabs.addTab(tab, "Overview")
        layout = QVBoxLayout(tab)
        self.overview_label = QLabel("No file loaded.")
        self.overview_label.setTextInteractionFlags(Qt.TextSelectableByMouse)
        self.overview_label.setAlignment(Qt.AlignTop | Qt.AlignLeft)
        layout.addWidget(self.overview_label, 1)

    def _build_save_controls(self, layout: QHBoxLayout) -> None:
        layout.addWidget(QLabel("Gil:"))
        self.gil_edit = QLineEdit()
        self.gil_edit.setMaximumWidth(100)
        layout.addWidget(self.gil_edit)
        set_gil_btn = QPushButton("Set Gil")
        set_gil_btn.clicked.connect(self._set_gil)
        layout.addWidget(set_gil_btn)

        layout.addWidget(QLabel("Output:"))
        self.out_edit = QLineEdit()
        layout.addWidget(self.out_edit, 1)
        out_browse_btn = QPushButton("Browse...")
        out_browse_btn.clicked.connect(self._browse_out)
        layout.addWidget(out_browse_btn)
        write_out_btn = QPushButton("Write New File")
        write_out_btn.clicked.connect(self._write_out)
        layout.addWidget(write_out_btn)
        write_inplace_btn = QPushButton("Write In-Place")
        write_inplace_btn.clicked.connect(self._write_inplace)
        layout.addWidget(write_inplace_btn)

    # --------------------------------------------------------------- theming

    def _apply_theme(self) -> None:
        app = QApplication.instance()
        app.setStyle("Fusion")
        app.setPalette(_dark_palette() if self._dark else self._light_palette)

    def _toggle_theme(self) -> None:
        self._dark = not self._dark
        self._apply_theme()

    # --------------------------------------------------------------- helpers

    def log_line(self, text: str) -> None:
        self.log_text.appendPlainText(text)

    def require_slot(self) -> bool:
        if self.slot is None:
            self.log_line("No slot loaded yet.")
            return False
        return True

    def _browse(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, "Open save file")
        if path:
            self.path_edit.setText(path)
            self._load_file(path)

    def _browse_out(self) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "Write save as")
        if path:
            self.out_edit.setText(path)

    # --------------------------------------------------------------- loading

    def _load_file(self, path_str: str) -> None:
        path_str = path_str.strip()
        if not path_str:
            self.log_line("Enter a save file path first.")
            return
        path = Path(path_str)
        try:
            doc = tool.open_document(path)
        except (OSError, ValueError) as exc:
            self.log_line(f"Could not open {path}: {exc}")
            QMessageBox.critical(self, "Could not open file", str(exc))
            return

        self.save_path = path
        self.doc = doc
        self.slot = None
        self.ref = None
        self.log_line(f"Loaded {path} - {tool.format_label(doc.fmt)}.")

        try:
            refs = doc.list_slots()
        except Exception as exc:  # noqa: BLE001
            self.log_line(f"Could not scan slots: {exc}")
            refs = []
        self._slot_refs = refs

        self.slot_combo.blockSignals(True)
        self.slot_combo.clear()
        self.slot_combo.addItems([f"{r.label}: {r.summary}" for r in refs])
        self.slot_combo.blockSignals(False)
        if refs:
            self.slot_combo.setCurrentIndex(0)
            self._open_slot(0)
        else:
            self.log_line("No occupied slots found in this file.")

    def _on_slot_changed(self, index: int) -> None:
        if index >= 0:
            self._open_slot(index)

    def _open_slot(self, index: int) -> None:
        if self.doc is None or not (0 <= index < len(self._slot_refs)):
            return
        ref = self._slot_refs[index]
        self.slot = self.doc.load_slot(ref)
        self.ref = ref
        self._current_char_index = 0
        self.log_line(f"Opened {ref.label}.")
        self.refresh_views()

    # --------------------------------------------------------------- views

    def refresh_views(self) -> None:
        if self.slot is None:
            return
        slot = self.slot

        characters = slot.characters()
        self.party_table.setRowCount(len(characters))
        for char in characters:
            hp_field = _field_name(char, "max_hp")
            mp_field = _field_name(char, "max_mp")
            max_hp = char.get(hp_field) if hp_field else "-"
            max_mp = char.get(mp_field) if mp_field else "-"
            stat = lambda n: char.get(_field_name(char, n)) if _field_name(char, n) else "-"
            _set_row(
                self.party_table, char.index,
                (
                    char.index, char.name or "(empty)", char.get("level"),
                    f"{char.get('cur_hp')}/{max_hp}", f"{char.get('cur_mp')}/{max_mp}",
                    stat("strength"), stat("speed"), stat("magic"), stat("spirit"),
                ),
            )

        items = sorted(slot.items(), key=lambda e: data.item_name(e[0]))
        self.items_table.setRowCount(len(items))
        for row, (item_id, count, _slot_idx) in enumerate(items):
            _set_row(self.items_table, row, (f"0x{item_id:02X}", data.item_name(item_id), count))

        cards = slot.cards()
        self.cards_table.setRowCount(len(cards))
        for row, rec in enumerate(cards):
            _set_row(
                self.cards_table, row,
                (
                    rec["index"], data.card_type_name(rec["type_id"]), rec["attack"],
                    "PMXA"[rec["attack_type"] % 4], rec["p_def"], rec["m_def"], f"0x{rec['arrows']:02X}",
                ),
            )
        wins, losses, draws = slot.card_record_stats()
        self.cards_status.setText(f"Tetra Master record: {wins}W - {losses}L - {draws}D")

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
        self.overview_label.setText("\n".join(lines))
        self.gil_edit.setText(str(slot.gil))

        self._refresh_char_editor()

    def _on_party_select(self) -> None:
        row = self.party_table.currentRow()
        if row < 0:
            return
        self._current_char_index = row
        self._refresh_char_editor()

    def _refresh_char_editor(self) -> None:
        if self.slot is None:
            return
        char = self.slot.character(self._current_char_index)
        self.editor_title.setText(f"{char.name or '(unnamed)'}  (row {char.index})")
        self._name_edit.setText(char.name)
        for key, _label in STAT_FIELDS:
            field = _field_name(char, key)
            self._stat_edits[key].setText(str(char.get(field)) if field else "")
        for key, _label in EQUIP_FIELDS:
            self._equip_edits[key].setText(data.item_name(char.get(key)) if char.has(key) else "")
        note = ""
        if char.fmt == "rr2016":
            note = "Equipment offsets for this format are experimental - see NOTICES.md."
        elif char.fmt == "memoria":
            note = "Strength/Speed/Magic/Spirit are inferred from Memoria's internal stat names - see NOTICES.md."
        self.editor_note.setText(note)

    # --------------------------------------------------------------- actions

    def _apply_char_edits(self) -> None:
        if not self.require_slot():
            return
        char = self.slot.character(self._current_char_index)
        try:
            char.name = self._name_edit.text()
            for key, _label in STAT_FIELDS:
                field = _field_name(char, key)
                if field is None:
                    continue
                value = int(self._stat_edits[key].text().strip())
                char.set(field, value)
            for key, _label in EQUIP_FIELDS:
                if not char.has(key):
                    continue
                token = self._equip_edits[key].text().strip()
                if not token:
                    continue
                item_id = data.resolve_item_id(token)
                char.set(key, item_id)
        except ValueError as exc:
            QMessageBox.critical(self, "Could not apply edits", str(exc))
            return
        self._commit(f"applied edits to {char.name or char.index}")

    def _max_selected_character(self) -> None:
        if not self.require_slot():
            return
        char = self.slot.character(self._current_char_index)
        char.max_out()
        self._commit(f"maxed {char.name or char.index}")

    def _max_all_characters(self) -> None:
        if not self.require_slot():
            return
        n = 0
        for char in self.slot.characters():
            if char.is_recruited:
                char.max_out()
                n += 1
        self._commit(f"maxed {n} character(s)")

    def _set_gil(self) -> None:
        if not self.require_slot():
            return
        try:
            amount = int(self.gil_edit.text().strip())
        except ValueError:
            QMessageBox.critical(self, "Invalid gil", f"Not a number: {self.gil_edit.text()!r}")
            return
        self.slot.gil = amount
        self._commit(f"gil set to {amount:,}")

    def _add_item(self) -> None:
        if not self.require_slot():
            return
        token = self.item_combo.currentText().strip()
        if not token:
            self.log_line("Enter an item/gear name or hex ID first.")
            return
        try:
            item_id = data.resolve_item_id(token)
            qty = int(self.item_qty_edit.text().strip())
        except ValueError as exc:
            QMessageBox.critical(self, "Could not add item", str(exc))
            return
        ok = self.slot.set_item(item_id, qty)
        self._commit(f"{'gave' if ok else 'FAILED to give (inventory full)'} {data.item_name(item_id)} x{qty}")

    def _give_all_items(self) -> None:
        if not self.require_slot():
            return
        try:
            qty = int(self.item_qty_edit.text().strip())
        except ValueError:
            qty = 99
        n = tool.give_all_items(self.slot, qty)
        self._commit(f"gave {n} items/gear pieces")

    def _commit(self, message: str) -> None:
        assert self.doc is not None and self.ref is not None and self.slot is not None
        self.doc.commit_slot(self.ref, self.slot)
        self.slot = self.doc.load_slot(self.ref)
        self.log_line(message)
        self.refresh_views()

    def _write_out(self) -> None:
        if self.doc is None:
            self.log_line("No file loaded.")
            return
        out_value = self.out_edit.text().strip()
        if out_value:
            out_path = Path(out_value)
        elif self.save_path is not None:
            out_path = self.save_path.with_name(self.save_path.stem + ".edited" + self.save_path.suffix)
        else:
            self.log_line("No output path and no loaded file path to derive one from.")
            return
        try:
            out_path.write_bytes(self.doc.to_bytes())
        except OSError as exc:
            self.log_line(f"Could not write {out_path}: {exc}")
            QMessageBox.critical(self, "Write failed", str(exc))
            return
        self.log_line(f"wrote: {out_path}")

    def _write_inplace(self) -> None:
        if self.doc is None or self.save_path is None:
            self.log_line("No file loaded.")
            return
        answer = QMessageBox.question(
            self, "Overwrite in place?",
            f"Overwrite {self.save_path} in place?\nA backup will be written to {self.save_path}.bak first.",
        )
        if answer != QMessageBox.Yes:
            return
        backup = self.save_path.with_suffix(self.save_path.suffix + ".bak")
        try:
            backup.write_bytes(self.save_path.read_bytes())
            self.save_path.write_bytes(self.doc.to_bytes())
        except OSError as exc:
            self.log_line(f"In-place write failed: {exc}")
            QMessageBox.critical(self, "Write failed", str(exc))
            return
        self.log_line(f"wrote in-place; backup: {backup}")


def main(argv: list[str] | None = None) -> int:
    argv = argv if argv is not None else sys.argv[1:]
    save_path = argv[0] if argv else None
    app = QApplication(sys.argv[:1])
    app.setApplicationName("FFIX Save Editor")
    window = FFIXSaveGUI(save_path)
    window.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
