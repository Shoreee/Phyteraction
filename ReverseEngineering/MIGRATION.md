# Vision Pro 手部物理实验室迁移记录

本次范围：Vision Pro 手部物理实验室。不是原作全部关卡、医疗玩法、成就、绘画或第三方插件的完整移植。

`output/signatures/Assembly-CSharp` 包含 735 个伪 C# 签名文件，没有方法体；不能直接编译，也无法据此证明与原程序逐行一致。目前使用原始序列化骨架、关节、网格、手势资源，加上独立重写的运行逻辑。`output/migration-audit.json` 逐文件记录与实现的名称匹配；名称匹配不代表行为完全一致。通过 `python3 ReverseEngineering/audit_migration.py` 更新清单。

## 已定位并修复

- 导入范围只有 `Humanoid/Hands`，而手腕锚点和 IK 目标位于 `Humanoid/References`。原引用变成 null，运动目标不再跟踪手部。导入器现在显式重建锚点并连接 TargetBody / TargetRotation。
- 原手腕另有连接手臂的关节，手臂未迁移时 `connectedBody = null` 变为世界约束。移除这一悬空的手臂关节与对应 Enforcer，保留手腕驱动关节和指骨关节。
- 源蒙皮包含无权重的空骨骼槽。仅为无权重槽补合法引用；如果缺失的是有权重骨骼则中止导入，避免静默破坏手型。移除没有导出网格的废弃 XR 可视组件。
- HandPoser 不再删除可能被其他蒙皮或骨骼引用的整个节点，只隐藏其渲染器。
- 重建 Mesh/Material 时保留原资源 GUID，不再删除后创建，避免已有场景和预制体引用失效。
- 真机构建使用 Metal 全沉浸模式。PolySpatial 设置目前保留工程现状；编辑器自动测试显式隔离远程 XR 输入，避免远程头显覆盖模拟测试的相机姿态。
- 摄像机增加 XR 头部姿态驱动。头显与双手共用 XR Origin 的地面偏移，请求 Floor 模式，Camera Y Offset 为 0，避免和 visionOS 已经包含眼高的姿态再叠一层。编辑器未跟踪时摄像机局部眼高 1.35 米。
- Auto 跟踪支持延迟启动/停止的 XR 子系统；Vision Pro 真机不会降级成键鼠假手。失去手部跟踪时释放持物与手势状态。
- 实验室使用独立的 `PhysicsSetup_Laboratory`（90 Hz，最大补帧时间 0.1 秒），缩短物理与插值等待；原始 Default / Performant / Accurate 预设仍保留。

## 实验室内容

场景为 `Assets/_Project/Scenes/Sandbox.unity`。包含原始双手骨架、物理指骨、抓取/捏取链路、预定义姿态库，以及抓取、堆叠、弹跳实验台。道具有各自的物理材质，可越界自动恢复。台面 RESET / GRAVITY 按钮支持手部触碰。

桌面操作：Tab 切换左右手；按住鼠标右键移动手；滚轮调整前后；G 或鼠标左键抓握；T 食指捏取、Y 中指捏取；Q/E 滚转、R/F 俯仰、Z/C 偏航；X 重置选中手；Backspace 重置全部道具和双手；1 切换道具重力。操作时让 Game 视图获得焦点。

原有 Telekinesis / ForceField 脚本保留，但本次默认实验室不启用超能力；它们不计入本次已验证功能。绘画、手臂/全身、医疗和关卡系统不在选定范围内。

## 运行与验证

- `Physics Hands > Import > Build Scaffold` 重新生成手部预制体与实验台，会覆盖生成的 Sandbox 场景；手工修改前先另存场景。
- `Physics Hands > Validation > Run Laboratory Smoke Test` 进行 20 秒 Play 模式检查，并写入 `Logs/laboratory-validation.json` 与 `Logs/laboratory-preview.png`。测试会移动/重置实验道具，用 G 键走抓取释放、用 T 键走捏取，并平移头显原点确认摄像机和跟踪手一起移动。请勿同时操作。
- `Physics Hands > Validation > Compile visionOS Player Scripts` 检查真机条件分支的脚本编译，结果在 `Logs/visionos-script-validation.txt`。
- visionOS 使用 Metal / Full Immersion。PolySpatial Play To Device 对应 RealityKit 模式，不是本场景的 Metal 真机验证路径；应构建 Xcode 工程后在 Vision Pro 运行。
- 编辑器通过不等于真机通过。手部权限、头部/手部对齐、真实遮挡、捏取阈值、真机帧率仍需佩戴设备验证。

修改前备份：`ReverseEngineering/backups/before-repair-20260926.tgz`，包含原工程脚本、场景、预制体和相关配置。

## 2026-09-27：跟随、折指和持物修复

- 同手碰撞链路此前未排除自身碰撞。指尖是 kinematic，和相邻指骨重叠时无法由碰撞推开，导致相互挤压和关节持续扭转。启动时仅忽略同一只手的非触发碰撞体之间的碰撞，保留双手之间及手与外界的接触。
- 指节目标改为相对于对应参考父骨的旋转，避免物理父骨偏转后让下一节反向补偿。拇指/小指基骨相对于物理腕部的参考骨架计算，保留它们的中间掌骨。
- ResetJoint 不再断开重连并将变形姿态写入 rest frame；原始关节框架、锚点保持稳定。
- 保留上轮指尖完整位置/旋转跟随修复，避免蒙皮被拖长。
- 实际应用 HumanoidSettings 中已有的 solver 迭代倍率（此前遗漏），而不是只读取质量预设倍率。
- 抓握增加持续检查：握拳状态先到、物体接触后到，也能捕获。强制释放后需要重新张手才可抓取，并在张手时可靠重新启用。
- 持物关节显式开放受驱动轴，让已有有限弹簧/阻尼生效，不依赖新关节组件的默认轴设置。未关闭持物时手指与物体的正常碰撞。
- XR 关节子节点/指尖数据缺失时保留上次局部姿态，避免遮挡期间退回不兼容的原始 XR 旋转轴；复用映射缓冲区避免每帧分配数组。
- 原始 Skin Default（动态 0.2 / 静态 0.5）和 Skin Sticky（3 / 3）材质已迁移，本轮未通过提高摩擦来掩盖抓取状态或关节问题。

新增验证菜单：`Stress Both Hands` 检查 30 根指骨、张手恢复、运动跟随误差和 15 厘米位置阶跃；`Grasp Lift Release Both Hands` 对每只手执行先握拳后接触、300 克物体抬升、保持、松手下落与张手恢复。报告分别保存于 `Logs/finger-stability.json` 和 `Logs/grasp-recovery.json`。模拟测试并不能复现 visionOS 的真实遮挡与跟踪噪声。

参考 Unity 官方文档：
- [Rigidbody 插值](https://docs.unity.com/en-us/engine/6000.3/manual/physics-section/physics-overview/rigidbody/rigidbody-interpolation)：插值增加一个物理更新周期的显示延迟。
- [关节稳定性](https://docs.unity.com/zh-cn/engine/6000.0/manual/physics-section/physics-overview/ragdoll/ragdoll-stability)：不可能同时满足的约束和碰撞、迭代次数与关节稳定性有关。

本轮修改前的运行时代码备份：`ReverseEngineering/backups/responsiveness-20260927/Runtime`。完整旧 Xcode 导出备份：`Builds/VisionOS-before-responsiveness-20260927`（本轮导出前版本）。目标导出仍为 `Builds/VisionOS/Unity-VisionOS.xcodeproj`。

本轮编辑器实测：90 Hz；双手运动腕部平均位置误差约 6.4 mm、最大 13.8 mm；15 cm 阶跃后约 37 ms 进入 1 cm 误差范围。该数字受测试轨迹和编辑器采样频率影响，不是摄像头到显示的真机总延迟。30 根指骨张手恢复检查通过；双手分别将 300 g 方块抬升约 23 cm，保持、释放、下落和张手恢复通过。基础场景/手势/原点对齐检查也通过。各项原始报告和时间戳见 `Logs/hand-repair-verification.json`。

## 2026-09-28：手与物体阴影修复

发现透明跟踪手、描边与物理骨手重复投影，透明材质仍接收阴影；主光虽然设置 Soft，但 URP 未启用软阴影。原 50m 单级联也不适合近距离手部实验。

HoloHand 只让当前可见物理网格投射/接收阴影，透明与描边层关闭投影和接收；隐藏骨手时切换到皮肤网格。导入器和设置流程同步政策，避免重建后回退。URP 改为 8m、两级联、2048 主光阴影、低质量软阴影，降低法线偏差；场景光采用管线设置。

Logs/shadow-validation.json 通过：物理手 caster=2、透明 caster=false、soft=true、cascade=2。Logs/shadow-preview.png 已检查近场物体与手部投影。Unity visionOS 导出成功，更新 Builds/VisionOS 原工程，旧导出保存在 Builds/VisionOS-before-shadows-20260928。此轮未进行 Vision Pro 真机渲染验证或 Xcode 原生编译安装。

游泳移动研究与建议见 Research/SwimmingXR-20260928.md；尚未将游泳移动加入当前实验室。

## 2026-10-13：REA 原包续查

见 `rea-20261013/REPORT.md`。REA 6.4.0 独立核验核心模块加密，独立节区检查确认 UnityFramework 整个 __text 均在加密范围；补全两游戏程序集 12,219 条方法声明索引，未恢复方法体。当前工程驱动公式仍是重写实现。本轮未修改运行逻辑或重新构建 Xcode。

## 2026-10-20：手部锁定跟踪，高斯场景逐物体材质

手部：骨骼手此前只有腕部跟手。快速反复摇动时，三节指骨的目标由弹簧驱动追赶姿态，弹簧在反向时持续振铃，看起来就是手指自己乱甩；`XRHandToOVRHandMapper` 在两段骨骼方向接近 180° 时取 `FromToRotation` 的任意垂直轴，也会让手指整圈翻转。`HoloHand` 现在在跟踪源可用时立即把整条链（腕 + 30 指骨）切成 kinematic，并在每个固定步与每帧末把骨骼直接映射到目标姿态（固定步供求解器读碰撞体，帧末供渲染）；`_lockToTracking` 可关掉回到弹性驱动。`SwingOnto` 在接近反向时改用骨骼自身坐标系的摆动轴。关节驱动在锁定状态下不再重写。

高斯场景：手交互此前对所有场景沿用 PhysFlow 发布的配置（GPT-4 初值，只对脚本回放调过），因此在回放之外每件物体都在指尖压力下屈服，整组看起来像同一种软泥，还能被戳穿。现在 `MpmSceneAsset.ApplyInteractionProfile()` 为九个场景各写一份交互材质：只有 carnation 保留 0.7 的断裂应变（唯一可撕裂）；hat/telephone/fox/kitchen/plane 用弹性律并提高 E（plane 3e7 近似刚性金属）；jam 用 6e4 Pa 的非牛顿膏体；sandcastle 用带 0.06 凝聚力的砂。`MpmWorld` 的破坏与连通性判定改读 `asset.fractureStrain`，不再写死场景名；profile 带版本号，旧资源在加载时就地升级。`Mpm.compute` 的 Deform 收紧 splat 轴拉伸并把中心位移限制在静止偏移的两倍内，避免流动态材质出现针状高斯。

未进行批处理验证：当时 Unity 编辑器占用工程，`MpmValidation` 与 `FingerStabilityValidation` 需要关闭编辑器后重跑。
