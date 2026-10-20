# Paper-scene fidelity audit (2026-10-20)

The headset demo and a paper reproduction are different quality targets. The former must keep hand-to-image latency low; the latter must preserve the released scene, camera, boundary conditions, and material parameters. They share one Unity scene and one Xcode export, but the `ROOM` and `PAPER` controls expose the high-coverage reconstruction and scripted reference motion.

| Scene | Source GS | GS passing local import filters | Imported GS after this change | Interactive prefix | Solver particles |
| --- | ---: | ---: | ---: | ---: | ---: |
| PhysFlow carnation | 1,037,279 | 547,834 | 547,834 | 123,615 | 6,422 |
| PhysFlow jam | 702,420 | 484,283 | 484,283 | 34,316 | 9,944 |
| PhysFlow sandcastle | 1,761,047 | 1,346,583 | 1,346,583 | 252,546 | 21,579 |

The older 393,216-splat scene cap discarded static scene data before runtime. The paper-facing import now retains every splat passing the local moving-part/opacity/background filters for these three scenes. The interactive prefix is still a deliberate render-level selection; `ROOM` draws the complete imported scene, while `PAPER` also runs the source scripted boundary conditions. This does **not** imply matching the published results.

Remaining gaps to a controlled reproduction:

The hand-interaction material override table has been removed. The runtime now reads the
released material family, Young's modulus, yield/viscosity, density, and boundary conditions
from each PhysFlow config for both replay and hand interaction. This restores provenance,
but those configs are LLM initializations; the paper's optical-flow-refined parameters are
not available in these assets. No claim of learned or matched material response should be
made from them. The carnation's 0.7 log-strain fracture threshold is a clearly separated
demo-only choice, and the bulldozer's brick segmentation is procedural, not a learned
assembly model.

The XR hand uses a fully tracked visible rig and kinematic collision bones, one per joint.
This avoids the observed dynamic-finger oscillation and skin penetration during rapid
motion. The kinematic colliders move PhysX objects and provide velocity/contact shapes to
the Gaussian solver. The MPM reaction is measured but cannot displace the tracked hand;
it acts through the simulated object. This is not haptic feedback or a collision-constrained
visual hand. The desktop rapid-motion probe passes, but it allows up to 4 cm transient
collision-bone separation at its stress speed; observed maximum was about 2.4 cm. Actual
thumb-tip accuracy, object contact count, and latency still require headset measurements.
One pre-fix headset log (`Logs/device/telephone-1653.csv`) recorded 916 frames:
median thumb-tip errors 30.35 mm left and 30.73 mm right, versus 4.66 mm and
1.92 mm for the index tips. The current build retargets the final thumb bone
to the visionOS ThumbTip joint; its post-fix error and collision-proxy alignment
have not yet been verified on the headset.

1. The PhysFlow release configuration contains initial material values. Its video-guided refined values are not in these imported assets. Compare against the authors' final renders only after obtaining or reoptimizing those values.
2. The Unity MLS-MPM solver uses a sparse sub-cell particle representation. The carnation has 6,422 particles; the source moving mask contains 99,145 Gaussians. Binding many splats to one particle is essential to mobile runtime but changes high-frequency deformation and contact. A paper-fidelity offline reference must compare trajectories at the same particle sampling and time step.
3. The renderer currently keeps DC plus degree-1 spherical harmonics. The original trained Gaussians contain higher-order coefficients. Novel-view appearance and translucent petals will differ even when geometry matches.
   Splats outside PhysFlow's released moving mask are currently static even when they
   visually belong to the object. `ROOM` reveals those residuals. A full object requires
   an audited part mask and dense motion binding, not merely a higher display budget.
4. The metric scale, interaction forces, and display camera are demo choices. They are not recovered from the paper. The author-facing scene camera is needed for pixel-level comparison. The default headset view is intentionally an instrumented mixed-reality view.
5. The PhysDreamer image supplied by the user shows a red rose. The public PhysDreamer archive currently lists alocasia, carnation, telephone, and hat; its red-rose GS and optimized physical field are not available in that archive. The imported pink carnation is therefore a *different released subject*, not a reconstruction of that red rose.

The physics checks should report at least rest-to-poke displacement, release trajectory, contact penetration, detached fraction, screen-space frame timing, and side-by-side calibrated-camera images. A pass on finiteness alone is insufficient. Do not call the present headset demo a paper reproduction until the scene, material, and trajectory comparisons pass.

Primary sources: [PhysDreamer paper](https://physdreamer.github.io/papers/physdreamer_paper.pdf), [PhysDreamer official code](https://github.com/a1600012888/PhysDreamer), [PhysDreamer released archive](https://huggingface.co/datasets/YunjinZhang/PhysDreamer/tree/main), [PhysFlow official code and data instructions](https://github.com/zhuomanliu/PhysFlow), [PhysFlow supplemental comparisons](https://openaccess.thecvf.com/content/CVPR2025/supplemental/Liu_Unleashing_the_Potential_CVPR_2025_supplemental.pdf).
