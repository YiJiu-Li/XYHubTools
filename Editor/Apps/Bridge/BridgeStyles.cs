// ═══════════════════════════════════════════════════════════════
//  BridgeStyles — MCP Bridge 面板样式缓存
//
//  遵循框架既有约定（见 Hub/XY_HubStyles.cs）：
//    · 集中管理、延迟初始化
//    · 皮肤（light / dark）切换时自动重建
//    · 避免在 OnGUI 热路径里反复 new GUIStyle / new Texture2D
//
//  通用样式（日志、富文本、提示框等）直接复用 XYEditorStyles，
//  这里只放 Bridge 面板特有的卡片 / 胶囊 / 结果行样式。
// ═══════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class BridgeStyles
    {
        private static bool _inited;
        private static bool _lastWasDark;

        // ── 颜色 ──────────────────────────────────────────────────────────
        public static Color CardBg { get; private set; }
        public static Color CardBorder { get; private set; }
        public static Color Divider { get; private set; }
        public static Color MutedText { get; private set; }
        public static Color OkText { get; private set; }
        public static Color FailText { get; private set; }
        public static Color WarnText { get; private set; }
        public static Color StatusOn { get; private set; }
        public static Color StatusOff { get; private set; }

        // ── 样式 ──────────────────────────────────────────────────────────
        /// <summary>状态卡片容器（带边框 + 内边距）</summary>
        public static GUIStyle Card { get; private set; }

        /// <summary>日志滚动区容器</summary>
        public static GUIStyle LogBox { get; private set; }

        /// <summary>区块小标题</summary>
        public static GUIStyle SectionLabel { get; private set; }

        /// <summary>次要说明文字</summary>
        public static GUIStyle Muted { get; private set; }

        /// <summary>次要说明文字（右对齐）</summary>
        public static GUIStyle MutedRight { get; private set; }

        /// <summary>次要说明文字（自动换行）</summary>
        public static GUIStyle MutedWrap { get; private set; }

        /// <summary>管道名等偏等宽的展示文字</summary>
        public static GUIStyle Mono { get; private set; }

        public static GUIStyle StatusOnStyle { get; private set; }
        public static GUIStyle StatusOffStyle { get; private set; }

        /// <summary>区块折叠标题</summary>
        public static GUIStyle Foldout { get; private set; }

        /// <summary>筛选胶囊（未选中）</summary>
        public static GUIStyle Chip { get; private set; }

        /// <summary>筛选胶囊（选中）</summary>
        public static GUIStyle ChipActive { get; private set; }

        /// <summary>自检结果行背景：通过 / 失败 / 未运行</summary>
        public static GUIStyle RowOk { get; private set; }
        public static GUIStyle RowFail { get; private set; }
        public static GUIStyle RowIdle { get; private set; }

        /// <summary>结果行里的名称列</summary>
        public static GUIStyle RowName { get; private set; }

        /// <summary>✓ / ✗ / 破坏性 标记</summary>
        public static GUIStyle MarkPass { get; private set; }
        public static GUIStyle MarkFail { get; private set; }
        public static GUIStyle MarkWarn { get; private set; }

        /// <summary>分组标签：core / assets / scene / events</summary>
        public static GUIStyle GroupCore { get; private set; }
        public static GUIStyle GroupAssets { get; private set; }
        public static GUIStyle GroupScene { get; private set; }
        public static GUIStyle GroupEvents { get; private set; }

        /// <summary>行内小按钮（重跑 / 跑）</summary>
        public static GUIStyle TinyButton { get; private set; }

        /// <summary>常规按钮</summary>
        public static GUIStyle Button { get; private set; }

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
            // ── 颜色 ──
            CardBg = dark ? new Color(0.20f, 0.20f, 0.21f) : new Color(0.94f, 0.94f, 0.95f);
            CardBorder = dark ? new Color(0.28f, 0.28f, 0.30f) : new Color(0.74f, 0.74f, 0.77f);
            Divider = dark ? new Color(0.27f, 0.27f, 0.28f) : new Color(0.72f, 0.72f, 0.74f);
            MutedText = dark ? new Color(0.56f, 0.56f, 0.60f) : new Color(0.42f, 0.42f, 0.46f);
            OkText = dark ? new Color(0.42f, 0.90f, 0.52f) : new Color(0.10f, 0.46f, 0.17f);
            FailText = dark ? new Color(0.95f, 0.45f, 0.45f) : new Color(0.70f, 0.14f, 0.14f);
            WarnText = dark ? new Color(1.00f, 0.74f, 0.32f) : new Color(0.62f, 0.38f, 0.02f);
            StatusOn = dark ? new Color(0.35f, 0.90f, 0.45f) : new Color(0.10f, 0.55f, 0.18f);
            StatusOff = dark ? new Color(0.88f, 0.70f, 0.26f) : new Color(0.60f, 0.45f, 0.02f);

            Color rowOkBg = dark
                ? new Color(0.14f, 0.26f, 0.17f, 0.75f)
                : new Color(0.84f, 0.94f, 0.86f, 0.95f);
            Color rowFailBg = dark
                ? new Color(0.30f, 0.15f, 0.15f, 0.75f)
                : new Color(0.97f, 0.86f, 0.86f, 0.95f);
            Color rowIdleBg = dark
                ? new Color(0.17f, 0.17f, 0.18f, 0.60f)
                : new Color(0.89f, 0.89f, 0.90f, 0.80f);

            // ── 容器 ──
            Card = new GUIStyle
            {
                normal = { background = MakeTex(CardBg) },
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 0, 0),
            };

            LogBox = new GUIStyle
            {
                normal = { background = MakeTex(CardBg) },
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 0, 0),
            };

            // ── 文字 ──
            SectionLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = MutedText },
            };

            Muted = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = MutedText },
            };

            MutedRight = new GUIStyle(Muted) { alignment = TextAnchor.MiddleRight };

            MutedWrap = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = MutedText },
            };

            Mono = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = dark ? new Color(0.70f, 0.70f, 0.74f) : new Color(0.35f, 0.35f, 0.40f) },
            };

            StatusOnStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = StatusOn },
            };

            StatusOffStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = StatusOff },
            };

            Foldout = new GUIStyle(EditorStyles.foldout)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = dark ? new Color(0.86f, 0.86f, 0.88f) : new Color(0.14f, 0.14f, 0.16f) },
                onNormal = { textColor = dark ? new Color(0.86f, 0.86f, 0.88f) : new Color(0.14f, 0.14f, 0.16f) },
                focused = { textColor = dark ? new Color(0.86f, 0.86f, 0.88f) : new Color(0.14f, 0.14f, 0.16f) },
                onFocused = { textColor = dark ? new Color(0.86f, 0.86f, 0.88f) : new Color(0.14f, 0.14f, 0.16f) },
            };

            // ── 胶囊筛选 ──
            Chip = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 2, 2),
                margin = new RectOffset(1, 1, 0, 0),
                normal =
                {
                    textColor = dark ? new Color(0.70f, 0.70f, 0.74f) : new Color(0.34f, 0.34f, 0.38f),
                },
            };

            ChipActive = new GUIStyle(Chip)
            {
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = Color.white,
                    background = MakeTex(
                        dark ? new Color(0.22f, 0.46f, 0.84f) : new Color(0.24f, 0.48f, 0.86f)
                    ),
                },
                hover =
                {
                    textColor = Color.white,
                    background = MakeTex(
                        dark ? new Color(0.26f, 0.52f, 0.90f) : new Color(0.28f, 0.54f, 0.92f)
                    ),
                },
            };

            // ── 结果行 ──
            RowOk = RowStyle(rowOkBg);
            RowFail = RowStyle(rowFailBg);
            RowIdle = RowStyle(rowIdleBg);

            RowName = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = dark ? new Color(0.88f, 0.88f, 0.90f) : new Color(0.12f, 0.12f, 0.14f) },
            };

            MarkPass = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = OkText },
            };

            MarkFail = new GUIStyle(MarkPass) { normal = { textColor = FailText } };
            MarkWarn = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = WarnText },
            };

            // ── 分组标签 ──
            GroupCore = GroupStyle(dark ? new Color(0.48f, 0.70f, 0.95f) : new Color(0.16f, 0.34f, 0.62f));
            GroupAssets = GroupStyle(dark ? new Color(0.96f, 0.74f, 0.40f) : new Color(0.55f, 0.36f, 0.02f));
            GroupScene = GroupStyle(dark ? new Color(0.60f, 0.88f, 0.60f) : new Color(0.14f, 0.44f, 0.18f));
            GroupEvents = GroupStyle(dark ? new Color(0.85f, 0.66f, 0.95f) : new Color(0.42f, 0.20f, 0.58f));

            // ── 按钮 ──
            TinyButton = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10,
                padding = new RectOffset(4, 4, 1, 1),
                margin = new RectOffset(2, 2, 0, 0),
            };

            Button = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                padding = new RectOffset(8, 8, 3, 3),
                margin = new RectOffset(0, 4, 0, 0),
            };
        }

        private static GUIStyle RowStyle(Color bg)
        {
            return new GUIStyle
            {
                normal = { background = MakeTex(bg) },
                padding = new RectOffset(6, 6, 2, 2),
                margin = new RectOffset(0, 0, 1, 1),
            };
        }

        private static GUIStyle GroupStyle(Color c)
        {
            return new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = c },
            };
        }

        /// <summary>按分组名取对应颜色的标签样式，未知分组回退到 Muted。</summary>
        public static GUIStyle GroupOf(string group)
        {
            switch (group)
            {
                case "core":
                    return GroupCore;
                case "assets":
                    return GroupAssets;
                case "scene":
                    return GroupScene;
                case "events":
                case "pipeline":
                    return GroupEvents;
                default:
                    return Muted;
            }
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.ARGB32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
