# UXR 手势参考

本参考来自一组 Rokid UXR 样例的人工提炼，只保留可迁移的行为与接入注意点。目标项目的 SDK 版本、namespace、模块资源标识、Prefab 层级和生命周期拥有更高优先级；不要把样例源码或 `.meta` 原样复制进点位。

## 能力目录

- `UPalm`：分别维护左右手 `GestureBean`，把 Palm 手势或掌心朝向规整为 `status/handType/position/rotation`。掌心位置可由 `WRIST` 与 `MIDDLE_FINGER_MCP` 插值，并在骨骼数组缺失或手长异常时回退到 `bean.position`。
- `UGrip`：握拳状态与右手位姿的轻量输出。若目标点位需要双手，不能沿用只保存右手的版本。
- `UGesWrist`/`UWatch`：输出腕部骨骼位姿。样例会在收到一只手时清空另一只手，只适用于明确的单活动手语义。
- `UScale`：双手 Pinch 时记录起始指尖距离，持续输出距离比例，并给出 Start/Moving/End 状态。需要处理初始距离过小、追踪丢失、重新开始以及每帧日志开销。
- `UButton`：用食指尖代理的 Trigger Collider 触发 `UBtnItem` 的 enter/exit 回调。按钮资源提供触发区，手代理负责跟随骨骼位置。
- `UAccept`：预建掌心碰撞节点，配合对象池、下落物状态和接住/落地回调实现接取玩法。
- `UThrow`：把左右手 Palm/Grip/未知手势规整为独立状态，由 Manager 执行吸附、持有、挥手采样、飞行、落地和回收状态机；支持相机空间持物偏移、水平相机前向和 Editor 模拟。

## 接入当前点位

1. 先确认目标工程实际安装的 `Rokid.UXR.Interaction` API、`GestureBean` 字段、骨骼枚举和事件签名，再从上述能力选择最小组合。不要为了一个按钮同时引入接取、抛掷和缩放管理器。
2. 真机订阅、`GesEventInput.Instance` 轮询和骨骼读取使用 `#if UNITY_ANDROID && !UNITY_EDITOR`；Editor 模拟使用 `#if UNITY_EDITOR`；其他 Player 平台显式输出无效状态。不要使用一个裸 `#else` 把所有非 Editor 平台都当成 Android。
3. 如果非 Android 环境没有 Rokid SDK 程序集，连 `using Rokid.UXR.Interaction`、SDK 类型字段和回调签名也要隔离到宏或 Android 专用适配器中。平台无关的业务层只接收自定义 DTO，例如 `status/handType/position/rotation`。
4. 点位业务入口在运行时挂载手势驱动和 Manager，并显式初始化。保存动态挂载的组件引用；再次进入时先复用或清理旧实例，避免重复 `AddComponent`、重复 Action 绑定和重复 SDK 订阅。
5. 左右手食指代理、掌心 Collider、按钮触发区、视频/UI 和玩法容器优先在 Prefab 中预建且默认禁用。样例中 `Instantiate` 右手代理或左掌碰撞体的写法只作为旧实现参考，不直接带入新点位。
6. 点位会反复进入/退出或只是 SetActive 时，SDK 订阅应归属于本次会话：使用当前框架的进入/退出或合适的 OnEnable/OnDisable 成对管理。只有对象确定随会话销毁时，Awake/OnDestroy 才足够。订阅与解绑必须位于完全相同的宏条件中。
7. 追踪失败时按 `HandType.None/LeftHand/RightHand` 分别清理对应状态并隐藏代理。需要双手并行时分别保存两只手，不能因一只手更新就清空另一只手。
8. 读取 `skeletons`、`skeletonsRot` 前检查数组和索引；异常时回退到 `GestureBean.position/rotation` 或输出无效状态。不要用 `Pose.identity` 作为唯一有效性判断。
9. `Action` 参数对象可能被每帧复用；业务层若要跨帧保存，应复制需要的值。绑定回调时保存委托并在退出时解绑，不在 Update 中重复 `+=`。
10. 相机由业务入口显式注入优先于反复查找 `Camera.main`。相机空间偏移使用 right/up/forward；仅水平朝向时把前向的 Y 清零，并为近零向量保留上次有效方向。
11. Editor 模拟与 Android 真机是两条独立路径。条件编译只控制代码路径，不能证明编辑器已有等价输入；为需要的玩法提供有限、可关闭的模拟入口，并在真机复测追踪丢失与恢复。

## 迁移前必须核对的样例特性

- 样例 namespace 与模块资源包标识属于原点位，不能进入新点位。
- `UButton` 在 Awake 中克隆右手代理并动态添加 Collider；新点位应使用 Prefab 预建左右手节点，并对辅助组件做存在性检查。
- `UAccept` 会克隆左掌节点、动态挂载 `UPalm`，且部分下落物会自动补 Collider/Rigidbody；接入时应把可预建部分放进 Prefab，只保留确有兼容需求的兜底。
- `UGrip`、`UPalmWorld` 和 `UGesWrist` 的单手选择逻辑不是通用双手实现；根据玩法明确手别与并行规则。
- `UThrow` 的状态机、手掌中心计算、相机空间偏移和防重复完成回调值得复用；抛掷阈值、物理常量、地面命名和回收策略必须按目标点位验证。
- `UScale` 的起始距离与比例计算需要零距离保护，调试日志不能在发布路径每帧输出。
- `UScale.Update` 直接轮询 `GesEventInput.Instance`，迁移时必须补 Android 宏和非 Android 降级路径；`UThrow.Update` 的 Editor `#else` 也应改为 `#elif UNITY_ANDROID`，避免把其他 Player 平台误当成真机路径。

验证至少覆盖：首次进入、退出后再次进入、注册前手已在追踪、单手丢失、双手切换、追踪恢复、快速手势变化、Editor 模拟和 Android 真机。只验证事件回调不代表碰撞、UI 或玩法状态正确。
