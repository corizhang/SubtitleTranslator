# AI 字幕翻译 Design Token 规范

## 1. 目标

Design Token 是应用视觉决策的唯一来源。页面不得自行定义可复用的品牌色、文字色、边框色、常用字号、控件高度、圆角或布局宽度。WPF UI 提供 Fluent 控件能力，本项目 Token 负责产品语义和品牌约束。

## 2. 分层

资源按以下顺序合并，后层可引用前层：

1. WPF UI `ThemesDictionary` 与 `ControlsDictionary`。
2. `DesignTokens.Colors.xaml`：原始色板、语义颜色和兼容别名。
3. `DesignTokens.Metrics.xaml`：4px 间距、内边距、圆角、控件及布局尺寸。
4. `DesignTokens.Typography.xaml`：字体族、字号和文本语义样式。
5. `ComponentStyles.xaml`：按钮、卡片、导航和图标按钮等组件模板。

## 3. 命名规则

- 原始值：`Palette.{Color}.{Step}`，仅供语义 Token 使用。
- 语义画刷：`Brush.{Category}.{Role}`，例如 `Brush.Text.Secondary`。
- 间距：`Space.{Scale}`，以 4px 为基础单位。
- 内边距：`Inset.{Component}.{Variant}`。
- 圆角：`Radius.{Role}`。
- 尺寸：`Size.{Component}.{Role}`。
- 字号：`Font.Size.{Role}`。
- 文本样式：`Text.{Role}`。
- 组件样式：`{Component}{Variant}Style`。

禁止在新代码中增加 `BlueBrush1`、`LargePadding`、`EditorGray` 等依赖颜色外观或具体页面的名称。

## 4. 使用原则

- 页面只引用语义 Token，不直接引用 `Palette.*`。
- 同一视觉含义必须复用同一个 Token；不要为数值相同但语义不同的对象错误共用 Token。
- 组件交互状态集中在组件样式中，页面不得重复实现 Hover、Pressed、Focus 和 Disabled。
- 默认界面不使用投影；只有浮层、菜单和模态对话框可以使用专用浮层阴影。
- 业务状态使用 Success、Warning、Danger，不以具体颜色名称表达。
- 任何新 Token 必须至少有两个预期使用位置，单页特例保留为局部值并注明原因。

## 5. 当前迁移范围

第一阶段已迁移应用资源入口、主框架关键尺寸与颜色、字幕校订中心栏位尺寸、表面颜色、图标按钮和底部状态栏。第二阶段已覆盖工作台、批量任务、项目库、资源与模型、设置和诊断中心的页面骨架及高频组件。第三阶段已删除旧兼容键，业务 XAML 的颜色全部通过语义 Token 引用，并加入自动审计与 DPI 等效布局测试。

下一阶段依次迁移工作台、批量任务、项目库、资源与模型、设置和诊断中心，并用硬编码扫描作为发布检查项。

## 6. 自动审计

运行 `tools/Test-DesignTokens.ps1` 检查业务 XAML。审计会拒绝 Design Token 字典之外的十六进制颜色，以及所有旧兼容资源键。单页确需新颜色时，应先在颜色字典中补充语义 Token，而不是绕过审计。
