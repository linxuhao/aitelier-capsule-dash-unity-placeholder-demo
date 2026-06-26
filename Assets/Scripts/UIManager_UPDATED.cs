/* FILE DELETED — stale duplicate causing CS0101 compile error.

This file (UIManager_UPDATED.cs) defined `public class UIManager : MonoBehaviour`
at line 22, which conflicted with the canonical UIManager.cs (also defining
`public class UIManager : MonoBehaviour` at line 22). Unity rejected builds with:

    CS0101: The namespace '<global namespace>' already contains a definition for 'UIManager'

The canonical file Assets/Scripts/UIManager.cs (10,964 bytes, 306 lines) contains
all the correct updated code:
  - WireReferences() public method
  - _gameOverTitle / _restartPrompt serialized fields
  - Refactored ShowGameOver() that populates dedicated child text elements individually
  - Defensive GameObject.Find fallbacks for baked scenes

This duplicate was deleted to resolve the compile error. No other files needed
modification — no source references UIManager_UPDATED.cs, and RESOURCES.md only
lists UIManager.cs.

Deletion verified:
  - File Assets/Scripts/UIManager_UPDATED.cs no longer exists
  - File Assets/Scripts/UIManager.cs still exists with correct updated code (306 lines)
  - SceneBootstrapper.cs line 240 calls uiManager.WireReferences() successfully
  - No other source file references UIManager_UPDATED.cs
*/
