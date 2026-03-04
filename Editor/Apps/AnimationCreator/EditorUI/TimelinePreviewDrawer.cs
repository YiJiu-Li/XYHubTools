using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YZJ.AnimationCreator;

namespace YZJ.AnimationCreator.Editor
{
    /// <summary>
    /// 时间轴预览绘制器
    /// </summary>
    public static class TimelinePreviewDrawer
    {
        private const float TRACK_HEIGHT = 16f;
        private const float TRACK_SPACING = 2f;

        private static GUIStyle _sectionHeaderStyle;
        private static GUIStyle SectionHeaderStyle =>
            _sectionHeaderStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                richText = true,
            };

        /// <summary>绘制时间轴预览</summary>
        public static void DrawTimelinePreview(MaterialTransparencyAnimationTemplate tpl)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("⏱ 动画预览", SectionHeaderStyle);

            // 出现动画
            var appearTracks = TrackDataCollector.CollectPhaseTracks(tpl, tpl.appearConfig);
            if (appearTracks.Count > 0)
            {
                DrawPhaseTimeline(
                    "▶ 出现",
                    appearTracks,
                    tpl.appearConfig.duration,
                    new Color(0.2f, 0.4f, 0.2f)
                );
            }

            EditorGUILayout.Space(4);

            // 消失动画
            var disappearTracks = TrackDataCollector.CollectPhaseTracks(tpl, tpl.disappearConfig);
            if (disappearTracks.Count > 0)
            {
                DrawPhaseTimeline(
                    "◀ 消失",
                    disappearTracks,
                    tpl.disappearConfig.duration,
                    new Color(0.4f, 0.2f, 0.2f)
                );
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawPhaseTimeline(
            string title,
            List<TrackInfo> tracks,
            float duration,
            Color bgTint
        )
        {
            if (tracks.Count == 0)
                return;

            // 标题
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
            };
            EditorGUILayout.LabelField(title, titleStyle);

            float totalHeight = tracks.Count * (TRACK_HEIGHT + TRACK_SPACING) + 20;
            var fullRect = EditorGUILayout.GetControlRect(false, totalHeight);

            // 背景
            EditorGUI.DrawRect(fullRect, new Color(bgTint.r, bgTint.g, bgTint.b, 0.3f));

            float maxDuration = Mathf.Max(duration, 0.01f);

            const float LABEL_WIDTH = 160f;
            float tlLeft = fullRect.x + LABEL_WIDTH;
            float tlRight = fullRect.xMax - 8;
            float tlWidth = tlRight - tlLeft;

            // 刻度
            DrawTimeRuler(fullRect, tlLeft, tlWidth, maxDuration);

            // 轨道样式
            float y = fullRect.y + 16;
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.MiddleLeft,
            };
            var valStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
            };
            var typeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.6f, 0.8f, 1f) },
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
            };

            foreach (var t in tracks)
            {
                // 属性名
                EditorGUI.LabelField(
                    new Rect(fullRect.x + 4, y, 120, TRACK_HEIGHT),
                    t.property,
                    labelStyle
                );
                // 类型标记
                EditorGUI.LabelField(
                    new Rect(fullRect.x + 124, y, 32, TRACK_HEIGHT),
                    $"[{t.typeTag}]",
                    typeStyle
                );

                // 条
                float barX = tlLeft + (t.startTime / maxDuration) * tlWidth;
                float barW = Mathf.Max((t.duration / maxDuration) * tlWidth, 3);
                Color barColor = t.isConstant
                    ? new Color(t.color.r * 0.6f, t.color.g * 0.6f, t.color.b * 0.6f)
                    : t.color;
                EditorGUI.DrawRect(new Rect(barX, y + 1, barW, TRACK_HEIGHT - 2), barColor);

                // 值
                string val = t.isAnimated ? $"{t.fromVal:F2} → {t.toVal:F2}" : $"= {t.fromVal:F2}";
                EditorGUI.LabelField(new Rect(barX + 3, y, barW - 4, TRACK_HEIGHT), val, valStyle);

                y += TRACK_HEIGHT + TRACK_SPACING;
            }
        }

        private static void DrawTimeRuler(
            Rect fullRect,
            float tlLeft,
            float tlWidth,
            float maxDuration
        )
        {
            int ticks = Mathf.Clamp(Mathf.FloorToInt(maxDuration / 0.25f), 2, 20);
            float step = maxDuration / ticks;
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
            };

            for (int i = 0; i <= ticks; i++)
            {
                float t = i * step;
                float x = tlLeft + (t / maxDuration) * tlWidth;
                EditorGUI.DrawRect(
                    new Rect(x, fullRect.y + 14, 1, fullRect.height - 14),
                    new Color(0.3f, 0.3f, 0.3f, 0.4f)
                );
                EditorGUI.LabelField(new Rect(x - 15, fullRect.y, 30, 14), $"{t:F2}s", style);
            }
        }
    }
}
