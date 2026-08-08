# AutoMapper DTO 映射使用指南

## 📋 概述

本方案通过 AutoMapper 实现了**业务类与存储 DTO 的解耦**，使得：
- 业务类（`CanvasSnapshot`、`DrawingLayer`、`IShape` 等）专注于业务逻辑
- 存储 DTO（`CanvasSnapshotDto`、`DrawingLayerDto`、`IShapeDto` 等）专注于序列化
- 两者之间通过 AutoMapper 自动转换

## 🏗️ 架构层次

```
┌─────────────────────────────────────────┐
│         业务层 (Business Layer)          │
│  CanvasSnapshot, DrawingLayer, IShape   │
│  - 包含业务逻辑方法（HitTest, Clone 等）  │
│  - 不关心序列化细节                       │
└──────────────┬──────────────────────────┘
               │ AutoMapper 转换
               ▼
┌─────────────────────────────────────────┐
│        存储层 (Storage Layer)            │
│  CanvasSnapshotDto, DrawingLayerDto     │
│  - 纯数据对象（仅用于 ProtoBuf 序列化）   │
│  - 不包含任何业务逻辑                     │
└─────────────────────────────────────────┘
```

## 📁 文件结构

```
03 Domain\DrSoft.MarkCard.Model\
├── DTO\Storage\                      # 存储 DTO 类
│   ├── CanvasSnapshotDto.cs
│   ├── DrawingLayerDto.cs
│   ├── IShapeDto.cs
│   ├── DrawCircleDto.cs
│   ├── DrawRectangleDto.cs
│   ├── DrawLineDto.cs
│   ├── DrawPolyLinesDto.cs
│   ├── DrawTextDto.cs
│   ├── GroupDto.cs
│   ├── CombinationDto.cs
│   ├── Point2D.cs
│   ├── PenDto.cs
│   ├── DrawingColorDto.cs
│   ├── PenStyleDto.cs
│   └── ShapeTypeDto.cs
├── Mapping\                          # AutoMapper 配置
│   ├── MarkCardMappingProfile.cs     # 映射规则配置
│   └── MarkCardMapper.cs             # 静态访问提供者
└── DTO\
    └── ShapeSerializableDto.cs       # 顶层序列化对象（使用 DTO）
```

## 🔄 使用方式

### 1️⃣ 保存文件（业务对象 → DTO）

```csharp
// 从画布获取业务对象
CanvasSnapshot canvasSnapshot = GetCanvasSnapshot();

// 使用 AutoMapper 转换为 DTO
var canvasSnapshotDto = MarkCardMapper.Map<CanvasSnapshot, CanvasSnapshotDto>(canvasSnapshot);

// 创建可序列化的 DTO 对象
ShapeSerializableDto dto = new()
{
    CanvasSnapshot = canvasSnapshotDto,
    MarkingParams = markingParams
};

// 序列化保存
SaveToFile(dto, filePath);
```

### 2️⃣ 加载文件（DTO → 业务对象）

```csharp
// 从文件加载 DTO
ShapeSerializableDto dto = LoadFromFile(filePath);

// 使用 AutoMapper 转换为业务对象
CanvasSnapshot canvasSnapshot = MarkCardMapper.Map<CanvasSnapshotDto, CanvasSnapshot>(dto.CanvasSnapshot);

// 将业务对象传递给 UI 层
_eventBus.Publish<FileMenuEvent, int>(new FileMenuEvent
{
    Snapshot = canvasSnapshot,
    Message = fileName,
    Order = FileOrderEnum.Open,
    Path = filePath
});
```

## 🎯 核心优势

### ✅ 业务类纯净
```csharp
// 业务类：只关注业务逻辑
public class CanvasSnapshot
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public List<DrawingLayer> Layers { get; set; }
    
    // 可以添加任意业务方法，不受序列化约束
    public void SomeBusinessMethod() { ... }
}
```

### ✅ DTO 类纯粹
```csharp
// DTO 类：只关注数据存储
[ProtoContract]
public class CanvasSnapshotDto
{
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public string? Name { get; set; }
    [ProtoMember(3)] public List<DrawingLayerDto> Layers { get; set; }
    
    // 没有任何业务方法，纯数据容器
}
```

### ✅ 自动映射
```csharp
// 无需手动转换，AutoMapper 自动处理
var dto = MarkCardMapper.Map<CanvasSnapshot, CanvasSnapshotDto>(businessObject);
var biz = MarkCardMapper.Map<CanvasSnapshotDto, CanvasSnapshot>(dto);
```

## 🔧 扩展新形状

当需要添加新的形状类型时：

### 1. 创建业务类
```csharp
// 99 Components\Drawing\DrSoft.Drawing.Models\DrawEllipse.cs
public class DrawEllipse : DrawObject
{
    // 业务逻辑...
}
```

### 2. 创建 DTO 类
```csharp
// 03 Domain\DrSoft.MarkCard.Model\DTO\Storage\DrawEllipseDto.cs
[ProtoContract]
public class DrawEllipseDto : IShapeDto
{
    [ProtoMember(1)] public int UId { get; set; }
    // ... 其他属性
}
```

### 3. 注册 ProtoInclude
```csharp
// IShapeDto.cs 中添加
[ProtoInclude(107, typeof(DrawEllipseDto))]
public interface IShapeDto { ... }
```

### 4. 添加映射规则
```csharp
// MarkCardMappingProfile.cs 中添加
CreateMap<DrawEllipse, DrawEllipseDto>();
CreateMap<DrawEllipseDto, DrawEllipse>();

// 并注册到多态映射
CreateMap<IShape, IShapeDto>()
    .Include<DrawEllipse, DrawEllipseDto>();
```

## ⚠️ 注意事项

1. **不要**在 DTO 中添加业务逻辑方法
2. **不要**在业务类中添加 `[ProtoContract]` 等序列化特性（已有的是为了兼容，建议逐步移除）
3. **必须**在添加新形状时同时更新 `IShape` 和 `IShapeDto` 的 `[ProtoInclude]`
4. **必须**确保 AutoMapper 映射规则覆盖所有属性

## 📊 性能说明

- AutoMapper 首次初始化会创建映射配置（约 10-50ms）
- 后续映射使用动态生成的 IL 代码，性能接近手写转换
- 对于文件保存/加载场景，性能开销可忽略不计

## 🎓 参考资料

- [AutoMapper 官方文档](https://docs.automapper.org/)
- [ProtoBuf-net 文档](https://github.com/protobuf-net/protobuf-net)
