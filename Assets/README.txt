SimulaVit Project Notes
## Automated Tests (Unity Test Framework)

This project includes EditMode and PlayMode tests under:
- `Assets/Tests/EditMode`
- `Assets/Tests/PlayMode`

### Run tests in Unity Editor
1. Open **Window > General > Test Runner**.
2. Run **EditMode** tests for fast unit coverage.
3. Run **PlayMode** tests for scene/simulation integration coverage.

### Run tests in batch mode (CLI)
Example command:

```bash
"<UnityEditorPath>" \
  -batchmode -nographics -quit \
  -projectPath "$(pwd)" \
  -runTests -testPlatform editmode \
  -testResults "TestResults/EditMode.xml"
```

PlayMode:

```bash
"<UnityEditorPath>" \
  -batchmode -nographics -quit \
  -projectPath "$(pwd)" \
  -runTests -testPlatform playmode \
  -testResults "TestResults/PlayMode.xml"
```
