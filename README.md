# ThreeFingerDrag

一个轻量、可移植的 Windows Precision Touchpad 三指拖拽工具。三指轻放并移动即可像 macOS 一样拖动窗口、文件、滚动条，或选择文本。

## 特点

- 单个 EXE，无安装器、无服务、无驱动、无第三方运行库
- 后台直接读取 Precision Touchpad 的 Raw Input/HID 接触点
- 低延迟：输入处理和鼠标注入均在原生 Windows 消息循环内完成
- 抬指后保留 350 ms 拖拽状态，可像 macOS 一样重新落指继续拖；期间鼠标或单指触控板一旦移动会立即松开，避免粘滞
- 换指时自动重设基准，避免光标跳变
- 自动学习单指移动时的系统光标速度，使三指拖拽接近普通双击拖拽的手感
- 托盘中可暂停、微调相对速度、开关拖拽保持和设置当前用户开机自启

## 使用

1. 运行 `release\ThreeFingerDrag.exe`。
2. 打开 **设置 → 蓝牙和设备 → 触摸板 → 三指手势**，把三指轻扫和三指点击设为“无”，避免 Windows 自带手势抢占。
3. 把光标放在要拖动的目标上，三指接触触控板并移动。
4. 先正常单指移动几次，程序会自动学习当前 Windows 指针速度。
5. 右击托盘图标可选择“开机自启（当前用户）”或微调相对速度；双击可快速暂停或恢复。

启动成功后会显示托盘通知；未检测到 Precision Touchpad、Raw Input 注册失败或程序已经在运行时也会给出明确提示。

程序只支持符合 Windows Precision Touchpad (PTP) 规范的触控板。普通鼠标模拟型触控板不会提供多点 HID 数据。

## 构建

Windows 10/11 自带 .NET Framework 编译器，不需要 Visual Studio：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

正式产物始终位于 `release\ThreeFingerDrag.exe`。更新前请先退出正在运行的托盘程序，否则 Windows 会锁定 EXE。运行内置状态机测试：

```powershell
.\release\ThreeFingerDrag.exe --self-test
echo $LASTEXITCODE
```

## 已知限制

- `SendInput` 受 Windows UIPI 保护：普通权限运行时不能拖动“以管理员身份运行”的窗口。若确有需要，可手动以管理员身份启动本程序。
- 部分厂商的非 PTP 触控板不兼容。
- 当前环境没有真实触控板，因此构建和手势状态机可自动验证；HID 报告兼容性仍需在目标电脑上实测。

## 技术说明

程序注册 Digitizers usage page (`0x0D`) 下的 Touch Pad usage (`0x05`)，通过 `WM_INPUT` 接收后台数据；使用 HID preparsed data 解析 Contact Count、Contact Identifier、X 和 Y。程序会比较单指 HID 位移与系统光标的实际位移来学习速度；三指拖拽时使用 `SendInput` 注入绝对光标位置与左键状态，避免 Windows 再次对相对移动施加加速度。

实现参考 Microsoft Precision Touchpad HID 规范，以及 emoacht/RawInput.Touchpad 展示的通用 HID capability 解析方法；本项目代码为独立实现。参见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
