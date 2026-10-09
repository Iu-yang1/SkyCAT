---
title: 中文文档
nav_order: 7
has_children: true
permalink: /zh-cn/
---

**语言：** [English](../index.md) | 简体中文

# SkyCAT 1.7

## 概述

SkyCAT 是一个开源、跨平台的 CAT（Computer Aided Transceiver）控制引擎。它既可以作为 **.NET** 类库 **SkyCAT.dll** 集成到其他软件中，也可以通过命令行程序 **skycatd.exe** 使用。skycatd 提供与 Hamlib
[rigctld.exe](https://hamlib.sourceforge.net/html/rigctld.1.html) 兼容的 TCP 控制接口。

SkyCAT 与现有 CAT 引擎（例如 [OmniRig](https://dxatlas.com/OmniRig/)、[Hamlib](https://hamlib.github.io/) 和 [FLRig](https://github.com/w1hkj/flrig)）有一些共同点：

- 与 Hamlib 类似，它支持通过 TCP 进行远程控制；
- 与 OmniRig 类似，它采用开放式架构，可以通过编写文本形式的电台命令集文件来增加新机型支持；
- 与 FLRig 类似，它不会持续轮询电台。客户端在需要读取频率、模式等状态时显式发起请求，因此控制过程更直接、响应更快。

当前 fork 还增加了 SkyRoof / IC-9700 场景所需的 WSJT-X 兼容代理、CTCSS、
PTT fail-safe、原生 IC-9700 频谱流，以及 **Remote Control Switch 独立辅助端口 4537**。
这些功能不一定存在于上游 VE3NEA 的发布版本中。

### RS-BA1 / SkyRoof / Remote Control Switch 并行使用

默认端口分别是 CAT **4532**、WSJT-X **4534**、频谱 **4535**、
Switch 辅助控制 **4537**。所有服务共享 SkyCAT 的串口链路，
Switch 端口不允许修改卫星频率、PTT 或 VFO，避免与 SkyRoof 冲突。

完整配置、Windows 端口占用排查与 CI-V 指令列表见
[skycatd 中文使用文档](skycatd.md#ic-9700-remote-control-switch-集成)。

## 工作模式

许多电台在不同工作模式下，对同一个动作使用不同的 CAT 命令。例如，卫星 SAT 模式和普通 VFO 模式下，“设置频率”的命令路径可能不同。

SkyCAT 会显式设置当前工作模式，因此能够选择正确的命令集。目前支持：

- **Duplex（双工）**；
- **Split（异频）**；
- **Simplex（单工）**。

详细定义和命令集结构请参阅 [SkyCAT 命令集文件格式](commandset-format.md)。

## 支持的电台

SkyCAT 通过 `Rigs/*.json` 命令集文件支持不同电台。当前仓库已经附带多种 ICOM、Yaesu 和 Kenwood 机型的定义。

要增加新电台，一般只需要文本编辑器和可用于实机测试的电台。命令集格式请参阅
[SkyCAT 命令集文件格式](commandset-format.md)。

## 中文文档

- [SkyCAT 类库](library.md)
- [skycatd.exe 命令行使用说明](skycatd.md)
- [SkyCAT 命令集文件格式](commandset-format.md)
- [源代码](source.md)
- [下载](download.md)
- [IC-R7000 SkyCAT 驱动笔记](IC-R7000-SkyCAT-Notes.md)
