# 从程序化植物到可泛化物理交互：研究与工程设计（2026-10-20）

## 要解决的问题和可辨识性边界

目标是在**推理时**导入一个此前未见过的 SpeedTree/FBX、Mesh 或植物 3DGS，不为这个对象编写交互脚本，也不指定逐对象的 Young 模量、阻尼、摩擦；手指从新方向推、拨、捏后，模型保持接触、局部形变与释放振动的合理性，并在 Vision Pro 上稳定运行。这需要跨对象训练所得的先验；目前仓库里的 PhysDreamer 康乃馨物性场和 PhysFlow 场景配置**不是**这种模型。

静态外观和几何并不能唯一决定物性。同样的叶形可以是新鲜叶、干叶或塑料；未知尺度、质量和力时，观测到的运动也不能唯一确定绝对弹性模量。因此目标应拆为：零次接触时输出**条件分布及置信区间**，接触发生后以少量已知/估计作用力做在线后验更新。若没有力标定，只可主张轨迹预测或相对顺应性，不应主张恢复真实绝对模量。

## 文献边界（只列与本问题直接相邻的工作）

| 已有工作 | 可借鉴 | 本项目尚需解决 |
|---|---|---|
| [Simplicits / NVIDIA Kaolin](https://developer.nvidia.com/blog/simulate-elastic-objects-in-any-representation-with-nvidia-kaolin-library/) | Mesh、GS、点云共用低维弹性表示与碰撞 | 物性、植物器官拓扑及跨资产推断不是现成的自动解 |
| [UniPhy, 2025](https://arxiv.org/abs/2505.16971) | 跨多材料的共享潜变量本构；从运动做逆仿真 | 推理仍需运动观测；训练域不等于薄叶/枝条 |
| [PhysTwin, ICCV 2025](https://openaccess.thecvf.com/content/ICCV2025/papers/Jiang_PhysTwin_Physics-Informed_Reconstruction_and_Simulation_of_Deformable_Objects_from_Videos_ICCV_2025_paper.pdf) | 接触视频逆建模与未见交互测试 | 对新资产的无需视频/无需逐对象拟合泛化尚未由此证明 |
| [MatPhys, 2026](https://arxiv.org/abs/2605.19386) | 部件级材质先验与共享 codebook，增强跨场景一致性 | 单目视频推断；未证明对任意程序化植物零样本接触有效 |
| [PhysReal, 2026 预印本](https://arxiv.org/abs/2609.07532) | 解析本构 + 局部神经残差 + 渐进式反演 | 主要是视频驱动对象特化；不直接给 SpeedTree 迁移权重 |
| [PhysSkin, CVPR 2026](https://openaccess.thecvf.com/content/CVPR2026/html/Lei_PhysSkin_Real-Time_and_Generalizable_Physics-Based_Animation_via_Self-Supervised_Neural_Skinning_CVPR_2026_paper.html) | 跨离散化的连续变形绑定 | 绑定不等于正确材料或接触动力学 |
| [NeuraLeaf, ICCV 2025](https://openaccess.thecvf.com/content/ICCV2025/html/Yang_NeuraLeaf_Neural_Parametric_Leaf_Models_with_Shape_and_Deformation_Disentanglement_ICCV_2025_paper.html)、[Demeter, ICCV 2025](https://openaccess.thecvf.com/content/ICCV2025/html/Cheng_Demeter_A_Parametric_Model_of_Crop_Plant_Morphology_from_the_ICCV_2025_paper.html)、[GaussianPlant, 2025 预印本](https://arxiv.org/abs/2512.14087) | 叶片、枝条或 GS 植物的结构化表示 | 结构恢复不等于学到接触物理 |

论文贡献不能只是把这些模块串起来。可检验的核心假设是：**器官图 + 尺度归一的材料后验 + 被动性约束的局部接触残差**，在未见物种和未见输入方向上，以固定运行预算优于统一材料、逐对象拟合、纯神经预测和现成通用低维绑定。

权重可用性也已核对：[PhysSkin 官方仓库](https://github.com/zju3dv/PhysSkin)提供 RigNet checkpoint，但它是通用绑定权重，不能直接给叶片真实材料；[UniPhy 官方仓库](https://github.com/HimangiM/UniPhy_CVPR2025)提供材料模型下载与从运动反演的示例，测试栈为 Ubuntu/CUDA，训练材料不含植物薄壳；[MatPhys 官方仓库](https://github.com/Yrainy0615/MatPhys)提供视频输入的 checkpoint，而不是静态 FBX 零样本预测。因此当前不能通过下载其中任一权重就宣称实现了目标。

## 资产事实与当前实现

[NVIDIA ORCA SpeedTree](https://developer.nvidia.com/orca/speedtree) 公开提供 Azalea、Boston Fern、Hedge、Japanese Maple 等 8 类 FBX 与 DDS；本仓库此前已下载全部 8 类到 `Research/SpeedTree/orca/SpeedTree_v2/`，但没有 Unity 导入或物理管线。许可是 **CC BY-NC-SA 3.0**，包含署名、非商业和相同方式共享要求；目前只选 Boston Fern LowPoly 作研究资产，不把这批文件称为无限制“开源”。这批资产是 FBX，不含可编辑 `.st9` 生成器，也不能假定 FBX 自带可信的枝条拓扑或真实厚度。Unity 对 `.st/.st9` 的原生导入另有 [SpeedTree Importer](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-trees/speed-tree/class-speed-tree-importer)；FBX 走普通模型导入。

已落地的第一段管线：ORCA Boston Fern 和 Azalea 的 LowPoly FBX + DDS + 许可证进入 `Assets/_Project/Imported/SpeedTreeORCA/`；Unity 编辑器 `SpeedTreeMeshExport` 导出经 FBX 坐标与缩放变换的顶点、三角形、三角形材质索引，支持对其他已导入资产用环境变量指定路径与许可；`build_asset_descriptor.py` 检查拓扑、尺度、材质分布并生成版本化 JSON。Boston Fern 为 561 顶点、696 三角形、21 个按共享顶点划分的连通分量；Azalea 为 3,132 顶点、4,362 三角形、139 个连通分量。这表明 FBX 叶卡和枝条不能被当成一个自动连通的弹性体。Boston Fern 的包围盒约 0.37 × 0.29 × 0.42 Unity 单位，但真实米制尺度尚未独立校准。导出的 `physics_ready=false` 是**刻意的门禁**：未识别根部、物理尺度、叶/茎结构和可迁移模型权重，不允许以视觉材质名冒充物理标签。

## 可训练系统架构

```text
SpeedTree .st/.st9 或 ORCA FBX ─┐
真实植物 GS + 可选 Mesh ───────┼─> CanonicalPlant ─> 结构推断 ─> 有置信度的器官图
程序化幻想植物 ────────────────┘        │              │
                                         │              └─ 枝/茎 rod、叶/瓣 shell、根部约束
                                         └─ 统一物理坐标、尺度、可见几何、绑定映射
几何 + PBR/图像 + 类别/环境 ─> 跨对象编码器 ─> p(z_material, z_contact | features)
                                                            │
手的接触位置/法向/速度/力 ───────────────────────────────────┤
                                                            ▼
                         GPU rod/shell/接触求解器 + 小幅、被动性约束残差
                                                            │
                                 Mesh 顶点 / GS 均值和协方差 / PBR / 声音事件
```

### 1. CanonicalPlant：表示与物理分离

单一资产契约包含：米制坐标与朝上轴、原点/根部、枝茎中心线及半径、叶/瓣薄壳面片和有效厚度、连接/邻接关系、视觉顶点或 GS 到物理自由度的绑定、原始资产哈希与许可证。输入可为 Mesh、GS 或混合；渲染表示不决定质量和刚度。FBX 中每个断开的叶卡不能自动视为独立实体；连接到哪条枝、固定在哪里必须由可审计的结构推断给出，置信度低时拒绝自动交互或请求一次性结构校正。

### 2. 跨对象物性后验，不是逐物体常数表

编码器输入为尺度归一的结构图特征（弧长、相对直径、分叉、叶面积/长宽比、相对厚度）、可见纹理/颜色、环境与可选植物类别；输出每个器官的 `log(EA), log(EI_1), log(EI_2), log(GJ), log(mass/length or area), damping, friction` 的分布与跨器官相关性。绝对米制尺度必须另外提供/估计，因为 `EI ∝ E r^4`，视觉半径或尺度错误会造成严重动力学错误。未知有效厚度作为潜变量，不能由渲染用叶卡厚度 0 直接代入。

零次接触用先验均值和不确定性运行；每次可信接触利用实际运动/反力做小步贝叶斯或优化更新，并记录是否属于训练分布。在线更新只调整低维潜变量，不能每帧反向传播大型视频模型。用户无需预定义该资产的推/捏动作。速度、力和接触点是运行时输入，不是预录动作模板。

### 3. 稳定求解器与表达绑定

用可解释的 rod/shell 弹性能（轴向、弯曲、扭转、叶片膜/弯曲）加接触互补约束；固定根与重力要进入显式边界条件。网络仅预测**正值**材料参数和有界残差势能/耗散，约束总储能非负、耗散非负，并对残差做能量/冲量裁剪。这样能避免纯神经 rollout 在长时间释放振动时能量爆炸。接触附近增加局部自由度，远处用低维模态；分辨率、步长和能量都要可记录。

Mesh 直接通过物理节点插值或蒙皮；GS 均值跟随同一形变映射，协方差用局部形变梯度 `Σ' = F Σ Fᵀ` 更新。相同物理状态驱动 Mesh/PBR 与 GS，禁止两套不一致的动画。训练和重计算发生在桌面 GPU；头显原生 Metal/Unity 只载入导出的轻量参数、结构、绑定和求解器。未来 Unreal 读取同一版本化 CanonicalPlant 包，替换平台求解/渲染适配层。

## 训练次序与数据条件

1. **现在：合成预训练。** 从多物种、多结构的可授权程序化植物和真实网格生成器采样几何，随机化有物理单位的杆/壳本构、厚度、密度、阻尼、边界、接触方向/速度/双指夹持；高保真离线模拟器提供状态与接触真值。ORCA 八类可作形状审计和保留测试，但数量不足以单独训练泛化。严格按生成器/物种/实例拆分，不能把同一几何的不同相机角度分到训练与测试。
2. **再做合成到真实迁移。** 使用已公开、许可允许且有真实接触和力度/尺度标定的数据；若无适合植物薄片/枝条的数据，就必须承认真实泛化尚未验证。PhysDreamer/PhysFlow 的单资产数据可用于渲染绑定或对象内对照，不应当作跨植物监督真值。
3. **未来用户采集。** 在方案、标定和误差预算定稿前不开始。Vision Pro 照片/手轨迹可提供视觉与运动观测，但不足以单独恢复绝对接触力、隐藏叶片结构、厚度和质量。设计成熟后再考虑多视角同步、米制标定、已知边界、重复推/拨/捏、独立力参考、音频同步和对象级留出。数据采集是验证与真实微调阶段，不是当前代码完成的前提。

## 必须做的对照、消融与门槛

- **对照：** Unity/SpeedTree 风 Shader、统一手调弹簧参数、几何缩放规则、逐对象逆仿真、无结构图的直接网络、Simplicits/PhysSkin 类通用绑定、完整方法。相同 Mesh/GS 画质、物理自由度、训练数据、运行预算。
- **消融：** 无器官图、无尺度归一、无材料后验、无在线更新、无被动性约束、无接触局部自由度；至少测试未见物种、未见大小、未见抓握方向、不同叶片含水状态。
- **指标：** 有标定力的接触位置/穿透和反力，顶点/GS 轨迹误差、轮廓与新视角图像、振动频谱和松手衰减、能量增长率、OOD 校准误差、单眼 GPU 时间、双眼帧时与头显感知延迟。可接受阈值需要由预注册实验与具体任务设定，不能拿桌面截图当证明。
- **失败即停：** 若 ORCA FBX 无法可靠恢复连接图和尺度，先完成结构估计/校正；若只在同一对象不同动作上有效，不能声称跨资产迁移；若推断刚度分布不校准，不能宣称“无预定义自然物理”。

## 当前工程下一步

1. 用已导出的 Boston Fern 拓扑先做自动器官图推断与人工标注对照，量化连接错误；增加 Azalea 和 Red Maple Young 作为不同形态的盲测资产。
2. 定义 `CanonicalPlant` 二进制交换格式与 `MaterialPosterior` 权重/版本校验；未有训练权重前只运行**明确标注为基线**的几何物理，绝不把它称作学习泛化。
3. 建立离线高保真 teacher 与统一接触轨迹生成器，先在合成留出测试验证物性预测，再导出到 Vision Pro 对比 PC/设备帧时与接触质量。

运行当前审计：

```sh
# Unity 菜单：Physics Hands > Gaussian World > Export ORCA Boston Fern Geometry
Research/GaussianWorld/.venv/bin/python Research/SpeedTree/build_asset_descriptor.py
```
