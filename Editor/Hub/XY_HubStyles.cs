using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    // =======================================================================
    //  样式缓存（参考 Odin 的 SirenixGUIStyles 理念：集中管理、延迟初始化）
    //  避免在热路径 OnGUI 中反复 new GUIStyle / new Texture2D
    // =======================================================================
    internal static class XY_HubStyles
    {
        private static bool _inited;
        private static bool _lastWasDark;

        // -- GUIStyle --------------------------------------------------------
        public static GUIStyle Toolbar { get; private set; }
        public static GUIStyle Title { get; private set; }
        public static GUIStyle CategoryHeader { get; private set; }
        public static GUIStyle ToolItem { get; private set; }
        public static GUIStyle ToolItemSelected { get; private set; }
        public static GUIStyle ContentBg { get; private set; }
        public static GUIStyle NoTool { get; private set; }
        public static GUIStyle Search { get; private set; }
        public static GUIStyle StatusBar { get; private set; }
        public static GUIStyle ToolTitle { get; private set; }
        public static GUIStyle ToolDesc { get; private set; }
        public static GUIStyle SectionLabel { get; private set; }

        // 缓存列表项文字样式（避免 DrawToolItem 每帧 new GUIStyle）
        public static GUIStyle ToolItemNameNormal { get; private set; }
        public static GUIStyle ToolItemNameSelected { get; private set; }
        public static GUIStyle StarLabel { get; private set; }

        // -- Color -----------------------------------------------------------
        public static Color ListBg { get; private set; }
        public static Color SplitterColor { get; private set; }
        public static Color AccentBar { get; private set; }
        public static Color SeparatorLine { get; private set; }
        public static Color StatusBg { get; private set; }
        public static Color ContentHeaderBg { get; private set; }

        /// <summary>在 OnGUI 开头调用，皮肤切换时自动重建。</summary>
        public static void EnsureInited()
        {
            bool dark = EditorGUIUtility.isProSkin;
            if (_inited && dark == _lastWasDark)
                return;
            _inited = true;
            _lastWasDark = dark;
            Build(dark);
        }

        private static void Build(bool dark)
        {
            ListBg = dark ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.80f, 0.80f, 0.80f);
            SplitterColor = dark ? new Color(0.10f, 0.10f, 0.10f) : new Color(0.55f, 0.55f, 0.55f);
            AccentBar = dark ? new Color(0.25f, 0.55f, 1.00f) : new Color(0.15f, 0.45f, 0.90f);
            SeparatorLine = dark ? new Color(0.11f, 0.11f, 0.11f) : new Color(0.58f, 0.58f, 0.58f);
            StatusBg = dark ? new Color(0.14f, 0.14f, 0.14f) : new Color(0.73f, 0.73f, 0.73f);
            ContentHeaderBg = dark
                ? new Color(0.16f, 0.16f, 0.16f)
                : new Color(0.75f, 0.78f, 0.85f);

            Toolbar = new GUIStyle
            {
                normal =
                {
                    background = MakeTex(
                        dark ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.83f, 0.83f, 0.83f)
                    ),
                },
                padding = new RectOffset(12, 8, 0, 0),
            };

            Title = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                normal =
                {
                    textColor = dark
                        ? new Color(0.90f, 0.90f, 0.90f)
                        : new Color(0.10f, 0.10f, 0.10f),
                },
            };

            CategoryHeader = new GUIStyle(EditorStyles.foldout)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(20, 4, 4, 4),
                normal =
                {
                    textColor = dark
                        ? new Color(0.60f, 0.75f, 0.95f)
                        : new Color(0.05f, 0.30f, 0.62f),
                },
                onNormal =
                {
                    textColor = dark
                        ? new Color(0.60f, 0.75f, 0.95f)
                        : new Color(0.05f, 0.30f, 0.62f),
                },
            };

            ToolItem = new GUIStyle
            {
                normal =
                {
                    background = null,
                    textColor = dark
                        ? new Color(0.84f, 0.84f, 0.84f)
                        : new Color(0.10f, 0.10f, 0.10f),
                },
                hover =
                {
                    background = MakeTex(
                        dark ? new Color(0.26f, 0.26f, 0.26f) : new Color(0.76f, 0.82f, 0.92f)
                    ),
                    textColor = dark
                        ? new Color(0.95f, 0.95f, 0.95f)
                        : new Color(0.05f, 0.05f, 0.05f),
                },
                padding = new RectOffset(8, 8, 4, 4),
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
            };

            ToolItemSelected = new GUIStyle(ToolItem)
            {
                normal =
                {
                    background = MakeTex(
                        dark ? new Color(0.20f, 0.44f, 0.78f) : new Color(0.22f, 0.46f, 0.84f)
                    ),
                    textColor = Color.white,
                },
                fontStyle = FontStyle.Bold,
            };

            ContentBg = new GUIStyle
            {
                normal =
                {
                    background = MakeTex(
                        dark ? new Color(0.21f, 0.21f, 0.21f) : new Color(0.90f, 0.90f, 0.90f)
                    ),
                },
            };

            NoTool = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal =
                {
                    textColor = dark
                        ? new Color(0.45f, 0.45f, 0.45f)
                        : new Color(0.52f, 0.52f, 0.52f),
                },
            };

            Search = new GUIStyle("SearchTextField");

            StatusBar = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 10, 0, 0),
                normal =
                {
                    textColor = dark
                        ? new Color(0.45f, 0.45f, 0.45f)
                        : new Color(0.50f, 0.50f, 0.50f),
                },
            };

            ToolTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = dark ? Color.white : new Color(0.08f, 0.08f, 0.08f) },
            };

            ToolDesc = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                normal =
                {
                    textColor = dark
                        ? new Color(0.52f, 0.52f, 0.52f)
                        : new Color(0.38f, 0.38f, 0.38f),
                },
            };

            SectionLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 4, 0, 0),
                normal =
                {
                    textColor = dark
                        ? new Color(0.45f, 0.45f, 0.45f)
                        : new Color(0.50f, 0.50f, 0.50f),
                },
            };

            // 列表项名称样式（缓存，避免 DrawToolItem 每帧分配）
            ToolItemNameNormal = new GUIStyle(ToolItem) { fontStyle = FontStyle.Normal };
            ToolItemNameSelected = new GUIStyle(ToolItemSelected) { fontStyle = FontStyle.Bold };

            // ★ 收藏星号标签
            StarLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.78f, 0.1f) },
            };
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
