// XY_EditorHub.Persistence.cs  —— 收藏 & 最近使用持久化

using System.Linq;
using UnityEditor;

namespace Framework.XYEditor
{
    public partial class XY_EditorHub
    {
        // -- 收藏 ------------------------------------------------------------

        private void LoadFavorites()
        {
            _favorites.Clear();
            foreach (var f in EditorPrefs.GetString(PREF_FAVS, "").Split('|'))
                if (!string.IsNullOrEmpty(f))
                    _favorites.Add(f);
        }

        private void SaveFavorites() =>
            EditorPrefs.SetString(PREF_FAVS, string.Join("|", _favorites));

        private bool IsFavorite(IXYEditorTool tool) => _favorites.Contains(tool.GetType().FullName);

        private void ToggleFavorite(IXYEditorTool tool)
        {
            string key = tool.GetType().FullName;
            if (_favorites.Contains(key))
                _favorites.Remove(key);
            else
                _favorites.Add(key);
            SaveFavorites();
            Repaint();
        }

        // -- 最近使用 --------------------------------------------------------

        private void LoadRecents()
        {
            _recents.Clear();
            _recents.AddRange(
                EditorPrefs
                    .GetString(PREF_RECENTS, "")
                    .Split('|')
                    .Where(s => !string.IsNullOrEmpty(s))
            );
        }

        private void SaveRecents() =>
            EditorPrefs.SetString(PREF_RECENTS, string.Join("|", _recents));

        private void PushRecent(IXYEditorTool tool)
        {
            string key = tool.GetType().FullName;
            _recents.Remove(key);
            _recents.Insert(0, key);
            if (_recents.Count > RECENT_MAX)
                _recents.RemoveAt(_recents.Count - 1);
        }
    }
}
