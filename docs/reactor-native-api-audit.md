# Reactor 封装与原生调用审计

审计日期：2026-10-16。对象：Tonarink `main` 的 `src/Tonarink.App` 手写 C# 代码。

首次扫描只检查和分类。下面保留扫描时的发现与判断，整改情况见文末；编译确认不代表已验证实际交互效果。

## 依据与边界

以项目实际引用的 **Microsoft.UI.Reactor 0.1.0-preview.16** 为可用能力基准，而不是以上游最新源码存在某 API 为依据：

- `Directory.Packages.props`
- 本机 NuGet 包的 `agentkit/reactor.api.txt`：Factories、Modifiers、Hooks 和相关类型定义
- 同包 `agentkit/skills/input.md`、`windowing.md` 相关章节及 XML API 注释
- `D:/dotnet/microsoft-ui-reactor` 中的事件绑定、焦点、动画、弹出层和托盘实现，用于核对行为差异；该检出可能比发布包新，新增能力不视为当前可用

搜索覆盖完整命名空间、原生类型别名、`new` 控件、`.Set(...)`、事件订阅/`AddHandler`、原生属性写入、视觉树、Composition 和 Dispatcher。完整命名空间本身不是问题：数据类型、枚举、生成的动画源、必要的系统集成不应为了消除名称而再包装一层。

## 先建立回调/API 对照

| 场景 | 当前包已有封装 |
| --- | --- |
| 普通指针事件 | `OnPointerEntered/Exited/Pressed/Released/Moved/WheelChanged/Canceled/CaptureLost` |
| 点击与手势 | `OnTapped`、`OnDoubleTap/DoubleTapped`、`OnRightTapped`、`OnHolding`、`OnLongPress`、`OnPan/Pinch/Rotate` |
| 键盘与焦点 | `OnKeyDown/Up`、`OnPreviewKeyDown/Up`、`OnCharacterReceived`、`OnGotFocus/LostFocus`、`OnAccessKeyDisplayRequested` |
| 拖放 | `OnDragStart/Enter/Leave/Over`、`OnDrop`，包含带类型的数据接口 |
| 生命周期与尺寸 | `OnMount/Unmount`、`OnMountAdd/UnmountAdd`、`OnSizeChanged` |
| 控件专用事件 | Button 的点击参数/`Click`、ComboBox 的 `DropDownOpened/Closed`、选择/勾选变化、ContentDialog 的 `Opened/Closed` 等；并非都以 `On` 命名 |
| 聚焦与系统偏好 | `UseElementFocus`、`UseFocus`、`UseFocusTrap`、`UseReducedMotion`、`UseIsDarkTheme`、`UseHighContrast` |
| 动画与窗口 | `Keyframes`、`Rotation/Scale/Translation`、`ConnectedAnimation`、`NavigationTransition.Connected`、`WindowSpec`、`UseTrayIcon` |

注意：`OnError/OnWarning` 是样式配置分支，不是应用异常回调。当前没有通用 `OnLoaded`、`OnActualThemeChanged`；`OnMount` 也不等于 WinUI 的 `Loaded`。

## 一、建议处理：已有封装或明确的冗余

| 位置 | 当前实现 | 建议与限制 |
| --- | --- | --- |
| `Windowing/TrayContextMenu.cs`（整改后已删除） | 旧 Win32 `CreatePopupMenu/TrackPopupMenuEx` 实现 | 全仓库搜索没有调用点，可删除遗留类。现用 `TrayMenuHost` 已通过 Reactor `MenuItems` 和命令构建菜单，不需另写替代实现 |
| [SegmentedContentSwitcher](../src/Tonarink.App/Components/Animations/SegmentedContentSwitcher.cs:95) | 组件内部自行创建 `UISettings`，读取 `AnimationsEnabled` | 改用组件顶层 `UseReducedMotion()`，传给切换逻辑；原生动画是否保留是另一件事。需要明确系统偏好在动画途中变化时是否立即中止 |
| [SettingsPage](../src/Tonarink.App/Pages/Settings/SettingsPage.cs:224) 等 7 处 Expander | `.Set` 写 `Header/Description` | 这些原生属性已经由生成 wrapper 映射，优先用生成属性的 `with { Header = ..., Description = ... }`。当前额外的 Content 工厂重载没有暴露这两个参数，不代表 wrapper 不支持它们。关于项中额外写入的字符串 `Content` 要单独处理，不应机械改成另一种内容类型 |
| [IncomingTransferOverlay](../src/Tonarink.App/Components/Transfers/IncomingTransferOverlay.cs:417) 3 处、[OutgoingTransferOverlay](../src/Tonarink.App/Components/Transfers/OutgoingTransferOverlay.cs:108) 2 处 | `OnMountAdd(element => element.Focus(...))` | 可用 `UseElementFocus` 的稳定 ref + requestFocus。该接口将焦点请求排到当前协调之后；须保留每个接收/完成阶段聚焦哪个按钮的行为，hook 不能移进条件分支 |
| [AnimatedDeleteButton](../src/Tonarink.App/Components/Animations/AnimatedDeleteButton.cs:192) | 原生 ScalarKeyFrameAnimation 播放摇晃 | `Keyframes(..., ShakeVersion, ...).At(..., rotation: ...)` 已能表达这些帧；可统一到 Copy/Verify 使用的 DSL。仍需保持重复触发、重置和减少动态效果的行为；尺寸相关旋转中心不是同一个替换项 |
| [NetworkStatusPane](../src/Tonarink.App/Components/Shell/NetworkStatusPane.cs:254) | 单独实现图标前景色更新 | 不是 Reactor 缺失封装，而是上一轮公共逻辑提取的遗漏，可复用 `AnimatedIconForeground.Apply`。`Loaded/ActualThemeChanged` 的订阅仍需保留 |

这六类中，删除摇晃动画和自动聚焦涉及时机变化，属于“能力已经具备，但迁移仍需核对行为”，不是查找替换即可完成。

## 二、有类似封装，但不能直接等价替换

### 全局鼠标返回键

[AppShell.cs:429](../src/Tonarink.App/AppShell.cs:429) 使用 `AddHandler(..., handledEventsToo: true)`，目的是在子按钮/输入控件已处理鼠标消息后，仍接收 XButton1 返回操作。

普通 `OnPointerPressed` 使用正常事件订阅，没有对应的 `handledEventsToo` 参数。直接替换会缩小事件接收范围。当前应保留；若希望彻底声明式化，需要框架先补足这一能力，而不是重复造普通指针回调。

刚整理过的动画目录已没有手动 `AddHandler` 或 `PointerEntered +=` 一类普通鼠标事件绑定残留。现在唯一这类 `AddHandler` 是上述全局处理。

### 设备连接动画

[DeviceConnectedAnimation](../src/Tonarink.App/Services/Devices/DeviceConnectedAnimation.cs) 与框架 `ConnectedAnimation` / `NavigationTransition.Connected` 存在能力重叠，但它还负责：

- 页面之外的传输覆盖层及返回原卡片
- 目标挂载、布局就绪后的有限帧重试
- 自定义配置、完成/失败反馈，以及源卡片引用管理

可以优先研究“普通设备详情导航”是否交给框架，但不能据此直接删除整个服务；覆盖层不是 NavigationHost 的一次页面跳转。

### 分段内容切换与图标旋转中心

[SegmentedContentSwitcher](../src/Tonarink.App/Components/Animations/SegmentedContentSwitcher.cs) 同时保持两个页面挂载，动画完成后才折叠旧页，且有中断版本号和 Composition batch 完成回调。`Transition/Keyframes` 能表达动画属性，但不能仅凭这一点保证保留上述生命周期。

[Copy](../src/Tonarink.App/Components/Animations/AnimatedCopyButton.cs:90)、[Verify](../src/Tonarink.App/Components/Animations/AnimatedVerifyButton.cs:104)、[Delete](../src/Tonarink.App/Components/Animations/AnimatedDeleteButton.cs:213) 的原生表达式绑定旋转/缩放中心，依赖实时 `visual.Size`。框架 `CenterPoint(Vector3)` 是绝对值，不是百分比；不能把 `(0.5, 0.5, 0)` 当作中心点替换。这里可以提取项目内共同辅助逻辑，但当前未找到等价的相对中心声明接口。

### 托盘面板和关闭策略

- `ReactorTrayIcon.ShowFlyout/HideFlyout` 已有封装，但其简单展示/失焦关闭模型不等价于当前 [TrayFlyoutHost](../src/Tonarink.App/Windowing/TrayFlyoutHost.cs) 的固定面板、窗口复用、自定义窗口动画和点击外部收起策略，不建议整体替换
- 该服务仍读取 `UISettings.AnimationsEnabled`，但它不是组件，不能直接调用 hook；可以后续由组件传入偏好，不必为消除原生类型额外造一个全局服务
- [UseShellWindow](../src/Tonarink.App/Hooks/Shell/UseShellWindow.cs:25) 的 `window.Closing` 本身是 ReactorWindow 事件。当前 `UseClosingGuard(Func<bool>)` 不提供关闭原因，无法区分用户关闭和真正退出，不能直接替换
- 原生窗口位置使用物理像素，Reactor 的位置/尺寸接口使用 DIP；托盘定位中不能忽略 DPI 后机械替换

## 三、当前应保留的原生边界

| 位置/类别 | 保留原因 |
| --- | --- |
| 多个 Lottie 组件的 `Loaded`、`ActualThemeChanged` | 当前没有对应通用回调，且挂载时可能尚无有效主题/XamlRoot，不能一律换 `OnMount`。若以后抽取公共订阅，应一起处理卸载与重用生命周期 |
| `AnimatedVisualPlayer.Source/PlayAsync/Stop/SetProgress` | 当前播放器声明面主要提供 AutoPlay 等，未覆盖这些精确播放操作、区间播放和完成等待 |
| [AnimatedSettingsToggleIcon](../src/Tonarink.App/Components/Animations/AnimatedSettingsToggleIcon.cs:73) 的 `AnimatedIcon.SetState` | 当前没有对应状态 modifier。Source/FallbackIconSource 有声明属性，可做局部整理，但要维持动画源身份；状态调用仍需原生接口 |
| [NavigationAnimatedIcons](../src/Tonarink.App/Components/Shell/NavigationAnimatedIcons.cs:28) | NavItem 的 IconElement 接受 IconData，不是任意 Element；当前 IconData 没有 AnimatedIcon 变体。原生项还负责继承状态、自动化标识和单项资源，不能简单用一个普通 Icon 工厂取代 |
| [ContentDialogStyleExtensions](../src/Tonarink.App/Styling/ContentDialogStyleExtensions.cs:10) | 虽有通用 RequestedTheme modifier，ContentDialog 的真实弹窗与其树内占位节点不同。`.Set` 明确作用于真实 dialog，不能仅按属性同名就替换 |
| [TrayMenuHost](../src/Tonarink.App/Windowing/TrayMenuHost.cs:95) | 菜单内容已由 Reactor 构造；剩余 XamlRoot、鼠标位置 ShowAt、辅助窗口激活和菜单关闭收尾属于原生托盘边界。当前 MenuFlyoutContentElement 没有 Closed 回调，不能套用普通 Flyout 的 Closed 接口 |
| [AppNavigationDialogs](../src/Tonarink.App/Services/Activation/AppNavigationDialogs.cs:14) | 枚举当前打开的弹窗、识别并关闭特定弹窗，再等待其原生关闭；不是某个已声明 dialog 的一般 Closed 处理。后续可随弹窗所有权集中管理再研究消除视觉树查询 |
| [QrCodeCanvas](../src/Tonarink.App/Utilities/QrCodeCanvas.cs:30) | 已使用 Win2DCanvas 工厂，原生部分是绘制与 Composition 裁剪，不能因为存在普通 CornerRadius 就认为能够等价替代 |
| NativeSystemMenuTheme、共享面板、徽标/通知、跳转列表、执行别名、Explorer/SendTo、激活处理、剪贴板与外部启动 | 系统/WinRT/COM 集成。当前 API 未提供对应完整封装，不应归类为“漏用了 On 回调” |
| `Thickness/FontWeight/FontFamily/Colors`、控件枚举和类型判断 | Reactor 的 API 本身使用原生值类型。完整命名空间通常只是避免名称冲突，可做命名整理，但不是架构问题 |

## 四、等待依赖版本后再收敛

以下项目不能因为本机上游检出已有实现，就直接在当前主分支使用：

| 位置 | 当前缺口与后续动作 |
| --- | --- |
| [SettingsExpanderItems](../src/Tonarink.App/Controls/SettingsExpanderItems.cs) 与 [ToolkitControls](../src/Tonarink.App/Controls/ToolkitControls.cs) | 当前发布包尚需手写动态 Items/Content 协调和 AOT 适配。升级到包含相关上游修复的版本后，再验证自动 wrapper 并删适配；与上文 Header/Description 的声明属性整理是两件事 |
| PageHeader / AppShell 的资源字典写入 | 当前 ResourceBuilder 缺 Thickness/FontWeight 等重载，不能直接改成同名 Set。等待对应资源重载版本后收敛 |
| [StoragePicker](../src/Tonarink.App/Services/Platform/StoragePicker.cs) | preview.16 只有单文件和文件夹 hook，没有多文件接口；当前服务还明确指定窗口 HWND、保留 WinRT 集合以适配 AOT。后续升级时一并核对多文件、owner 和返回类型，不宜只为文件夹拆出两套路径 |
| AppShell 的 TitleBar.PreferredTheme | 当前 WindowSpec API 没有相应配置，后续使用包含该配置的版本；NativeSystemMenuTheme 仍是另一个系统菜单主题问题，不随之删除 |

## 推荐顺序

1. 先做无行为变化的收敛：删除无引用旧托盘菜单、网络图标公共前景色、Expander Header/Description
2. 再处理已有框架能力的局部迁移：组件减少动态效果 hook、阶段自动聚焦、删除摇晃关键帧
3. 设备连接动画和分段切换独立评估，不与小清理混合；原生边界只在有等价语义时移除
4. 依赖升级后另做一次清单核对，删除届时不再需要的适配，不提前引入尚不可用 API

此次没有发现新的“大量普通事件被手写”的情况。主要剩余量来自动画播放器生命周期、项目特定的交互策略、系统集成和当前依赖版本缺口，不能以原生调用数量作为重构完成标准。

## 整改结果

2026-10-16 完成上述六类局部改造：

- 删除无引用的旧 Win32 托盘菜单类；现用的 Reactor 菜单不变
- 分段切换改用 `UseReducedMotion`，关闭动画偏好时使旧 batch 失效并立即落到选中页；仍保留双页挂载和原有 Composition 切换
- 7 处 Expander Header/Description 改用生成 wrapper 属性/工厂参数，保留关于项原有的字符串 Content 写入及动态 Items 适配
- 5 处按钮聚焦改用顶层 `UseElementFocus`，各按钮使用独立 ref，仍在对应挂载点请求焦点；接收按钮先应用 AccentButton 样式，再追加挂载回调，避免样式覆盖聚焦回调
- 删除按钮摇晃改为 Reactor `Keyframes`，保留 340 ms 和原有幅度，显式将角度转换为 Composition 所需弧度；版本增加才触发，减少动态效果时归零
- 网络状态图标复用 `AnimatedIconForeground`，保留 Loaded/ActualThemeChanged 时机

核对关键帧单位与焦点调度时，还对照了 NuGet 包 nuspec 标注的源码提交 `459f7234dc2b0b72125f833ba86c051a554c12cf`。

验证仅执行 x64 Debug 编译，不新增或运行测试用例，不进行 UI 自动化；第二至四节标记为保留/待升级的部分未改动。
