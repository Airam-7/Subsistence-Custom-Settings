# Validation: version 2.0.6

The 2.0.6 source passed **105 portable regression tests**, with zero build errors or warnings. Tests use synthetic installations. No private INI fixtures are required or distributed.

Ten new cases cover exact parent/plain and child/dotted pairs; valid dotted pairs as a byte-identical migration no-op; old parent-only, child-only and mixed installations; duplicate cleanup; mismatched pairs; explicit resolution of ambiguous keys; conflicts with unrelated dotted bindings; reversed sections, LF/CRLF, five encodings and missing final newlines; missing sections; and section-aware journals plus removal. Existing backup, concurrency, atomic-write and preservation checks remain in the suite.

The locally compiled 2.0.6 EXE passed WPF smoke scenarios for installation, startup/Verify migration, installed-key display, Save all, reopening and the close dialog. Its Hotkeys screenshot and generated synthetic INI were inspected. This source/documentation update does not upload that executable.

The UE3 behavior motivating the dotted child entries comes from empirical in-game testing reported by the user. These automated checks validate generated files and app behavior, not a new gameplay session or an independent security audit.

See [BUILD.md](../BUILD.md) to reproduce both the regression suite and the separate WPF smoke tests.
