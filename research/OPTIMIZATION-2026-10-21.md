# 2026-10-21 高斯场景与物理手修复记录

已修改工程并完成 Unity 6000.4.7f1 / Metal 编辑器验证。全部九个模型的高斯总数、可动高斯数均保留；没有通过裁剪高斯、降低渲染分辨率或换低精度颜色获得下列结果。当前修复不等于所有模型已经在头显达到实时物理：康乃馨、海芋和沙子的完整 MPM 仍是明显瓶颈。

**根因与修改。** 初始同机测量显示，康乃馨、帽子、狐狸单眼绘制仅约 1.15、2.10、3.84 ms，而一次 11.11 ms 物理时段的 GPU 模拟耗时约 68.27、199.10、265.51 ms。主要问题在 MPM；原先自适应 `budget` 被计算后没有限制实际子步数。

渲染改为一张四顶点网格逐高斯实例化，移除按二次幂填充的大网格与无效顶点，保持现有投影、SH、透明混合和高斯足迹。平行双眼仅在同帧且方向一致时共享排序；相机层级遮罩不绘制时不再排序。每个实例使用独立 compute shader 状态，避免不同场景覆盖彼此的 buffer 绑定。

MPM 粒子按 Morton 顺序排列，P2G 使用紧凑线程组节点缓存合并整数原子操作；缓存放不下时回退完整传输。塑性投影与应力计算共用一次 SVD。完整源采样与旧的两次 SVD 对照，在三个代表场景的 60 步按压/释放测试中，渲染中心的 P95 差异小于 0.009 mm。

交互积分与渲染高斯分离，积分保留体积、体积一阶矩及原始边界成员关系，每个高斯仍由位移场和变形梯度驱动。经过比较，仅帽子、狐狸、果酱、推土机启用子单元积分。植物、电话线、螺旋桨、沙子因粗积分偏差过大保留全部原始物质粒子。海芋和电话线采用通过当前回归门槛的至多两倍源步长；康乃馨、金属、沙子保持原始子步上限。论文回放始终使用完整源采样、原始子步上限与参考本构路径。原始配置是作者公开的 GPT 初始化，工程没有论文优化后的物理参数，不能把回放称为最终论文结果复现。

帧预算现在实际限制提交量，并在 `time_scale`、`SimTime` 和 CSV 中反映物理时间减速。180 Hz 手部物理中同帧多余 fixed step 的时长累计到下一次 GPU 提交，防止每帧只提交一次时无意把 MPM 速度减半。预算保护渲染响应；**预算不足时物理会减速，不代表完整物理已经实时**。

**逐指节追踪与拇指。** 30 个指节及两侧手腕保持动态刚体，末端也保留关节。每个指节直接追踪参考手对应骨骼的世界位置和旋转，加入速度前馈、向后 PD 和惯性张量换算的力矩。平移与旋转具有不同响应带宽，移除旧马达与新驱动的争抢。自由移动与接触受阻采用有限且不同的追赶上限。移除 LateUpdate 瞬移、跨墙的距离安全重置以及追踪时的关节投影瞬移；关节长度、限位和接触由 PhysX 共同求解。实验室物理步长改为 180 Hz。拇指末端局部位移从米制追踪骨架换算到 0.01 缩放的物理骨架后再更新连接点。

**尺寸和墙面固定。** 原导入器把扩大后的显示覆盖集合用于归一化，改变了作者配置所依赖的坐标。海芋和帽子的固定 cuboid 当时各覆盖 0 个粒子，康乃馨仅覆盖 1 个。现在用作者原始中心距离 mask 的范围建立模拟坐标；覆盖扩展高斯继续完整渲染，并绑定到原始物质粒子，避免把长协方差尾部误当成模型外的物质。恢复源配置中的固定区域，没有追加任意冻结刚体或把整个物体设为 kinematic。九份配置与官方仓库中的 JSON 逐项一致。

| 场景 | 各原始固定区域覆盖的源粒子数（区域可重叠） |
| --- | --- |
| carnation | 420 |
| alocasia | 5453 |
| hat | 11356, 12005 |
| telephone | 1535, 1830 |
| fox | 124424 |
| jam | 1769 |
| kitchen | 34856 |
| plane_960 | 源配置没有固定 cuboid |
| sandcastle | 38114 |

绝对米制尺寸仍是演示的显式设定，不是论文标定得到的真实尺寸；例如康乃馨主物质最长边 0.32 m、帽子 0.30 m。渲染、粒子、固定区域共用同一 `localFromSim`。修改实际展示尺寸应在预处理的 `SCENES[scene].meters` 中设定并重新导入，不能仅缩放显示层。

**实测性能。** Apple M5；单眼 1920×1824、90° FOV，12 次排队重复后同步 GPU。模拟使用完整 11.11 ms 时段，所有行 `timeScale=1`；未靠减少已模拟的时长获得此表。绘制列扣除了空相机及排序，数值包含测量噪声。工程坐标与固定边界同时得到修复，所以前后基线不是完全隔离其他变量的微基准。

| 场景 | 保留高斯总数 | 源粒子 / 交互积分点 | 模拟 ms | 默认视图单眼绘制 ms | ROOM 单眼绘制 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| carnation | 549,125 | 99,165 / 99,165 | 70.45 | 1.15 | 6.86 |
| alocasia | 588,362 | 135,313 / 135,313 | 59.91 | 2.15 | 7.09 |
| hat | 441,723 | 259,873 / 11,550 | 6.32 | 2.17 | 5.70 |
| telephone | 421,221 | 48,358 / 48,358 | 12.82 | 0.70 | 15.51 |
| fox | 986,386 | 354,881 / 7,648 | 4.02 | 3.60 | 9.28 |
| jam | 560,371 | 14,651 / 6,936 | 3.83 | 0.26 | 6.96 |
| kitchen | 1,770,264 | 141,819 / 8,087 | 4.02 | 2.38 | 14.44 |
| plane_960 | 3,154,456 | 24,029 / 24,029 | 14.79 | 0.45 | 22.29 |
| sandcastle | 1,385,485 | 233,960 / 233,960 | 190.21 | 1.65 | 10.30 |

帽子从约 199 ms 降至 6.32 ms，狐狸从约 266 ms 降至 4.02 ms。康乃馨约 70 ms、沙子约 190 ms 的完整时段成本仍不满足头显实时预算；这些模型默认依赖帧预算降低模拟时间推进量。ROOM 保留完整扫描背景，螺旋桨场景的全背景单眼绘制约 22 ms，也不能宣称双眼 90 Hz。要继续提高这些场景的实时物理速度，需要经过新验证的求解器或设备上的进一步优化；没有以未经验证的粗积分替代它们。

**已执行验证。**

穿板测试：合成追踪目标穿过 25 mm 板子 197.5 mm；两手共 30 个动态指节，包含受阻弯曲、撤回恢复及快速自由平移。最大物理穿透 0.81 mm，最大连接点误差 7.13 mm，快速移动最大逐指节位置误差 27.01 mm；撤回后达到恢复门槛。视频中的实心手是物理手，青色线条是追踪目标，蓝色板有实体碰撞体。结果通过。

XR 拇指输入测试：左右手共 180 组合成 provider 姿态，覆盖 CMC、MCP、IP 运动及末端长度变化；映射目标尖端误差通过 0.1 mm 门槛，最大骨段瞄准误差 0.028°。这是输入/映射验证，不是头显传感器精度测量。

完整源粒子/参考本构与默认交互设置比较：九个场景，同一 4 mm 球形按压后释放，60 个 90 Hz 物理步，每 15 步比较全部可动高斯中心；体积与一阶矩保持、重置绑定、数值有限性、回放完整采样均通过。几何差异使用与原始网格分辨率相关的门槛，不能解释为两种离散轨迹完全相同。

| 场景 | 相比完整源路径的中心差异 P95 mm | 本次测试门槛 mm |
| --- | ---: | ---: |
| carnation | 0.009 | 8.40 |
| alocasia | 4.653 | 11.00 |
| hat | 7.408 | 8.00 |
| telephone | 7.170 | 9.00 |
| fox | 1.055 | 7.00 |
| jam | 0.172 | 3.60 |
| kitchen | 0.386 | 8.00 |
| plane_960 | 0.008 | 7.00 |
| sandcastle | 0.006 | 5.00 |

仍需真实头显采集左右拇指、各指节误差、MPM 接触穿透、反作用延迟、双眼 GPU 时间与 `time_scale`。本次穿板是 PhysX 板与合成手目标测试，不能替代所有高斯模型上的真实设备接触验收。未声称头显 90 Hz、零延迟或所有模型的物理实时问题已解决。

[穿板测试视频](<../artifacts/reports/board-pursuit/board-pursuit.mp4>) · [穿板数据](<../artifacts/reports/board-pursuit/result.json>) · [拇指输入测试](<../artifacts/reports/repair-2026-10-21/thumb-retarget.json>) · [九场景形变比较](<../artifacts/reports/repair-2026-10-21/integration-comparison.json>) · [导入审计](<../artifacts/reports/repair-2026-10-21/import-audit.json>) · [优化前 GPU 数据](<../artifacts/reports/repair-2026-10-21/before-gpu.json>) · [优化后 GPU 数据](<../artifacts/reports/repair-2026-10-21/after-gpu.json>)

**清理后的验证记录。** 上述数值和链接是 2026-10-21 优化阶段保存的证据。随后按仓库清理要求删除了穿板、拇指、形变比较和 GPU 基准的临时验证脚本，原菜单和批处理入口已不再存在。保留运行时 `GaussianMetrics` 及其读数、CSV，以及 `useInteractionQuadrature`、`useAdaptiveBudget` 等实际运行配置。完整源采样的论文回放和实时帧预算继续保留。

**调研依据。** [PhysFlow 官方实现与数据说明](https://github.com/zhuomanliu/PhysFlow)、[原始预处理顺序](https://github.com/zhuomanliu/PhysFlow/blob/main/simulation.py)、[原始坐标归一化](https://github.com/zhuomanliu/PhysFlow/blob/main/utils/transformation_utils.py)、[PhysFlow 论文](https://openaccess.thecvf.com/content/CVPR2025/papers/Liu_Unleashing_the_Potential_of_Multi-modal_Foundation_Models_and_Video_Diffusion_CVPR_2025_paper.pdf)、[Unity Gaussian Splatting](https://github.com/aras-p/UnityGaussianSplatting)、[Unity 实例绘制 API](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Graphics.RenderMeshPrimitives.html)。另检索了 [SeeLe，CVPR 2026](https://github.com/SJTU-MVCLab/SeeLe) 与 [Neo，ASPLOS 2026](https://doi.org/10.1145/3779212.3790192)。没有把新的论文名称当作性能保证，也没有宣称本工程已经实现这些系统；实际采用并量测的是上述实例绘制、守恒积分、传输缓存与本构复用。


## 后续真机反馈：拖动、手刚度与异步接触边界

本轮基线提交为 `6187acd`。visionOS 3.2.2 包内 `VisionOSSpatialPointerEventListener.GetPointerId/QueueEvent` 明确复用输入 ID，并在阶段缺失时合成 Began/Cancelled；永久消费 ID 的应用逻辑因此会拒绝后续拖动。修复以手势活动期消费、结束后重新允许同 ID，并等待延迟到达的 kind/ray 元数据；不在释放时转入骨骼输入。

手部柔软与末端粘附包含两个独立因素：物理骨节使用了静／动摩擦均为 3 的粘性皮肤，以及接触时的逐骨节加速度限幅。改用正常皮肤摩擦，提高有限驱动响应，保持关节限位和动态碰撞。自动包裹另有两处问题：捕获后的局部姿态忽略原始手指变化，预测碰撞位置漏乘厘米骨架的缩放。修复为保留闭合状态并叠加原始姿态增量，允许脱离接触的动作、拒绝增加穿入的目标；继续以实际碰撞表面收拢。中心吸附框架与表面间隙分开，避免为了把标记移到质心而将掌心拉进物体。

GPU MPM 与 PhysX 之间是异步、不同时间尺度的分区边界。以仿真时长将冲量还原为力后，直接对 25 g 的指骨重复施加最多 8 N，会允许约 320 m/s² 的单方向加速度；摩擦合力也不是接触几何法线。现在回读独立的几何法线，按刚体聚合多个碰撞体的贡献，用接触点平动／转动有效质量约束冲量；自由速度包含本次物理步已排队的肌肉力和力矩。只保留小幅分离偏置、反对滑移的摩擦，不继续推动已分离的指骨；过期或已远离样本的反馈作废。网格重叠修正改用物理耦合周期，而非极小 CFL 子步，去掉相对两指直接焊接网格速度的分支。花朵已有模态接触路径保持原样，材料参数、PAPER 无手回放和 Gaussian 数据保持原样。

依据：[PhysFlow 官方仿真实现](https://github.com/zhuomanliu/PhysFlow/blob/main/mpm_solver_warp/mpm_solver_warp.py)、[MIT 多体动力学接触约束](https://underactuated.csail.mit.edu/multibody.html)、[Yu 等，2025，异步 MPM／刚体凸接触](https://arxiv.org/abs/2503.05046)、[Hu 等，2018，MLS-MPM／CPIC 双向耦合](https://yuanming.taichi.graphics/publication/2018-mlsmpm/mls-mpm-cpic.pdf)。这些来源支持区分材料本构、单边接触、冲量与异步耦合；本轮没有实现论文完整的凸优化／隐式求解器，也没有据此宣称全局稳定或任意动作下零穿透。当前冲量限制是对既有分区求解器的保护，发生限制时不保证与 GPU 端严格动量守恒。需要更高接触精度时，应以完整的隐式双向接触求解器作为独立改造与基准，而不是继续提高反作用力上限。

验证结果保存在忽略版本控制的 `artifacts/reports/contact-refinement`。编辑器合成输入不等同于新版本头显实测；头显帧率、真实抓握和遮挡恢复须在安装新版后确认。

## 粘性皮肤、下载枪模型与接触时钟修订

本节取代上节“普通皮肤”和“异步限幅回读”的运行时描述。原始粘性皮肤资产和各碰撞体的材料分配已恢复。静态接触不仅约束平移目标，也约束接触点处的向内旋转目标；物体上的摩擦和夹持压力继续保留。跟踪关节仍使用 PGS、96 次位置／16 次速度迭代，不启用投影瞬移；不可达目标关闭关节预处理。360 Hz 物理步和 60 Hz 位置响应通过本轮合成输入门槛，驱动力／力矩仍有限。TGS 对照在该骨架上失稳，额外增加 PGS 迭代也没有稳定消除误差，因此未保留这些尝试。

本轮穿板测试：两手 30 个动态指节，追踪腕部穿板 197.5 mm，1170 次采样；最大穿透 2.22 mm、连接误差 5.81 mm、快速自由追踪误差 28.61 mm、最终恢复位置误差小于 0.001 mm。该结果不等于真实传感器精度或所有接触条件下的零穿透。

手部蒙皮同时启用了转换成不透明材质的替代外层，Skin 还用第二个材质重绘最后一个子网格，造成深度竞争及重叠阴影。现在每手只显示一个实体蒙皮并使用一个阴影投射源；丢追用该表面材质与掌心提示，不再叠加共面的不透明轮廓。仍需在头显实测阴影级联与传感器抖动。

LAB 原有七个可动物体全部移除 Holdable／GripAnchor，使用纯摩擦。特殊握持示例采用网上下载的 [Kenney Blaster Kit 2.1](https://kenney.nl/assets/blaster-kit)，CC0；只引入 blaster-b、必要调色板与许可。碰撞通过 CoACD 1.0.14 对完整源几何进行离线近似凸分解，烘焙为 28 个部件；只保留实际顶点，避免把整个凹形握把做成单一凸包。此前按连通部件的方案不能充分表达凹处，未作为最终碰撞资产。握持坐标从实际骨节建立，避免依赖旧掌心标记的局部轴；专用关节姿态加上原始手指变化，经关节限位和碰撞约束后驱动。没有关闭碰撞、取消重量或瞬移捕获。

提示圈固定半径，24 个矩形刻度按顺时针逐个出现／消失；反向切换保留透明度。箭头等待完整提示圈出现后显示。提示资源拥有独立生命周期，来源停用后通过协程完成退场，再释放网格和材质。

MPM 的原预算在高负载下将物理时间缩慢到手部时钟的一小部分。旧异步反作用力跨多个 PhysX 步保持、限幅或重放，无法与本次粒子冲量对应。本轮改为每个 fixed step 完整推进其时段，按作者 substep 上限和 CFL 决定子步数；碰撞形状在相同区间扫掠。GPU 回读本步等大反向冲量和力矩，在 PhysX 积分前施加一次，去掉第二次 G2P 速度投影。每个子步使用不可变的刚体快照；质量和逆惯量构成的共享迁移率上界限制并行网格节点对同一刚体的重复响应。不是手工按场景调力上限，也没有改变材料 E、nu、密度、屈服或黏性。

这是分区耦合，GPU 仍按单独刚体的迁移率计算，尚未实现整条手骨的约束逆质量或论文完整凸优化器。不能把共享上界称为完整隐式接触的收敛证明。合成有限质量碰撞测试在缩放及反射坐标下检测到双向反作用，最大线动量误差约 1.30e-7 kg·m/s，测试中未出现能量爆炸。十个模型的球形探针均获得接触／反作用；这不证明所有材料的真实穿透深度和触感已符合论文。Flower 模态路径保留。

完整时长与同步边界具有明确代价：本机每 10 ms 模拟区间（含变形上传、GPU 同步，不含相机绘制，无接触）的中位耗时，康乃馨 70.98 ms、海芋 104.66 ms、帽子 7.31 ms、电话线 26.60 ms、狐狸 5.21 ms、果酱 4.25 ms、厨房 4.89 ms、金属螺旋桨 13.37 ms、沙堡 190.03 ms；Flower 每 10 ms 区间约 0.25 ms。海芋／电话线不再放宽作者子步上限，所以不能与前表的两倍步长结果直接视为同一算法微基准。部分 MPM 模型仍远未达到实时要求；本轮没有借减慢物理时钟或减少高斯掩盖成本，同步回读也会增加 CPU/GPU 等待。需要单独验证更高效的求解与接触离散，不能靠提高反作用力上限解决算力不足。

九份 MPM 数据仍是作者初始化，没有训练 checkpoint。官方固定版本 `6beba6746fad4590f5b63ce6fa85c3b862b182d9` 的材料张量是一元素，`ckpt/sim.json` 对本工程支持的优化字段无损；空间变化的派生实现不接受均值导入。新增工具仅预检／调用官方训练，以及验证并导入其结果，记录配置、源点云、预处理载荷、checkpoint 与训练代码来源。当前 Mac 无 NVIDIA CUDA、相应 torch／warp 环境，且缺少 `images_generated` 指导帧，未启动训练，未伪造训练参数。以后提供 CUDA 机器时，还需补齐指导数据与官方依赖；真实米制尺寸／力标定也需要额外观测，不能从初始化配置凭空得到。

本轮验证证据位于忽略版本控制的 `artifacts/reports/grip-pipeline`；临时验证与生成代码完成检查后删除。54 项现有模型／材料／元数据哈希保持一致，完整高斯数量和属性保留。编辑器测试与 visionOS 导出不等于已经安装新版头显并完成实际抓握验收。

碰撞烘焙采用 [CoACD 官方实现](https://github.com/SarahWeiii/CoACD)，没有将其 Python 或原生运行库加入 Unity 构建。

依据：[PhysFlow 官方训练与 checkpoint 输出](https://github.com/zhuomanliu/PhysFlow/blob/main/simulation.py)、[材料参数定义及单位](https://github.com/zhuomanliu/PhysFlow/blob/main/mpm_solver_warp/mpm_solver_warp.py)、[MIT 多体接触约束](https://underactuated.csail.mit.edu/multibody.html)、[2025 凸 MPM／刚体接触研究](https://arxiv.org/abs/2503.05046)、[MLS-MPM／CPIC](https://yuanming.taichi.graphics/publication/2018-mlsmpm/mls-mpm-cpic.pdf)。


## 2026-10-22：四枪对照、实体扳机与皮肤恢复

本节替代前面的 Kenney 枪示例描述。采用用户提供的两个 MARATHON FBX，分别制作纯摩擦与柔顺握持副本，共四把，质量均为 500 g。几何、法线和 UV 保留；仅合并完全一致的顶点属性，不删渲染三角形。两个枪口均读取用户单顶点标记的实际世界位置，不能用标记对象原点。导入时将原文件的米制外观缩放 0.5，使第一把总长约 32 cm、第二把约 52 cm；这是交互道具尺度，不宣称还原实体枪规格。坐标转换为 Unity +Y 向上、+Z 沿枪口向外，移除源场景的远离原点偏移。

握持和使用分开：纯摩擦枪没有 Holdable／GripAnchor 或持有关节；柔顺枪的有限驱动对齐作者掌心框架，其余手指在关节限制和碰撞约束下向握姿收拢，食指保留独立真实追踪。抓握或捏合近距离捕获，张手／失追释放。两种模式均用同一实体扳机，第一把按用户标注沿枪身向后滑动 6 mm，第二把绕独立扳机上端铰接、最大 20°。按下阈值 70%、复位阈值 20%，需在复位区停留 50 ms；仅最近的有效追踪食指接触授权发射。接触事件来自实体指骨，不以整手握紧代替扣扳机。冷却、扳机与弹道使用同一物理时钟，避免编辑器回放／追帧时混用墙钟造成漏发。原生 HingeJoint.angle 曾在刚体旋转／复位后的测试读到 NaN，而刚体四元数有限；触发逻辑改从连接刚体的相对姿态提取轴向转角，并拒绝非有限输入。

弹丸是低速虚拟测试参数（8 g、12 m/s），不作真实弹药标定。固定 48 个可复用实例；短拖尾自然淡出，逐步球扫掠和初始重叠查询防漏检，碰撞后向刚体施加动量冲量。枪口施加等大反向发射冲量，目标为十一段有限弹簧关节布娃娃，无血腥效果。命中点以物理刚体姿态转为局部位置，渲染时跟随其插值姿态；避免将物理查询命中点直接相对旧渲染 Transform 记录而产生偏移。RESET、停用和离开 LAB 清理弹丸、持有与扳机状态。两侧枪架为按钮和工作台拖动留出中间区域。

最初两个 FBX 只包含丢失的外部纹理引用。随后用户提供 `MARATHON_GUN_BUNDLE_1.blend`，内含两把枪对应的打包贴图。从中仅提取实际使用的七组材质、18 张共享图片（约 10.7 MB），保留原始像素尺寸，未引入整个 184 MB 工程或其他枪械／人物资源。原贴图的 B 通道金属度转 Unity R，G 通道粗糙度取反转 A 光滑度；颜色按 sRGB、数据及法线按线性导入。FBX UV 与材质槽对应关系明确绑定到枪身、瞄具、扳机和弹匣，匹配的两份枪共享资源。源材质中第一把的法线曾误接到 Normal Map Strength，导入时按法线图接到 Unity normal 输入，不复制该错误节点接线。

实体手默认重新启用 Skin_L／Skin_R，替代骨骼外观而非叠加蒙皮。另发现失追皮肤 Skin_Red 仍是透明队列、alpha≈0.455、ZWrite Off；现改为不透明、alpha=1、ZWrite On 并恢复深度及阴影 pass。正常皮肤原有光滑度 10.66 超出 PBR 合法范围，改为 0.25；去掉误导性的 Dither 文件名并保留 GUID。提示圈／箭头保持独立透明覆盖层，不改变实体手深度行为。

设计依据：[Meta Hand Grab Use 的抓握／使用职责分离](https://developers.meta.com/horizon/reference/interaction/v78/interface_oculus_interaction_hand_grab_i_hand_grab_use_delegate/)、[Ultraleap Physical Hands 的刚体、碰撞与小部件限制](https://docs.ultraleap.com/xr-and-tabletop/xr/unity/plugin/features/physical-hands.html)、[Unity 有限驱动力和关节参考](https://docs.unity3d.com/6000.0/Documentation/Manual/class-ConfigurableJoint.html)、[CoACD 碰撞近似](https://github.com/SarahWeiii/CoACD)。没有把 H3VR 等商业作品的内部实现当作已公开算法，也没有假定控制器的按键输入等同于视觉手追。

源 FBX SHA-256：`MARATHON_GUN_BUNDLE_1.fbx` = `b46792eac27cf4b08ff4e288fde809ba1dc87a75d9eb8210ad0a3b1c80fc8e21`；`MARATHON_GUN_BUNDLE_2.fbx` = `329486e50f6381432d25bdc05f9b437f0f67f5518e80b3e67896c3b260e863c8`。原 FBX 本地副本置于忽略版本控制的 `data/raw/guns`。

重新检查 PhysFlow 的[官方仓库](https://github.com/zhuomanliu/PhysFlow)、GitHub Releases、[项目页](https://zhuomanliu.github.io/PhysFlow/)和其链接的九个公开数据 ZIP 的完整中央目录：未找到优化后的 sim.json／sim.pth／checkpoint；每份 ZIP 确实包含 49 张 images_generated 指导帧。公开的 iteration_30000/point_cloud.ply 是高斯重建结果，与材料参数优化 checkpoint 不同。本地原先缺少指导帧不代表作者没有发布指导数据。这个结论限于已检查的公开位置；没有推断作者私有文件不存在。当前 Mac 无 NVIDIA CUDA，未伪造或手调“训练后”材料值。公开指导帧可用于后续 CUDA 机器的官方训练流程。

验证证据位于忽略版本控制的 `artifacts/reports/pistol-lab`。编辑器合成输入验证与头显实测分开：未以批处理通过声称真实遮挡下的食指追踪、纯摩擦握持成功率或设备帧率已经验收。

最终验证：71 项编辑器运行检查通过，包括两手／两种柔顺枪捕获和释放、食指独立目标、真实失追事件释放、实体食指连续两次扣动、中指误触不发射、反冲动量、120 m/s 弹丸对 1 mm 目标的扫掠、初始重叠、嵌入后跟随、弹丸池上限及反复停用复用。正常与失追皮肤均通过 opaque／alpha=1／ZWrite=1 检查，每手一个可见皮肤与阴影源。场景和保留预制体无缺失脚本及失效引用；54 项高斯基线哈希未变。清理临时代码后，完整资产与排除全部 20 个高斯资产的 LAB-only 两种 visionOS 脚本／IL2CPP 导出均为零构建错误，并恢复原高斯资产及元数据。完整版工程位于 `artifacts/builds/visionOS`，LAB-only 位于 `artifacts/builds/visionOS-lab-only`。未进行头显安装或 Xcode 真机签名运行，不把导出成功作为设备帧率、传感器遮挡或纯摩擦握持成功率的证明。

用户随后明确要求半透明手皮、不向其他物体投影，取代上文不透明皮肤方案：正常及失追材质均改为 alpha=0.5 的透明混合、ZWrite Off；保留单层蒙皮，预制体与运行时的实体皮肤／骨骼阴影投射均关闭。接收其他场景光照及阴影的行为保留。


## 2026-10-22 runtime follow-up

The interactive rig advances PhysX at 360 Hz. Each MPM fixed step submits all source/CFL substeps and previously synchronized reaction readback whenever tracked bones existed, including distant hands. This is a simulation and synchronization bottleneck. The eight undisturbed zero-gravity models now elide exactly zero evolution until contact; gravity, moving boundaries, replay and disturbed states retain full stepping. Conservative swept bounds reject distant shapes before allocating contact work. Exact identity strain skips redundant SVD; zero integer atomic adds are omitted. G2P reuses compact Morton-group grid stencils in threadgroup memory, falls back to the full stencil when needed, and skips only exactly zero grid-velocity updates. A prescribed moving-contact comparison against the global G2P stencil produced zero difference across the complete particle state. All 54 Gaussian baseline hashes remain unchanged.

Contact geometry now samples Rigidbody physics poses rather than interpolated display poses. Shapes cover the upcoming common integration interval. Reaction readback includes impulse-weighted contact positions, allowing angular pursuit to obey the same unilateral normal as translation. A box-centre contact now has a defined nonzero normal. Finite-mass coupling remains partitioned, with individual-body mobility rather than the full articulated contact operator; this limitation has not been solved by the changes above. Two-handed synthetic board pursuit, withdrawal and thumb checks passed. Ten-model synthetic hand pokes produced finite reactions and peak bone speed below 2.4 m/s, but this is not a proof of acceptable deformable-surface penetration or sensor behavior. Momentum error in the mirrored finite-mass coupling fixture stayed below 2.6e-7 kg m/s.

Active-contact editor timings on Apple M5 / 32 GB remain approximately 80 ms for carnation, 111 ms for alocasia and 197 ms for sand per 10 ms of simulated time. These cases remain substantially slower than real time. Idle elision does not establish real-time active interaction. No Gaussian count, resolution, material stiffness, elapsed simulation time or source step limit was reduced to conceal the remaining cost. Device timings are pending: the physical Vision Pro was unavailable during the inspection. The on-device Profiler markers separate MPM work, contact wait and Gaussian sort.

Normal and tracking-loss skin retain 50% opacity and no shadow casting. Toon lighting and a two-pixel silhouette hull provide the cartoon appearance. The authored skeleton already follows the physical bones through explicit RigMapper bindings; those bindings and terminal markers are preserved. A public URP stencil bit keeps skeleton pixels above skin while retaining scene depth, including Gaussian occlusion. Reference and ghost skins stay disabled.

Rounds are 12 mm diameter / 30 mm length cylinders with wider trails. Virtual round parameters are 25 g and 24 m/s, yielding 0.6 N s launch momentum with equal opposite recoil. In editor tests the spring target chest moved 36.6 mm; 100 g, 500 g, 1 kg and 2 kg cubes received approximately 6, 1.2, 0.6 and 0.3 m/s respectively. Physical trigger travel remains supported. Independent tracked-index use assistance requires a nearby gripping palm, near-trigger index location, closure and movement along the pull direction. Acquiring with a curled index does not fire; release/rearm hysteresis and actual tracking validity remain required. Finite trigger actuation applies equal/opposite internal forces or torques without teleporting fingers. The final lab regression covered both hands, both trigger types, repeated pulls, non-index rejection, recoil, ordinary props, embedded rounds, pool bounds and repeated activation.

[PhysFlow](https://arxiv.org/abs/2411.14423) refines initialized material parameters through video-guided differentiable MPM. The nine local MPM assets still contain author initialization. Training and Windows setup were explicitly deferred by the user on 2026-10-22. Material optimization alone does not provide the missing articulated rigid/deformable contact solve. The [convex MPM/rigid-body coupling work](https://arxiv.org/abs/2503.05046) provides a relevant future solver reference; its quasi-Newton method is not implemented here. Trigger design follows the separation of grip and secondary finger use documented by [Meta Hand Grab Use](https://developers.meta.com/vr/reference/interaction/v85/interface_oculus_interaction_hand_grab_i_hand_grab_use_delegate/) and the physical small-part limitations documented by [Ultraleap](https://docs.ultraleap.com/xr-and-tabletop/xr/unity/plugin/features/physical-hands.html).

Verification logs, timings and images are ignored under `artifacts/reports/headset-runtime-2026-10-22`. Temporary regression and bake code was removed from Unity after verification. Editor validation and Xcode export do not constitute headset performance or tracking acceptance.
