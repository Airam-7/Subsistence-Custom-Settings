# Validation: version 2.0.5

The clean public source tree was rebuilt on Windows with SDK 9.0.318 before upload: **95 portable tests passed, 0 failed**. The two optional private fixtures were not needed for this run.

Before source publication, the release was built without errors or warnings and passed 97 regression cases, including two optional local INI fixtures. Those personal fixtures and their paths are not included in the public repository. A normal checkout runs the portable cases without requiring them.

The published executable also passed synthetic WPF smoke scenarios covering isolated Save profile, Save all across profiles/preferences, unchanged INI after Save all, disabled Save all after saving, clean reopen, validation before writes, partial write and metadata failures, retries, and the three real close-dialog actions.

All test writes used synthetic installations. No new gameplay session or independent security audit was performed for this release. Re-run the checks using BUILD.md; passing tests do not prove every engine behavior or rule out all defects.
