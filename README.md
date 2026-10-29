# 3DGS Auto Collider Toolkit

`com.3dgs.physics-toolkit` 0.1.0 builds Unity `BoxCollider` components from a 3D Gaussian Splatting PLY file.

Rendering is not included. The scene is drawn by [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting). Colliders are built from Gaussian centers only.

## Contents

1. [Project Overview](#project-overview)
2. [Getting Started](#getting-started)
3. [Tutorial](#tutorial)
4. [Performance & Benchmark](#performance--benchmark)
5. [Future Work](#future-work)

## Project Overview
<img width="1429" height="858" alt="image" src="https://github.com/user-attachments/assets/1f81175c-620e-45a2-b2a5-76a665f35f05" />
<img width="1433" height="829" alt="image" src="https://github.com/user-attachments/assets/8ed1b920-2c85-4327-9718-3f0c30b88657" />


A 3DGS PLY stores Gaussian positions and appearance. It does not store a Unity collision shape. This toolkit bins those centers into a voxel grid and creates `BoxCollider` components so a `Rigidbody` can collide with the rendered scene.

Two datasets are used with the toolkit:

- `garden.ply` is the garden scene distributed with [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting). This repository does not host that file.
- `partyroom.ply` was trained for this project for 30,000 iterations from an image set used by the original 3D Gaussian Splatting paper. This repository does not host the images, the training log, or the PLY file.

Collider generation has two modes. The default is `Legacy`. `Surface` is the other mode.

| Mode | What it builds |
| --- | --- |
| Legacy | An axis-aligned `BoxCollider` for every voxel whose Gaussian-center count reaches `Density Threshold`. Interior voxels are included. |
| Surface | Keeps voxels that fail the density test on at least one of the six faces, then fits a rotated box with PCA. |

PCA uses only the centers inside one surface voxel. The covariance is the population covariance, divided by `N`. The eigenvector of the smallest eigenvalue is the surface normal. A voxel is kept only when it passes the point-count, eigenvalue-ratio, thickness, and outlier checks. PCA does not succeed for every surface voxel.

Failed voxels follow two more paths. All three results are Unity `BoxCollider` components. The names record how the box was built.

| Group | `ColliderBuildKind` | When it is used |
| --- | --- | --- |
| CyanPca | `Pca` | PCA passed for that surface voxel. |
| GreenNeighbor | `Neighbor` | PCA failed, and a neighboring successful plane fit the points. |
| OrangeFallback | `Fallback` | PCA and the neighbor fit both failed, so the voxel cube is used. |

`Collision Debug Mode` is a Play Mode view. It draws the `BoxCollider` that was hit and the contact face. It does not rebuild colliders. The optional `Disable OrangeFallback Physics (Experimental)` control is off by default. While it is on, and only during Play Mode, it disables `BoxCollider.enabled` on the `OrangeFallback` group.

```text
3DGS PLY
  -> SplatImporter
  -> SplatData positions (-x, -y, -z)
  -> voxel grid
  -> DensityKernel.compute
  -> Density Threshold
  -> Legacy or Surface
  -> BoxCollider
  -> Unity Physics
```

On the Surface path the split is: PCA success goes to `CyanPca`, a neighbor-plane success goes to `GreenNeighbor`, and the remaining exposed voxels go to `OrangeFallback`.

Per-Gaussian rotation, scale, and opacity are loaded for rendering. The collider builders in this repository do not use them.

## Getting Started

### Unity versions

| Role | Version |
| --- | --- |
| Version declared by `package.json` | Unity `2022.3`, release `47f1` |
| Editor used to develop this package | Unity 2022.3.47f1 |
| Editor where this package was also run | Unity 6.6 (`6000.6.7f1`) |

Unity 2023 and later releases were not tested one by one. A version that is not in the table above is not a verified target.

### Render pipeline

URP is not required. The development project and the Unity 6.6 project both have no custom render pipeline asset assigned, so they use the Built-in Render Pipeline.

`GaussianSplat.shader` and `CollisionDebugWire.shader` include `UnityCG.cginc`. They are Built-in shaders. The recommended scene path does not use those shaders for the Gaussian view. It uses `GaussianSplatRenderer` from UnityGaussianSplatting. This repository does not add a URP `Renderer Feature` or change a render-pass event.

### UnityGaussianSplatting

Install this commit in the same project. The package name is `org.nesnausk.gaussian-splatting`. The renderer version noted by this package's documentation is `1.1.2`.

```json
"org.nesnausk.gaussian-splatting": "https://github.com/aras-p/UnityGaussianSplatting.git?path=/package#ebd76138a09b68340db3c74b3867f0451a53b6cd"
```

`GaussianPhysicsObjectEditor` binds the renderer shaders and compute shader by GUID from that commit:

- Splats `ed800126ae8844a67aad1974ddddd59c`
- Composite `7e184af7d01193a408eb916d8acafff9`
- Debug Points `b44409fc67214394f8f47e4e2648425e`
- Debug Boxes `4006f2680fd7c8b4cbcb881454c782be`
- Splat Utilities `ec84f78b836bd4f96a105d6b804f08bd`

That install also brings in the renderer's own packages: `com.unity.burst` 1.8.8 or newer, `com.unity.collections` 2.1.4, and `com.unity.mathematics` 1.2.6. This toolkit's `package.json` does not declare them. Unity Package Manager does not install Git dependencies written inside a package's own `package.json`, so the consuming project must add the renderer line itself.

### Install this toolkit

`package.json` is at the root of this repository. The package name is `com.3dgs.physics-toolkit`.

Git URL for this private repository:

```json
"com.3dgs.physics-toolkit": "https://github.com/HHW9944/3dgs-auto-collider-urp-dev.git"
```

A private Git URL needs credentials that Package Manager can use. A local folder does not:

```json
"com.3dgs.physics-toolkit": "file:C:/absolute/path/to/3dgs-auto-collider-urp-dev"
```

Use `/` in the path. On Unity 2022.3, a `file:` path that leaves the project, such as `file:../...`, can be rejected. An absolute path avoids that. Copying this folder to `Packages/com.3dgs.physics-toolkit` can create the manifest entry automatically.

Do not install this package into a project that already contains the same scripts under `Assets`. The duplicate `SplatImporter` and component types will collide.

### Hardware

Collider generation dispatches `DensityKernel.compute`. The GPU must support compute shaders and the graphics API selected below. The generation log does not record a GPU name, VRAM size, or driver version, so this document does not state a minimum card.

### Graphics API Configuration

The checked Windows Player settings turn automatic API selection off. The list is Direct3D12 first and Direct3D11 second. Vulkan is not in that list and was not given a separate run for this document.

Use one of Direct3D12 or Vulkan at the top of the list. The project does not need both.

1. Open `Edit → Project Settings → Player → Other Settings → Rendering`.
2. Clear `Auto Graphics API for Windows`.
3. In `Graphics APIs`, add `Direct3D12` or `Vulkan` if it is missing.
4. Drag that API to the top of the list.
5. Restart the Unity Editor if the active API does not change. The editor title shows the API in use, such as `DX12`.

## Tutorial

The labels below are the strings in this repository.

### 1. Import 3DGS PLY
<img width="110" height="137" alt="image" src="https://github.com/user-attachments/assets/d5187983-d1c1-43f3-b437-2982db751d3c" />
<img width="588" height="152" alt="image" src="https://github.com/user-attachments/assets/7f7ebae9-bff7-436f-a459-25c5b440c160" />

Copy the `.ply` into the project's `Assets` folder. `SplatImporter` is a `ScriptedImporter` for the `ply` extension. It creates a `SplatData` asset and stores positions as `(-x, -y, -z)`. The importer applies to every `.ply` in the project, so do not keep mesh PLY files beside a Gaussian PLY.

For the renderer path, create a `GaussianSplatAsset` from the same file with `Tools/Gaussian Splats/Create GaussianSplatAsset`. That menu belongs to UnityGaussianSplatting. The asset keeps the file's original coordinates.

### 2. Create GaussianPhysicsObject
<img width="1022" height="75" alt="image" src="https://github.com/user-attachments/assets/4bc55c6a-9e8b-4756-b3f3-c1fa5d255caf" />

Open `Gaussian Tool/Collider Generator`. The window title is `Gaussian Physics Tool`.

1. Assign `Splat Data`.
2. Enable `aras-p 렌더러 사용`.
3. Assign `Gaussian Splat Asset`.
4. Press `가우시안 오브젝트 생성 및 물리 적용`.

The created object is named `GaussianPhysicsObject_<splat name>`. Its children are `GaussianVisual/GaussianSplatRenderer` and `GaussianPhysics/GeneratedColliders`. The renderer local scale is `(-1, -1, -1)`. Colliders stay in `SplatData` local space.

Leaving `aras-p 렌더러 사용` off builds a `GS_<name>` object with the package's Built-in `GaussianSplat` material and runs Legacy generation only. The renderer path above is the one used for Surface mode.

### 3. Configure Collider Mode
<img width="637" height="302" alt="image" src="https://github.com/user-attachments/assets/f01e6832-757c-45b7-9991-12681e04a044" />

`Collider 모드` is shown only while `aras-p 렌더러 사용` is on. The default is `Legacy`.(But 'Surface' is recommended)

`생성 시 자동 수평 보정` appears only for Legacy. Its default is on. It uses `GroundPlaneEstimator` to rotate the root before the boxes are built.

Surface shows the notice `Surface 모드는 자동 수평 보정을 적용하지 않습니다. 원본 PLY 기울기를 유지합니다.` Surface does not apply that rotation. If the root still has a rotation, use `Reset Leveling` on the `GaussianPhysicsObject` inspector, then generate again.

`Grid Size` and `Density Threshold` are on the `DensityCompute` component under `GaussianPhysics`, not in the generator window. The defaults are Grid Size `(64, 64, 64)` and Density Threshold `5`.

`VoxelSize` is the largest of each bounds axis divided by the matching grid count, and it is at least `0.01`.

### 4. Generate Colliders

The generator button creates the hierarchy and the first set of colliders. To build them again after changing the grid or the threshold, select `GaussianPhysicsObject` and press `Hierarchy 구성 및 Collider 생성`.

Legacy writes `Voxel_<x>_<y>_<z>` boxes under `GeneratedColliders`. Surface writes boxes under `CyanPca`, `GreenNeighbor`, and `OrangeFallback`. Regenerating deletes the children of `GeneratedColliders` and builds the selected mode again. The boxes are not triggers, and this code does not add a `Rigidbody`.

### 5. Visualize Generated Colliders

Select `GaussianPhysicsObject` in the Scene view. With `Draw Collider Mode Gizmos` enabled, the gizmo colors are:

- CyanPca, cyan `(0.1, 0.85, 1)`
- GreenNeighbor, green `(0.15, 0.95, 0.35)`
- OrangeFallback, orange `(1, 0.55, 0.1)`

A box with no `ColliderShapeMarker`, including a Legacy voxel, uses the orange color. Drawing stops after `Collider Gizmo Budget` times two. The default budget is `2000`, so the stop point is `4000` boxes.

The Console line that starts with `[SurfaceCollider]` reports the surface-candidate count, PCA successes, neighbor successes, fallback count, and the reject reasons.

### 6. Test Physics with Rigidbody

Use `Gaussian Tool/Create Collision Test Objects`. It creates `CollisionTestGround` and a `CollisionTestSphere` with a `Rigidbody` in the open scene. Place the sphere over the generated colliders and enter Play Mode.

The ground box can receive the first contact. Disable `CollisionTestGround` while testing the Gaussian colliders.

### 7. Enable Collision Debug Mode
<img width="572" height="36" alt="image" src="https://github.com/user-attachments/assets/fa3c6f51-94ee-497a-b530-aa108cc15ff1" />
<img width="620" height="907" alt="image" src="https://github.com/user-attachments/assets/ecc85389-9acf-4f5d-bc55-fb0b082f3b8a" />


On the `GaussianPhysicsObject` inspector, press `Collision Debug Mode 컴포넌트 추가`. The component menu is `3DGS/Collision Debug Mode`. `Enable Collision Debug Mode` starts off.

`Ball Rigidbody` is the single tracked body in this repository. Assign the test sphere, or leave it empty. An empty slot still attaches relays during Play Mode to rigidbodies outside the generated collider root. The field tooltip says an empty slot does not track, which does not match that behavior.

`Show In Game View` draws, in the Game view, the hit box, a yellow contact face, a red contact point, and a light-blue normal. `Keep Hit Colliders Visible` keeps those marks after the body leaves the box. `Clear Hit Collider History` removes the kept marks.

`[CollisionDebug]` is written to the Console. A Game-view recording does not show that log. The log line contains the box name, build kind, face label, contact point, normal, and tracked velocity.

`Show Cyan Pca`, `Show Green Neighbor`, and `Show Orange Fallback` draw group wires in the Scene view. `Show Only Last Hit Collider` defaults to on, and while it is on those three group wires are skipped.

## Performance & Benchmark

The numbers below are generation records, not Play Mode frame time. `ProcessingTime(ms)` is the stopwatch around the density dispatch and the collider build together. It is not a physics-step measurement.

`ReductionRate(%)` is `(1 - ColliderCount / OriginalPoints) * 100`. It compares collider count with Gaussian count. It is not a runtime speedup.

This repository does not contain the log. The rows were read from a development project file, `Assets/GaussianPhysicsStats.csv`. The file has no collider mode, grid size, density threshold, GPU, or repeat protocol. Legacy and Surface are therefore not split. Accuracy against the rendered surface was not measured.

| FileName | Rows | OriginalPoints | ColliderCount | ProcessingTime(ms) | ReductionRate(%) |
| --- | ---: | ---: | --- | --- | --- |
| Tree | 4 | 279,910 | 12,684 | 137–156 | 95.47 |
| scene | 5 | 459,281 | 4,319–4,716 | 100–257 | 98.97–99.06 |
| garden | 20 | 4,325,323 | 0–2,299 | 299–1,304 | 99.95–100.00 |

The `garden` range includes a row whose collider count is `0`. The `scene` collider count is not the same on every row. `partyroom.ply` does not appear in this log.

## Future Work

The current builders do not consume per-Gaussian scale or rotation. Using those values for box orientation and size is open work. A Gaussian-shape surface estimate is not a generation mode in this repository.

Also open:

- measuring how far the boxes sit from the rendered surface
- reducing the number of `BoxCollider` components
- measuring Play Mode physics cost
- repeating generation on scenes other than the log above, including the relationship between voxel resolution and build time
