# Architecture

The immutable catalog defines gameplay concepts, ranges, baseline values and conversion rules. Profile records store values separately from draft sessions, raw numeric input and validation errors.

`ProfileCompiler` validates and compiles a complete profile in deterministic order, including baseline values. UI output labels and generated commands share compiled results. Internal negative power consumption remains negative in commands and is presented as positive consumption to the player.

`ProfileWriter` and `AtomicFileWriter` handle complete writes. Installation and Vanilla maintenance remain separate from profile editing. INI editing parses exact SCS commands, preserves unrelated bindings, detects conflicts, backs up files and selects the effective input section based on existing non-SCS bindings.

The WPF layer projects this state into gameplay and workspace pages. Save profile acknowledges only the active profile after verifying written bytes. Save all first validates pending profiles and local assignments, then saves pending profiles and preferences; it never installs hotkeys. Partial disk failures preserve completed saves, retain unsaved work and prevent automatic close. The close dialog offers Save all and close, Discard changes and Cancel.

The CLI and regression executable consume the same core library. Tests use synthetic installations and do not constitute in-game validation of every gameplay setting.
