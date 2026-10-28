# FanControl.NCT6779D

**[中文说明](#中文说明) | [English](#english)**

---

<a name="中文说明"></a>
## 中文说明

**FanControl.NCT6779D** 是专为 [FanControl](https://github.com/Rem0o/FanControl.Releases) 深度定制的**独立硬件级风扇控制插件**。

专门用于彻底解决 **Nuvoton NCT6779D**（以及 NCT677x / NCT679x 系列 Super I/O 芯片，广泛存在于各类 **X99 主板**、华南金牌、精粤、科脑等平台）将 `SYS_FAN`（`PWM 1`）默认锁死在 **DC 调压模式**、导致 4 针 PWM 风扇无法调速且常年 100% 满速狂转的问题。

---

### 核心功能与特性

1. **硬件级 PWM 模式自动切换与守护**：
   - 插件初始化及调速时，自动向硬件监控寄存器 **Bank 0, Reg `0x004`** 写入 `0x00`（清除 Bit 0），将 `SYS_FAN (PWM 1)` 电气输出模式强制设定为 **PWM 模式**。
   - **防睡眠重置机制**：在后台定期检查该寄存器。当 Windows 经历睡眠/休眠（S3/S4）被唤醒、BIOS 试图恢复默认 DC 模式时，插件将自动检测并恢复为 PWM 模式。
2. **专属原生 PWM 控制卡片 (`ControlSensor`)**：
   - 在 FanControl 界面中直接生成专属控制卡片：`NCT6779D SYS_FAN Control (PWM)`。
   - 支持 0% ~ 100% 完整 PWM 线性占空比调节（直接写入 Bank 1, Reg `0x0109`），并自动切换为手动控制模式（Reg `0x0102` = `0`）。
3. **高精度转速读取 (`FanSensor`) 与自动配对**：
   - 采用 Nuvoton 官方硬件计数器算法，提供高精度的风扇实时转速 `NCT6779D SYS_FAN`。
   - 实现了 `IPluginControlSensor2.PairedFanSensorId`，FanControl 会自动将控制卡片与该转速表配对显示。
4. **ISA 总线互斥锁安全同步**：
   - 插件通过反射共享 FanControl 内部已加载的底层 `LpcPort` 与 `Mutexes.WaitIsaBus` / `ReleaseIsaBus`。
   - 无论是新版 FanControl (基于 `PawnIO` 驱动) 还是旧版 (基于 `WinRing0`)，读写寄存器时均受到全局互斥锁保护，绝不与 FanControl 默认的传感器轮询发生 Bank 冲突或总线碰撞。

---

### 支持的芯片型号列表 (Supported Chips)

本插件经过寄存器定义对齐，代码上支持 Nuvoton NCT677x 系列以及延续其架构的 NCT679x / NCT670x 芯片：

| 芯片型号 | Chip ID | 芯片 Revision | 说明 |
| :--- | :--- | :--- | :--- |
| **NCT6779D** | `0xC5` | `0x6x` | **仅测试NCT6779D**（大量 X99 / Z97 / B85 主板标配） |
| **NCT6776F** | `0xC3` | `0x3x` | 经典 LGA1155 (Z68/Z77/H61/H77) 主板标配 |
| **NCT6771F** | `0xB4` | `0x7x` | 早期 LGA1156 / AM3 平台 |
| **NCT6791D** | `0xC8` | `0x03` | LGA1150 (Z97/H97) 主板 |
| **NCT6792D / NCT6792D-A** | `0xC9` | `0x11` / `0x13` | LGA1151 系列 |
| **NCT6793D** | `0xD1` | `0x21` | LGA1151 (Z170/B150/H170) 主板 |
| **NCT6795D** | `0xD3` | `0x52` | LGA1151 (Z270/B250) 主板 |
| **NCT6796D / NCT6796D-R** | `0xD4` | `0x23` / `0x2A` | LGA1151 (Z370/Z390/B360) 主板 |
| **NCT6797D** | `0xD4` | `0x51` | LGA1200 (Z490/B460) 主板 |
| **NCT6798D** | `0xD4` | `0x2B` | LGA1200 (Z590/B560) 及部分 AM4 主板 |
| **NCT6799D** | `0xD8` | `0x02` | LGA1700 (Z690/B660/Z790) 及 AM5 主板 |
| **NCT6701D** | `0xD8` | `0x06` | 现代 OEM / 台系主板 |

---

### 更新日志 (Changelog)

#### v1.2.0 (2026-10-28)
- **扩展芯片支持**：重构芯片检测逻辑，除已实机测试的 `NCT6779D` 外，扩展加入对 `NCT6776F`、`NCT6771F` 以及 `NCT6791D ~ NCT6799D` / `NCT6701D` 的寄存器兼容支持。
- **转速采集自适应**：根据检测到的芯片型号自适应选择转速寄存器算法（NCT6779D/NCT679x 采用 13-bit 计数器，老款 NCT6771F/NCT6776F 采用 16-bit 转速寄存器）。
- **优化文档**：规范说明文档，标注当前仅实测了 NCT6779D 芯片。

#### v1.1.0 (2026-10-28)
- **新增原生风扇控制卡与转速读取**：向 FanControl 注册专属 `NCT6779D SYS_FAN Control (PWM)` 控制卡与 `NCT6779D SYS_FAN` 实时转速传感器，并实现自动配对。
- **解决无传感器警告**：彻底解决 FanControl 启动时因插件未加载传感器而弹出 `could not initialize or has no sensors` 的提示问题。
- **增强 PWM 模式守护**：后台定期轮询 Bank 0 Reg 0x004 状态，防止 Windows 睡眠唤醒后被 BIOS 重置为 DC 模式。

#### v1.0.0 (2026-10-28)
- 初始版本发布，实现基础的 Nuvoton Super I/O 探测与 SYS_FAN (PWM 1) 模式强制设置。

---

### 安装与使用方法

1. 下载编译好的 [`FanControl.NCT6779D.dll`](./FanControl.NCT6779D.dll) 或 [`FanControl.NCT6779D.zip`](./FanControl.NCT6779D.zip)。
2. 将 `FanControl.NCT6779D.dll` 复制到 FanControl 的 `Plugins\` 文件夹中（例如 `FanControl\Plugins\FanControl.NCT6779D.dll`）。
   > **注意**：Windows 可能会对下载的 DLL 文件添加锁定标记。右键点击该 `.dll` -> **属性** -> 勾选底部的 **“解除锁定 (Unblock)”** -> 点击确定。
3. **完全退出并重新启动 FanControl**（确保系统托盘已完全退出）。
4. 启动后，在 FanControl 界面中即可看到：
   - 控制卡片：`NCT6779D SYS_FAN Control (PWM)`
   - 转速传感器：`NCT6779D SYS_FAN`
5. 此时即可直接将该控制卡绑定到你自定义的任何风扇曲线上，享受安静且可控的 PWM 调速！
   *(主板原厂默认识别出的那个失效风扇卡可直接在 FanControl 中右键隐藏)*。

---

### 技术原理与寄存器参考

- **Super I/O 芯片基地址**：通过 `0x2E/0x2F` 或 `0x4E/0x4F` 进入扩展配置模式，读取逻辑设备 `0x0B` 的基地址（通常为 `0x0A20` 或 `0x0290`）。
- **寄存器功能表**：
  | 寄存器 (Bank + Reg) | 功能 | 插件行为 / 写入值 |
  | :--- | :--- | :--- |
  | **Bank 0, Reg `0x004` Bit 0** | PWM 1 模式 (0: PWM, 1: DC) | 设为 `0` (PWM Mode) |
  | **Bank 0, Reg `0x001`** | PWM 1 实际输出占空比 | 实时读取用于反馈当前实际值 |
  | **Bank 1, Reg `0x0102`** | PWM 1 风扇控制模式 | 控制时写入 `0` (Manual Mode)；重置时恢复默认 |
  | **Bank 1, Reg `0x0109`** | PWM 1 手动输出命令值 | 写入 `0 ~ 255` (对应 `0% ~ 100%`) |
  | **Bank 4, Reg `0x4B0` / `0x4B1`** | 13-bit 风扇转速计数器 | 读取并计算 `RPM = 1.35e6 / count` |

---

### 从源码自行编译

本项目为单文件免依赖设计，使用 Windows 自带的 `.NET Framework 4.x` 编译器 (`csc.exe`) 即可一键编译：

```powershell
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$netstandard = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
$pluginApi = "path\to\FanControl.Plugins.dll"

& $csc /nologo /target:library /optimize+ /reference:$netstandard /reference:$pluginApi /out:FanControl.NCT6779D.dll NCT6779DPwmModePlugin.cs
```

---

<a name="english"></a>
## English

**FanControl.NCT6779D** is a dedicated hardware-level plugin for [FanControl](https://github.com/Rem0o/FanControl.Releases).

It resolves the longstanding issue where **Nuvoton NCT6779D** (and compatible NCT677x / NCT679x Super I/O chips, widely used on **Intel X99 motherboards**) defaults the `SYS_FAN` (`PWM 1`) header to **DC mode**, locking connected 4-pin PWM fans at 100% speed.

### Key Features (v1.2.0)
- **Automatic PWM Mode Enforcement**: Sets Bank 0, Reg `0x004` Bit 0 = 0 (PWM Mode) on startup and monitors it against S3 sleep/BIOS resets.
- **Dedicated PWM Control Card**: Exposes `NCT6779D SYS_FAN Control (PWM)` supporting smooth 0%–100% manual duty cycle.
- **Accurate RPM Reading & Pairing**: Reads fan counter registers (Bank 4, `0x4B0`/`0x4B1` or `0x656`) and pairs automatically with the control card.
- **Broad Super I/O Chip Support**: Supports **NCT6779D** (tested), plus **NCT6776F, NCT6771F**, and the modern **NCT6791D through NCT6799D / NCT6701D** family based on register specs.
- **ISA Bus Mutex Protected**: Reflects into LHM's shared ISA mutex to ensure zero register or bank conflicts with FanControl's sensor loop.

### Installation
1. Copy [`FanControl.NCT6779D.dll`](./FanControl.NCT6779D.dll) into the `FanControl\Plugins\` folder.
2. Unblock the DLL if flagged by Windows (Right-click -> Properties -> Unblock).
3. Restart FanControl.

### License
MIT License
