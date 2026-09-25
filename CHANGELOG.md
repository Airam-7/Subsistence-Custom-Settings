# Changelog

## 2.0.6 — 2026-09-25

Source and documentation update; executable publication is handled separately by the maintainer.

- Install the same five hotkeys in both sections: `Bindings=` in `Engine.PlayerInput`, `.Bindings=` in `ColdGame.ColdPlayerInput`.
- Preserve inherited controls by never generating plain SCS child entries. Preserve existing unrelated child bindings, flags, formatting and encoding.
- Remove previous exact SCS entries from both sections before installation; removal handles plain, dotted and legacy plus-prefixed forms.
- Migrate complete, unambiguous older assignments with backups and concurrent-change checks. Valid dotted pairs do not trigger repair. Conflicting/incomplete assignments require explicit selection; missing or duplicate input sections block installation.
- Mark a hotkey installed only when both copies match and use the correct operators.
- Record all ten added lines with their sections in operation journals.
- Add ten regression cases (105 portable tests total) and update the WPF smoke checks.

Gameplay catalog 2.0.1, the 20 settings, 154 profile commands, Save all semantics and MIT license remain unchanged.

## 2.0.5

Earlier public release: Save all for pending profiles and local preferences, three-option close dialog, readback verification and separate hotkey installation. Its release tag and executable are preserved.
