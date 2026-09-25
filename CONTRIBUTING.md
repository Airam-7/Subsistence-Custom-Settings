# Contributing

Thanks for helping improve Subsistence Custom Settings.

## Ways to contribute

- Report reproducible bugs or regressions.
- Share gameplay-validation results for existing supported settings.
- Improve documentation or translations.
- Submit focused pull requests for fixes and improvements.

## Before opening a pull request

For larger behavioral or architectural changes, please open an issue first so the approach can be discussed before implementation.

Keep pull requests focused on one change where practical. Include a short explanation of what changed, why it changed, and how it was tested.

## Testing

Run the repository checks described in [BUILD.md](BUILD.md) before submitting code changes.

Do not run tests against a real Subsistence installation. The test suite is designed to use synthetic installations and temporary files.

Gameplay behavior that cannot be demonstrated by the automated test suite should be identified explicitly as requiring in-game validation.

## Bug reports

Please include:

- SCS version.
- Windows version.
- Clear reproduction steps.
- Expected and observed behavior.
- Relevant error text, if any.

Do not post credentials, private INI files, or personal filesystem paths unless you have removed identifying information.

## License

By contributing code or documentation to this repository, you agree that your contribution may be distributed under the project's [MIT License](LICENSE.md).
