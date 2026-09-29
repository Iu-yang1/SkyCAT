---
title: SkyCAT 类库
parent: 中文文档
nav_order: 2
---

**语言：** [English](../library.md) | 简体中文

# SkyCAT 类库

SkyCAT 也可以作为 **.NET** 程序集使用。核心代码没有依赖特定操作系统的平台实现，因此可以运行在支持 **dotnet** 的平台上，包括 Windows、Linux 和 macOS。

程序集中的主要类是 `CatCommandSender`。下面是一个基本示例：

```csharp
var sender = new CatCommandSender();

sender.SelectRadio("IC-9700");
sender.SerialPort.PortName = "COM2";
sender.SerialPort.BaudRate = 115200;
sender.SerialPort.Open();
sender.SetupRadio(OperatingMode.Simplex);

var frequency = sender.SendCommand(CatCommand.read_rx_frequency);

sender.SendCommand(CatCommand.write_rx_mode, "CW");
```

基本使用流程是：

1. 创建 `CatCommandSender`；
2. 使用 `SelectRadio()` 选择电台命令集；
3. 配置并打开 `SerialPort`；
4. 使用 `SetupRadio()` 设置 Duplex / Split / Simplex 工作模式；
5. 通过 `SendCommand()` 发送标准化的 SkyCAT CAT 操作。

实际项目中的完整使用方式可以参考本仓库
[skycatd.exe 源代码](https://github.com/Iu-yang1/SkyCAT/tree/master/skycatd)。

如果你正在编写自己的卫星跟踪、电台遥控或多普勒控制软件，优先使用 SkyCAT 类库通常比自己直接维护不同机型的原始 CI-V / CAT 字节序列更方便。
