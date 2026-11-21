"""Slices the race concept sheets (6x4 grids of portraits with captions) into one PNG per race id.

Usage: python3 tools/slice_portraits.py [output_dir]
"""
import os
import sys

from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONCEPTS = os.path.join(REPO, "art", "concepts")
DEFAULT_OUTPUT = os.path.join(REPO, "client", "Assets", "Game", "Resources", "Portraits")

COLUMNS = 6
# Measured on the 512x512 concept sheets: framed portrait origin, size and grid pitch in pixels.
FIRST_FRAME_X = 14
FIRST_FRAME_Y = 12
FRAME_SIZE = 74
PITCH_X = 82
PITCH_Y = 128
OUTPUT_SIZE = 128

SHEETS = {
    "races_sheet_1.png": [
        "warforged", "leonin", "locathah", "loxodon", "lizardfolk", "minotaur",
        "half_orc", "halfling", "half_elf", "satyr", "owlin", "tabaxi",
        "thri_kreen", "triton", "firbolg", "fairy", "hadozee", "hobgoblin",
        "shifter", "elf", "yuan_ti", "high_elf", "troll", "pandaren",
    ],
    "races_sheet_2.png": [
        "aarakocra", "aasimar", "autognome", "astral_elf", "bugbear", "vedalken",
        "verdan", "simic_hybrid", "githyanki", "giff", "gnome", "goblin",
        "goliath", "grung", "dwarf", "genasi", "dragonborn", "harengon",
        "kalashtar", "kender", "kenku", "centaur", "kobold", "races_icon",
    ],
}


def slice_sheet(path, ids, output_dir):
    sheet = Image.open(path).convert("RGB")
    for index, race_id in enumerate(ids):
        column, row = index % COLUMNS, index // COLUMNS
        left = FIRST_FRAME_X + column * PITCH_X
        top = FIRST_FRAME_Y + row * PITCH_Y
        box = (left, top, left + FRAME_SIZE, top + FRAME_SIZE)
        portrait = sheet.crop(box).resize((OUTPUT_SIZE, OUTPUT_SIZE), Image.NEAREST)
        portrait.save(os.path.join(output_dir, race_id + ".png"))


def main():
    output_dir = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUTPUT
    os.makedirs(output_dir, exist_ok=True)
    for file_name, ids in SHEETS.items():
        slice_sheet(os.path.join(CONCEPTS, file_name), ids, output_dir)
    print("Portraits written to", output_dir)


if __name__ == "__main__":
    main()
