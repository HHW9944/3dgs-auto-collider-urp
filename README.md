# 3DGS Game Asset Pipeline for Unity URP

[![Unity](https://img.shields.io/badge/Unity-2022.3%20LTS-black?style=flat-square&logo=unity)](https://unity.com/)
[![Render Pipeline](https://img.shields.io/badge/Render%20Pipeline-URP-blue?style=flat-square)](https://unity.com/srp/Universal-Render-Pipeline)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

This project provides a comprehensive implementation for importing, optimizing, and rendering 3D Gaussian Splatting (3DGS) assets within the Unity Universal Render Pipeline (URP). It is designed to bridge the gap between research-oriented splat generation and production-ready game engine integration.

---

## 📸 Showcase & Preview

| Real-time 3DGS Rasterization | Edit-time Scene Preview |
| :---: | :---: |
| ![3DGS Render Showcase](docs/images/render_demo.gif) | ![Edit-time Preview](docs/images/editor_preview.png) |
| *Photorealistic radiance field rendering in Unity URP* | *Real-time feedback within Unity Scene View* |

---

## 📌 Project Overview

The **3DGS Game Asset Pipeline** focuses on high-performance rasterization and memory-efficient storage of Gaussian splats. By leveraging GPU compute shaders and custom URP rendering features, this pipeline allows developers to use photorealistic radiance fields as standard game objects.

---

## ✨ Features

- **Custom Asset Importer**: Automated conversion of `.ply` and `.splat` files into optimized Unity sub-assets.
- **URP Integration**: Full support for the Universal Render Pipeline via a dedicated `ScriptableRenderFeature`.
- **Compute-Based Radix Sort**: High-speed, per-frame GPU sorting to handle semi-transparent splat blending.
- **Splat Compression**: Quantization of spherical harmonics and covariance data to reduce VRAM footprint by up to **70%**.
- **Frustum Culling**: Efficient CPU/GPU culling to skip rendering of splats outside the camera view.
- **Edit-time Preview**: Real-time feedback within the Unity Scene View for technical artists.

---

## 🛠 System Architecture

The pipeline is divided into three distinct layers to ensure modularity and performance:

```text
+-------------------------------------------------------------------+
|                     1. Data Layer (Import)                        |
|   .ply / .splat  -->  Custom ScriptableObject  --> GraphicsBuffer  |
+-------------------------------------------------------------------+
                                  |
                                  v
+-------------------------------------------------------------------+
|                     2. Processing Layer (Compute)                 |
|   Culling Kernel (Frustum)  -->  Sorting Kernel (GPU Radix Sort)  |
+-------------------------------------------------------------------+
                                  |
                                  v
+-------------------------------------------------------------------+
|                  3. Presentation Layer (Shading)                  |
|   URP Fragment/Vertex Shader  -->  2D Gaussian Alpha Blending     |
+-------------------------------------------------------------------+

### **Data Layer (Import & Storage)**

- **Data Layer (Import & Storage)**'Dource data is processed through a custom ScriptableObject format. During import, the pipeline calculates bounding volumes and performs initial data normalization. Splats are stored in GraphicsBuffer objects at runtime.

** Processing Layer (Compute): The GPU manages the heavy lifting through two primary compute kernels:

Sorting Kernel: Uses a bitonic or radix sort to order splats based on their distance from the camera plane.

Culling Kernel: Filters out splats based on the camera frustum and optional occupancy masks.

** Presentation Layer (Shading): A specialized URP Fragment/Vertex shader performs the final rasterization. It calculates the 2D footprint of each 3D Gaussian and handles alpha-blending logic according to the radiance field mathematical model.



