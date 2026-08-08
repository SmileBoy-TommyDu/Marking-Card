# LaserEngrave-dev 开发约束

本文只整理当前仓库中与 `DrawingCanvas` 重构相关、后续开发必须遵守的规则。

## 1. 先看文档

涉及 `DrawingCanvas`、交互、事件、状态收敛时，优先参考：

- `docs/react/整体改造评审.md`
- `docs/react/事件与刷新架构收口清单.md`
- `docs/react/DrawingCanvas架构约束.md`
- `docs/react/DrawingCanvas外围契约清单.md`
- `docs/react/DocumentContext回调清单与宿主迁移依据.md`

不要在不了解当前主通路和边界约束的前提下直接加代码。

## 2. 状态分层规则

`DocumentContext` 只作为聚合入口，新字段不要直接平铺到根部。

新增字段前必须先判断归属：

- `CanvasDocumentState`
  - 当前工具、当前图元、当前画布、持久编辑语义
- `CanvasViewState`
  - 脏区、缓存、显示开关、渲染相关状态
- `CanvasInteractionState`
  - 框选、拖拽、节点编辑、鼠标过程态

禁止：

- 同一交互语义同时落到多个 state 分区
- 用额外布尔字段重复表达已有枚举状态

## 3. 事件与主通路规则

一个变化只保留一条主通路。

当前重点主通路：

- 选区变化
  - `DrawingCanvas.SetSelectedShapes()/ClearSelectedShapes()`
  - -> `DocumentContext.PublishSelectChanged()/PublishSelectSharpsChange()`
  - -> `CanvasChangedEvent`
- 视口变化
  - `ToolZoom.NotifyZoomChanged()`
  - -> `ViewportChangedEvent`
- 节点编辑模式变化
  - `PathNodeEditSession` / `DrawingCanvas.Services`
  - -> `EditNodesModeChangedEvent`

禁止：

- 新增同语义双写通知
- 新增 `BaseEvent<object> + EventName` 风格弱类型事件
- 把局部 UI 行为伪装成全局领域事件

## 4. Tool / Session / Service 边界

职责划分必须保持清楚：

- `Tool`
  - 输入路由与顶层决策
- `Session`
  - 短生命周期交互状态机
- `Service`
  - 纯逻辑、命中判断、批处理、可复用计算
- `Host`
  - UI 宿主能力桥接

满足以下任一条件时，优先拆成 `Session` 或 `Service`：

- 有独立生命周期
- 有 3 个以上专用状态字段
- 可独立测试
- 逻辑能被其他交互复用

不要继续把复杂交互直接堆回 `ToolSelect`。

## 5. Host 适配规则

如果某段逻辑本质上是在“让 WPF 做点事”，就不应继续平铺在 `DocumentContext` 或交互会话里。

这类能力应优先走 Host：

- 状态提示
- 光标切换
- 重绘请求
- 弹框请求

新增生产代码不要再直接依赖历史兼容回调字段。

## 6. 生命周期与订阅规则

长生命周期订阅必须成对释放。

要求：

- 订阅必须能明确找到所有权
- `Dispose()` 必须覆盖对应解绑
- 不要用无法解绑的匿名 lambda 做长期订阅

新增以下类型逻辑时，必须同步考虑释放：

- `CanvasViewModel`
- `DrawingCanvasControl`
- Layer / EventBus / MultiCanvas 订阅
- 全局消息或事件总线订阅

## 7. 测试规则

改动主通路时，默认要补测试。

优先覆盖：

- 主通路事件是否从唯一发布点发出
- 工具栏 / UI 状态是否由预期事件同步
- `Dispose()` 后是否还会接受旧事件回流

当前重点测试组：

- `InteractionSessionsTests`
- `SelectionNotificationTests`
- `EditPathNodesToolViewModelTests`
- `CanvasContractTests`

## 8. 兼容层规则

兼容层可以短期保留，但必须满足两点：

1. 能指出来源主通路
2. 能指出计划移除点

不要新增“暂时先放一个兼容字段、以后再说”这种无期限兼容。

## 9. 提交前自检

提交 `DrawingCanvas` 相关改动前，至少自问：

1. 这次状态变化的唯一主通路是什么？
2. 有没有新增重复状态或重复通知？
3. 这段逻辑属于 Tool、Session、Service 还是 Host？
4. 订阅是否有释放点？
5. 是否需要补主通路或生命周期回归测试？

如果这些问题答不清楚，先补设计说明，再继续写代码。
