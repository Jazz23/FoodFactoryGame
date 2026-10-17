# Rasterizes the game UI's stroke icons (from the "Main Menu" and "Food Factory UI" designs) to PNGs under
# assets/ui/game/icons, one file per icon and colour, because superui cannot draw inline SVG yet. Needs ImageMagick
# (`magick`) with its librsvg delegate. Run from the bevy folder: python tools/ui_icons.py
import os
import subprocess
import tempfile

OUT = os.path.join("assets", "ui", "game", "icons")

# name: (svg body, stroke width, filled, viewBox width, viewBox height)
ICONS = {
    "factory": ('<path d="M3 21V10l5 3V10l5 3V6l8 4v11z"/><path d="M7 17h2"/><path d="M12 17h2"/><path d="M17 17h1"/>', 1.8, False),
    "play": ('<path d="M7 4.5v15l13-7.5z"/>', 0, True),
    "home": ('<path d="M3 10l9-6 9 6v10H3z"/><path d="M9 20v-6h6v6"/>', 1.8, False),
    "server": ('<rect x="3" y="4" width="18" height="7" rx="2"/><rect x="3" y="13" width="18" height="7" rx="2"/><path d="M7 7.5h.01"/><path d="M7 16.5h.01"/>', 1.8, False),
    "sliders": ('<path d="M4 6h10"/><path d="M18 6h2"/><circle cx="16" cy="6" r="2"/><path d="M4 12h4"/><path d="M12 12h8"/><circle cx="10" cy="12" r="2"/><path d="M4 18h12"/><circle cx="18" cy="18" r="2"/>', 1.8, False),
    "chevron-right": ('<path d="M9 6l6 6-6 6"/>', 2, False),
    "chevron-left": ('<path d="M15 6l-6 6 6 6"/>', 2, False),
    "chevron-down": ('<path d="M6 9l6 6 6-6"/>', 2, False),
    "quit": ('<path d="M14 4h5v16h-5"/><path d="M10 8l-4 4 4 4"/><path d="M6 12h10"/>', 1.8, False),
    "plus": ('<path d="M12 5v14"/><path d="M5 12h14"/>', 2, False),
    "reset": ('<path d="M4 12a8 8 0 1 0 2.4-5.7"/><path d="M4 4v4h4"/>', 2, False),
    "warning": ('<path d="M12 3l10 18H2z"/><path d="M12 10v5"/><path d="M12 18v.01"/>', 2, False),
    "user": ('<circle cx="12" cy="8" r="4"/><path d="M4 21c0-4 4-6 8-6s8 2 8 6"/>', 1.8, False),
    "users": ('<circle cx="9" cy="8" r="3.5"/><path d="M2 20c0-3.5 3-5.5 7-5.5s7 2 7 5.5"/><circle cx="17" cy="7" r="2.5"/><path d="M17 12.5c3 0 5 1.6 5 4.5"/>', 1.8, False),
    "check": ('<path d="M5 12l5 5 9-10"/>', 2.4, False),
    "clock": ('<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>', 2, False),
    "close": ('<path d="M6 6l12 12M18 6L6 18"/>', 2.2, False),
    "person": ('<circle cx="12" cy="8" r="4"/><path d="M4 21c1-4 4-6 8-6s7 2 8 6"/>', 1.8, False),
    "repeat": ('<path d="M17 2l4 4-4 4"/><path d="M3 11V9a3 3 0 013-3h15"/><path d="M7 22l-4-4 4-4"/><path d="M21 13v2a3 3 0 01-3 3H3"/>', 2, False),
    "target": ('<circle cx="12" cy="12" r="8"/><path d="M12 2v4M12 18v4M2 12h4M18 12h4"/>', 2.4, False),
    "arrow": ('<path d="M2 10h34M28 3l8 7-8 7"/>', 2, False, 40, 20),
    "arrow-short": ('<path d="M2 8h22M18 3l6 5-6 5"/>', 2, False, 28, 16),
    "star": ('<path d="M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z"/>', 1.8, False),
    "map": ('<path d="M9 4L3 6v14l6-2 6 2 6-2V4l-6 2z"/><path d="M9 4v14"/><path d="M15 6v14"/>', 1.8, False),
    "list": ('<path d="M8 6h13"/><path d="M8 12h13"/><path d="M8 18h13"/><path d="M3 6h.01"/><path d="M3 12h.01"/><path d="M3 18h.01"/>', 2, False),
    "truck": ('<path d="M2 6h12v10H2z"/><path d="M14 9h4l4 4v3h-8z"/><circle cx="6" cy="18" r="2"/><circle cx="18" cy="18" r="2"/>', 1.8, False),
    "leaf": ('<path d="M5 19c0-9 6-14 15-14 0 9-5 15-14 15"/><path d="M5 19l7-7"/>', 1.8, False),
    "book": ('<path d="M4 4h6a3 3 0 013 3v13a2 2 0 00-2-2H4z"/><path d="M20 4h-6a3 3 0 00-3 3v13a2 2 0 012-2h7z"/>', 1.8, False),
    "battery": ('<rect x="2" y="7" width="18" height="10" rx="2"/><path d="M22 11v2"/><path d="M5 10v4M8 10v4M11 10v4M14 10v4"/>', 1.8, False),
    "signal": ('<path d="M4 20v-3"/><path d="M9 20v-7"/><path d="M14 20v-11"/><path d="M19 20V5"/>', 2.4, False),
}

# Each icon in the colours the designs draw it in.
VARIANTS = {
    "factory": ["F2A33A"], "play": ["F2A33A", "16171A"], "home": ["A8A49B", "CFCBC3", "ECE8E1"], "server": ["A8A49B"],
    "sliders": ["A8A49B"], "chevron-right": ["6A675F"], "chevron-left": ["ECE8E1"], "chevron-down": ["A8A49B"], "quit": ["A8A49B"],
    "plus": ["ECE8E1", "CFCBC3"], "reset": ["ECE8E1"], "warning": ["FFC170", "F2A33A"], "user": ["ECE8E1"], "users": ["F2A33A"],
    "check": ["F2A33A", "16171A"], "clock": ["A8A49B"], "close": ["ECE8E1", "A8A49B", "FF8A78"], "person": ["ECE8E1"], "repeat": ["A8A49B"],
    "target": ["FF8A78"], "arrow": ["6A675F"], "arrow-short": ["6A675F"],
    "star": ["FFC170", "FF8A78", "A8A49B"], "map": ["A8A49B", "F2A33A"], "list": ["A8A49B", "F2A33A"], "truck": ["A8A49B", "F2A33A"],
    "leaf": ["A8A49B", "F2A33A"], "book": ["A8A49B", "F2A33A"], "battery": ["ECE8E1"], "signal": ["ECE8E1"],
    "users": ["F2A33A", "A8A49B"], "home": ["A8A49B", "CFCBC3", "ECE8E1", "F2A33A"],
}

SCALE = 4


def main():
    os.makedirs(OUT, exist_ok=True)
    with tempfile.TemporaryDirectory() as scratch:
        for name, spec in ICONS.items():
            body, stroke, filled = spec[:3]
            width, height = (spec[3], spec[4]) if len(spec) > 3 else (24, 24)
            for colour in VARIANTS[name]:
                paint = f'fill="#{colour}" stroke="none"' if filled else f'fill="none" stroke="#{colour}" stroke-width="{stroke}" stroke-linecap="round" stroke-linejoin="round"'
                svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="{width * SCALE}" height="{height * SCALE}" viewBox="0 0 {width} {height}" {paint}>{body}</svg>'
                source = os.path.join(scratch, f"{name}.svg")
                with open(source, "w") as f:
                    f.write(svg)
                subprocess.run(["magick", "-background", "none", source, os.path.join(OUT, f"{name}-{colour.lower()}.png")], check=True)
    print(f"wrote icons to {OUT}")


if __name__ == "__main__":
    main()
