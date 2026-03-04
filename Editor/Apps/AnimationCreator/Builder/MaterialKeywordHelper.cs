using UnityEditor;
using UnityEngine;

namespace YZJ.AnimationCreator
{
    /// <summary>
    /// 材质 Keyword 处理工具类
    /// </summary>
    public static class MaterialKeywordHelper
    {
        /// <summary>
        /// keyword 名对应 [KeywordEnum(X, _X, Y, _Y)]_AXIS
        /// </summary>
        private static readonly string[] AXIS_KEYWORDS =
        {
            "_AXIS_X",
            "_AXIS__X",
            "_AXIS_Y",
            "_AXIS__Y",
        };

        /// <summary>
        /// 直接修改 GameObject 及其所有子物体材质的 _AXIS keyword 和 float 默认值。
        /// 支持 MeshRenderer、SkinnedMeshRenderer、SpriteRenderer。
        /// KeywordEnum 属性无法通过 AnimationClip 曲线控制（动画只改 float，不切 keyword），
        /// 因此在 Editor 创建动画时直接把材质的 keyword 设好。
        /// </summary>
        /// <param name="go">目标 GameObject</param>
        /// <param name="direction">轴向方向</param>
        /// <param name="axisProperty">Shader 中的轴向属性名</param>
        /// <returns>修改的材质数量</returns>
        public static int ApplyAxisKeywordToMaterials(
            GameObject go,
            AxisDirection direction,
            string axisProperty
        )
        {
            if (go == null)
                return 0;

            int targetIndex = (int)direction;
            int modifiedCount = 0;

            // 获取所有支持的 Renderer 类型
            var renderers = go.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                // 跳过不支持的渲染器类型（如 ParticleSystemRenderer 等）
                if (renderer == null)
                    continue;
                if (
                    !(
                        renderer is MeshRenderer
                        || renderer is SkinnedMeshRenderer
                        || renderer is SpriteRenderer
                    )
                )
                    continue;

                // 使用 sharedMaterials 直接修改材质资产
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null)
                        continue;

                    // 切换 keyword
                    for (int i = 0; i < AXIS_KEYWORDS.Length; i++)
                    {
                        if (i == targetIndex)
                            mat.EnableKeyword(AXIS_KEYWORDS[i]);
                        else
                            mat.DisableKeyword(AXIS_KEYWORDS[i]);
                    }

                    // 同步 float 值
                    if (mat.HasProperty(axisProperty))
                        mat.SetFloat(axisProperty, targetIndex);

                    EditorUtility.SetDirty(mat);
                    modifiedCount++;
                }
            }

            if (modifiedCount > 0)
            {
                Debug.Log(
                    $"<color=cyan>[MaterialKeywordHelper]</color> "
                        + $"已设置 {modifiedCount} 个材质的 Axis keyword → {direction} ({AXIS_KEYWORDS[targetIndex]})"
                );
            }

            return modifiedCount;
        }

        /// <summary>
        /// 获取轴向方向对应的 Keyword 名称
        /// </summary>
        public static string GetAxisKeyword(AxisDirection direction)
        {
            int index = (int)direction;
            return index >= 0 && index < AXIS_KEYWORDS.Length
                ? AXIS_KEYWORDS[index]
                : AXIS_KEYWORDS[0];
        }
    }
}
