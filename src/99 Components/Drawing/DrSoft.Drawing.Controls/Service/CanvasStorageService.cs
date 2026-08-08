using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DrSoft.Drawing.Controls.Service
{
    /// <summary>
    /// 负责画布数据与 drw 文件之间的序列化/反序列化。
    /// drw 采用分段存储：画布元数据、几何图元、图层附加 payload 各自独立。
    /// </summary>
    public sealed class CanvasStorageService
    {
        public const int DefaultStorageVersion = 1;
        public const int XingChengStorageVersion = 4;

        private const int MatrixStorageVersion = DefaultStorageVersion;
        private const uint GeometryMagic = 0x33465244; // "DRW3"
        private const uint PersistedTransformMagic = 0x31584D54; // "TMX1"
        private const int PersistedTransformSize = sizeof(uint) + (sizeof(float) * 6);

        private const int SectionHeaderSize = sizeof(int) + sizeof(long) + sizeof(long) + sizeof(int);
        private const int FileHeaderSize = sizeof(uint) + sizeof(int) + sizeof(int) + sizeof(int);

        /// <summary>
        /// 从 drw 文件读取画布快照和图层附加数据。
        /// </summary>
        public CanvasStorageDocumentDto Load(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);
            return ReadDocument(br);
        }

        /// <summary>
        /// 将当前画布保存为 drw 文件。
        /// 默认使用带最终仿射矩阵尾字段的 v1；兴诚互操作使用 v4。
        /// </summary>
        public void Save(
            string filePath,
            ICanvasData canvasData,
            IReadOnlyDictionary<int, byte[]>? layerPayloads = null,
            IReadOnlyDictionary<string, byte[]>? extensionPayloads = null,
            int storageVersion = DefaultStorageVersion)
        {
            ValidateStorageVersion(storageVersion);

            // 保存标准版本
            using var fs = new FileStream(Path.ChangeExtension(filePath, ".drw"), FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
            using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);
            WriteDocument(
                bw,
                canvasData,
                layerPayloads ?? new Dictionary<int, byte[]>(),
                extensionPayloads ?? new Dictionary<string, byte[]>(),
                storageVersion);

            // 标准兴诚专用版本
            //string dir = Path.GetDirectoryName(filePath);
            //string name2 = Path.GetFileNameWithoutExtension(filePath);
            //var filePath2 = Path.Combine(dir, name2 + "_xc.drw");
            //using var fs2 = new FileStream(Path.ChangeExtension(filePath2, ".drw"), FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
            //using var bw2 = new BinaryWriter(fs2, Encoding.UTF8, leaveOpen: false);
            //WriteDocument(
            //    bw2,
            //    canvasData,
            //    layerPayloads ?? new Dictionary<int, byte[]>(),
            //    extensionPayloads ?? new Dictionary<string, byte[]>(),
            //    storageVersion);
        }

        /// <summary>
        /// 校验文件头并根据版本选择具体读取流程。
        /// </summary>
        private static CanvasStorageDocumentDto ReadDocument(BinaryReader br)
        {
            if (br.ReadUInt32() != GeometryMagic)
                throw new InvalidDataException("drw magic 无效");

            var version = br.ReadInt32();
            return version switch
            {
                MatrixStorageVersion => ReadSectionedDocument(br, version),
                XingChengStorageVersion => ReadSectionedDocument(br, version),
                _ => throw new InvalidDataException($"不支持的 DRW 版本: {version}")
            };
        }

        private static void ValidateStorageVersion(int storageVersion)
        {
            var isSupportedVersion = storageVersion == MatrixStorageVersion ||
                                     storageVersion == XingChengStorageVersion;
            if (!isSupportedVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(storageVersion),
                    storageVersion,
                    "仅支持 DRW v1 和 v4。");
            }
        }

        /// <summary>
        /// 读取分段式 drw 文件。
        /// 先解析 section 表，再按偏移分别读取画布元数据、几何数据和图层 payload。
        /// </summary>
        private static CanvasStorageDocumentDto ReadSectionedDocument(
            BinaryReader br,
            int storageVersion)
        {
            var flags = br.ReadInt32();
            var sectionCount = br.ReadInt32();
            var sections = new Dictionary<SectionType, SectionEntry>(sectionCount);

            for (var i = 0; i < sectionCount; i++)
            {
                var entry = new SectionEntry(
                    (SectionType)br.ReadInt32(),
                    br.ReadInt64(),
                    br.ReadInt64(),
                    br.ReadInt32());
                sections[entry.Type] = entry;
            }

            CanvasSnapshotDto snapshot = new();
            Dictionary<int, byte[]> payloads = new();
            Dictionary<string, byte[]> extensionPayloads = new(StringComparer.Ordinal);

            if (sections.TryGetValue(SectionType.CanvasMeta, out var metaSection))
            {
                br.BaseStream.Seek(metaSection.Offset, SeekOrigin.Begin);
                snapshot = ReadCanvasMetaSection(br);
            }

            if (sections.TryGetValue(SectionType.Geometry, out var geometrySection))
            {
                br.BaseStream.Seek(geometrySection.Offset, SeekOrigin.Begin);
                ReadGeometrySection(br, snapshot, storageVersion);
            }

            if (sections.TryGetValue(SectionType.MarkCardPayload, out var payloadSection))
            {
                br.BaseStream.Seek(payloadSection.Offset, SeekOrigin.Begin);
                payloads = ReadLayerPayloadEntries(br);
            }

            if (sections.TryGetValue(SectionType.ExtensionPayloads, out var extensionSection))
            {
                br.BaseStream.Seek(extensionSection.Offset, SeekOrigin.Begin);
                extensionPayloads = ReadExtensionPayloadEntries(br);
            }

            return new CanvasStorageDocumentDto
            {
                CanvasSnapshot = snapshot,
                LayerPayloads = payloads,
                ExtensionPayloads = extensionPayloads
            };
        }

        private static CanvasSnapshotDto ReadCanvasMetaSection(BinaryReader br)
        {
            var layerCount = br.ReadInt32();
            var snapshot = new CanvasSnapshotDto
            {
                Id = br.ReadInt32(),
                Name = br.ReadString(),
                Layers = new List<ILayerData>(layerCount)
            };

            // 这里只构建图层壳数据，具体图元会在 Geometry section 中补齐。
            var layers = new List<ILayerData>(layerCount);
            for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                layers.Add(new DrawingLayer
                {
                    UId = br.ReadInt32(),
                    Name = br.ReadString(),
                    Color = br.ReadString(),
                    IsVisible = br.ReadBoolean(),
                    IsLocked = br.ReadBoolean()
                });
            }

            snapshot.Layers = layers;
            return snapshot;
        }

        /// <summary>
        /// 读取几何 section，并将图元挂回对应图层。
        /// </summary>
        private static void ReadGeometrySection(
            BinaryReader br,
            CanvasSnapshotDto snapshot,
            int storageVersion)
        {
            var layers = snapshot.Layers.OfType<DrawingLayer>().ToList();
            var layerCount = br.ReadInt32();
            if (layerCount != layers.Count)
                throw new InvalidDataException("几何 section 与画布元数据中的图层数不一致");

            for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                var layer = layers[layerIndex];
                var shapeCount = br.ReadInt32();
                // layer.EnsureShapeCapacity(shapeCount);
                for (var i = 0; i < shapeCount; i++)
                {
                    var shape = ReadShapeRecord(br, storageVersion);
                    layer.AddShape(shape);
                }
            }

            // 填充图形会额外持有 TargetShapes 引用，几何读完后统一做一次重绑和填充参数恢复。
            RestoreLoadedHatchState(layers);
        }

        /// <summary>
        /// 读取单个图元记录。
        /// 记录结构为：类型 + 通用头 + 定长 payload + 容器子节点。
        /// </summary>
        private static DrawObject ReadShapeRecord(
            BinaryReader br,
            int storageVersion)
        {
            var type = (ShapeType)br.ReadByte();
            var shape = CreateShape(type);
            var header = ReadShapeHeader(br, shape);
            var payloadLength = br.ReadInt32();
            var payloadStart = br.BaseStream.Position;
            var payloadEnd = payloadStart + payloadLength;
            var isMatrixStorageVersion = storageVersion == MatrixStorageVersion;
            var specificPayloadEnd = payloadEnd;
            if (isMatrixStorageVersion)
            {
                var hasTransformPayload = payloadLength >= PersistedTransformSize;
                if (!hasTransformPayload)
                {
                    throw new InvalidDataException($"DRW v{MatrixStorageVersion} 图元 {type} 的 payload 缺少变换矩阵字段。");
                }

                specificPayloadEnd -= PersistedTransformSize;
            }

            ReadSpecificPayload(br, shape, type, specificPayloadEnd, storageVersion);
            SKMatrix? persistedTransform = null;
            if (isMatrixStorageVersion)
            {
                persistedTransform = ReadPersistedTransform(br, type, payloadEnd);
            }

            var bytesRead = br.BaseStream.Position - payloadStart;
            if (bytesRead != payloadLength)
            {
                // shape payload 支持尾部扩展字段，读取方按声明长度对齐即可。
                br.BaseStream.Seek(payloadStart + payloadLength, SeekOrigin.Begin);
            }

            if (shape is IContainer container)
            {
                var childCount = br.ReadInt32();
                for (var i = 0; i < childCount; i++)
                {
                    var child = ReadShapeRecord(br, storageVersion);
                    container.Children.Add(child);
                }
                RebuildContainerGeometry(shape);
            }

            if (isMatrixStorageVersion)
            {
                var transform = persistedTransform!.Value;
                RehydrateLoadedShapeV1(shape, type, header, transform);
                RestorePersistedTransform(shape, header, transform);
                if (shape is DrawText loadedText)
                {
                    // 持久化矩阵按旧约定存储（作用于 Y 向上的局部路径，不含字体翻转），
                    // 覆盖运行时矩阵后需重新烘焙翻转，与保存侧的剥离对称。
                    loadedText.BakeFontFlipIntoMatrix();
                }
            }
            else
            {
                RehydrateLoadedShapeV4(shape, type, header);
            }

            return shape;
        }

        /// <summary>
        /// 根据持久化类型创建具体图元实例。
        /// </summary>
        private static DrawObject CreateShape(ShapeType type) => type switch
        {
            ShapeType.Point => new DrawDot(),
            ShapeType.PolyLine => new DrawPolyLines(),
            ShapeType.Rectangle => new DrawRectangle(),
            ShapeType.Circle => new DrawCircle(),
            ShapeType.Polygon => new DrawPolygon(),
            ShapeType.Arc => new DrawArc(),
            ShapeType.Bezier => new DrawBezier(new List<SKPoint>()),
            ShapeType.ArbitraryCurve => new DrawArbitraryCurve(),
            ShapeType.Text => new DrawText(),
            ShapeType.Combination => new DrawCombination(),
            ShapeType.Group => new DrawingGroup(),
            ShapeType.Hatch => new DrawingHatch(),
            ShapeType.CubicPath => new DrawCubicPath(),
            _ => throw new InvalidDataException($"不支持的图元类型: {type}")
        };

        /// <summary>
        /// 读取图元的类型专属 payload。
        /// </summary>
        private static void ReadSpecificPayload(
            BinaryReader br,
            DrawObject shape,
            ShapeType type,
            long payloadEnd,
            int storageVersion)
        {
            switch (type)
            {
                case ShapeType.Point:
                    ((DrawDot)shape).Points = ReadPoints(br, type, payloadEnd);
                    break;
                case ShapeType.PolyLine:
                    var polyLine = (DrawPolyLines)shape;
                    polyLine.LineStyle = (LineStyle)br.ReadByte();
                    polyLine.IsClosed = br.ReadBoolean();
                    polyLine.Points = ReadPoints(br, type, payloadEnd);
                    if (polyLine.Points.Count >= 2)
                    {
                        // DrawPolyLines 渲染依赖 UpdateSetProperty 初始化的局部点缓存。
                        polyLine.UpdateSetProperty(polyLine.Points);
                    }
                    break;
                case ShapeType.Rectangle:
                    var rectangle = (DrawRectangle)shape;
                    rectangle.CornerRadiusTopLeft = br.ReadSingle();
                    rectangle.CornerRadiusTopRight = br.ReadSingle();
                    rectangle.CornerRadiusBottomRight = br.ReadSingle();
                    rectangle.CornerRadiusBottomLeft = br.ReadSingle();
                    rectangle.ChamferTopLeft = br.ReadSingle();
                    rectangle.ChamferTopRight = br.ReadSingle();
                    rectangle.ChamferBottomLeft = br.ReadSingle();
                    rectangle.ChamferBottomRight = br.ReadSingle();
                    rectangle.Points = ReadPoints(br, type, payloadEnd);
                    break;
                case ShapeType.Circle:
                    var circle = (DrawCircle)shape;
                    circle.RadiusX = br.ReadSingle();
                    circle.RadiusY = br.ReadSingle();
                    circle.IsEllipse = br.ReadBoolean();
                    circle.Points = ReadPoints(br, type, payloadEnd);
                    break;
                case ShapeType.Polygon:
                    var polygon = (DrawPolygon)shape;
                    polygon.SideCount = br.ReadInt32();
                    polygon.IsStar = br.ReadBoolean();
                    polygon.Points = ReadPoints(br, type, payloadEnd);
                   // polygon.MarkGeometryDirty();
                    break;
                case ShapeType.Arc:
                    var arc = (DrawArc)shape;
                    arc.Points = ReadPoints(br, type, payloadEnd);
                    break;
                case ShapeType.Bezier:
                    var bezier = (DrawBezier)shape;
                    bezier.IsClosed = br.ReadBoolean();
                    bezier.Points = ReadPoints(br, type, payloadEnd);
                  //  bezier.MarkGeometryDirty();
                    break;
                case ShapeType.ArbitraryCurve:
                    var arbitraryCurve = (DrawArbitraryCurve)shape;
                    arbitraryCurve.IsClosed = br.ReadBoolean();
                    arbitraryCurve.Points = ReadPoints(br, type, payloadEnd);
                    break;
                case ShapeType.Text:
                    var text = (DrawText)shape;
                    text.Points = ReadPoints(br, type, payloadEnd);
                    text.TextModel = ReadTextPayload(br);
                    break;
                case ShapeType.CubicPath:
                    var cubic = (DrawCubicPath)shape;
                    cubic.IsClosed = br.ReadBoolean();
                    cubic.Points = ReadPoints(br, type, payloadEnd, nameof(cubic.Points));
                    cubic.ControlHandles = ReadPoints(br, type, payloadEnd, nameof(cubic.ControlHandles));
                    cubic.Initialize(cubic.Points, cubic.ControlHandles);
                    break;
                case ShapeType.Combination:
                case ShapeType.Group:
                    break;
                case ShapeType.Hatch:
                    var hatch = (DrawingHatch)shape;
                    var boundaries = ReadShapeList(br, storageVersion);
                    hatch.Boundaries.AddRange(boundaries);
                    break;
                default:
                    throw new InvalidDataException($"高速 DRW 暂不支持图元类型: {type}");
            }

            // hatch 参数作为可选尾字段挂在各类可填充图形后面。
            TryReadHatchParamPayload(br, shape, payloadEnd);
        }

        /// <summary>
        /// 容器图元在子节点恢复后需要重新建立边界缓存。
        /// ChildCollection 的 Add 已自动订阅 BoundingBoxInvalidated 事件。
        /// </summary>
        private static void RebuildContainerGeometry(DrawObject shape)
        {
            switch (shape)
            {
                case DrawCombination combination:
                    combination.RebuildFromChildren();
                    break;
                case DrawingGroup group:
                    group.UpdateSetProperty(new List<SKPoint>());
                    break;
                case DrawingHatch hatch:
                    hatch.UpdateSetProperty(new List<SKPoint>());
                    break;
            }
        }

        private static ShapeHeader ReadShapeHeader(BinaryReader br, DrawObject shape)
        {
            shape.UId = br.ReadInt32();
            shape.LayerId = br.ReadInt32();
            shape.IsVisible = br.ReadBoolean();
            shape.IsLocked = br.ReadBoolean();
            shape.IsClockwise = br.ReadBoolean();
            shape.Name = br.ReadString();
            var persistedSharpCenter = new SKPoint(br.ReadSingle(), br.ReadSingle());
            var persistedWidth = br.ReadSingle();
            var persistedHeight = br.ReadSingle();

            var persistedRotation = br.ReadSingle();
            var persistedScaleX = br.ReadSingle();
            var persistedScaleY = br.ReadSingle();
            var persistedSkewX = br.ReadSingle();
            var persistedSkewY = br.ReadSingle();

            shape.Rotation = persistedRotation;
            shape.ScaleX = persistedScaleX;
            shape.ScaleY = persistedScaleY;
            shape.SkewX = persistedSkewX;
            shape.SkewY = persistedSkewY;

            var header = new ShapeHeader
            {
                SharpCenter = persistedSharpCenter,
                Width = persistedWidth,
                Height = persistedHeight,
                Rotation = persistedRotation,
                ScaleX = persistedScaleX,
                ScaleY = persistedScaleY,
                SkewX = persistedSkewX,
                SkewY = persistedSkewY
            };
            return header;
        }

        private static List<SKPoint> ReadPoints(
            BinaryReader br,
            ShapeType? type = null,
            long? payloadEnd = null,
            string fieldName = "Points")
        {
            if (payloadEnd.HasValue)
            {
                var countBytesAvailable = br.BaseStream.Position + sizeof(int) <= payloadEnd.Value;
                if (!countBytesAvailable)
                {
                    var remainingBytes = payloadEnd.Value - br.BaseStream.Position;
                    throw new InvalidDataException($"DRW 图元 {type} 的 {fieldName} 点数量字段不完整，剩余 {remainingBytes} 字节。");
                }
            }

            var count = br.ReadInt32();
            if (count < 0)
            {
                throw new InvalidDataException($"DRW 图元 {type} 的 {fieldName} 点数量无效: {count}。");
            }

            if (payloadEnd.HasValue)
            {
                var pointBytes = count * sizeof(float) * 2L;
                var remainingBytes = payloadEnd.Value - br.BaseStream.Position;
                var hasEnoughBytes = remainingBytes >= pointBytes;
                if (!hasEnoughBytes)
                {
                    throw new InvalidDataException($"DRW 图元 {type} 的 {fieldName} 点数据不完整，点数量 {count}，需要 {pointBytes} 字节，剩余 {remainingBytes} 字节。");
                }
            }

            var points = new List<SKPoint>(count);
            for (var i = 0; i < count; i++)
            {
                points.Add(new SKPoint(br.ReadSingle(), br.ReadSingle()));
            }
            return points;
        }

        private static SKMatrix ReadPersistedTransform(
            BinaryReader br,
            ShapeType type,
            long payloadEnd)
        {
            var remainingBytes = payloadEnd - br.BaseStream.Position;
            var hasTransformPayload = remainingBytes == PersistedTransformSize;
            if (!hasTransformPayload)
            {
                throw new InvalidDataException($"DRW v{MatrixStorageVersion} 图元 {type} 的变换矩阵字段长度无效，剩余 {remainingBytes} 字节。");
            }

            var transformMagic = br.ReadUInt32();
            var hasExpectedMagic = transformMagic == PersistedTransformMagic;
            if (!hasExpectedMagic)
            {
                throw new InvalidDataException($"DRW v{MatrixStorageVersion} 图元 {type} 的变换矩阵标识无效。");
            }

            var transform = new SKMatrix
            {
                ScaleX = br.ReadSingle(),
                SkewX = br.ReadSingle(),
                TransX = br.ReadSingle(),
                SkewY = br.ReadSingle(),
                ScaleY = br.ReadSingle(),
                TransY = br.ReadSingle(),
                Persp0 = 0f,
                Persp1 = 0f,
                Persp2 = 1f
            };
            return transform;
        }

        private static List<DrawObject> ReadShapeList(
            BinaryReader br,
            int storageVersion)
        {
            var count = br.ReadInt32();
            var result = new List<DrawObject>(count);
            for (var i = 0; i < count; i++)
            {
                var shape = ReadShapeRecord(br, storageVersion);
                result.Add(shape);
            }

            return result;
        }

        private static TextModel ReadTextPayload(BinaryReader br)
        {
            return new TextModel
            {
                Text = br.ReadString(),
                FontSettings = new FontSettings
                {
                    FontFamily = br.ReadString(),
                    FontSize = br.ReadSingle(),
                    IsBold = br.ReadBoolean(),
                    IsItalic = br.ReadBoolean(),
                    IsUnderline = br.ReadBoolean(),
                    IsVerticalLayout = br.ReadBoolean(),
                    HorizontalAlign = (SKTextAlign)br.ReadInt32(),
                    LineHeight = br.ReadSingle(),
                    CharacterSpacing = br.ReadSingle(),
                    TextColor = ReadColor(br)
                }
            };
        }

        private static Dictionary<int, byte[]> ReadLayerPayloadEntries(BinaryReader br)
        {
            var count = br.ReadInt32();
            var result = new Dictionary<int, byte[]>(count);
            for (var i = 0; i < count; i++)
            {
                var layerIndex = br.ReadInt32();
                var payloadLength = br.ReadInt32();
                result[layerIndex] = br.ReadBytes(payloadLength);
            }

            return result;
        }

        private static Dictionary<string, byte[]> ReadExtensionPayloadEntries(BinaryReader br)
        {
            var count = br.ReadInt32();
            var result = new Dictionary<string, byte[]>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = br.ReadString();
                var payloadLength = br.ReadInt32();
                result[key] = br.ReadBytes(payloadLength);
            }

            return result;
        }

        /// <summary>
        /// 写入完整 drw 文件，包括 section 表和各 section 内容。
        /// </summary>
        private static void WriteDocument(
            BinaryWriter bw,
            ICanvasData canvas,
            IReadOnlyDictionary<int, byte[]> layerPayloads,
            IReadOnlyDictionary<string, byte[]> extensionPayloads,
            int storageVersion)
        {
            var sectionsToWrite = new List<SectionEntryBuilder>
            {
                new(SectionType.CanvasMeta, writer => WriteCanvasMetaSection(writer, canvas)),
                new(SectionType.Geometry, writer => WriteGeometrySection(writer, canvas, storageVersion))
            };

            if (layerPayloads.Count > 0)
            {
                sectionsToWrite.Add(new SectionEntryBuilder(
                    SectionType.MarkCardPayload,
                    writer => WriteLayerPayloadEntries(writer, layerPayloads)));
            }

            if (extensionPayloads.Count > 0)
            {
                sectionsToWrite.Add(new SectionEntryBuilder(
                    SectionType.ExtensionPayloads,
                    writer => WriteExtensionPayloadEntries(writer, extensionPayloads)));
            }

            var sectionCount = sectionsToWrite.Count;
            var sectionTableOffset = FileHeaderSize;
            var firstSectionOffset = sectionTableOffset + (sectionCount * SectionHeaderSize);

            bw.Write(GeometryMagic);
            bw.Write(storageVersion);
            bw.Write(0);
            bw.Write(sectionCount);

            for (var i = 0; i < sectionCount; i++)
            {
                bw.Write(0);
                bw.Write(0L);
                bw.Write(0L);
                bw.Write(0);
            }

            bw.BaseStream.Seek(firstSectionOffset, SeekOrigin.Begin);

            var sections = sectionsToWrite
                .Select(section => WriteSection(bw, section.Type, section.WriteAction, section.Flags))
                .ToArray();

            bw.BaseStream.Seek(sectionTableOffset, SeekOrigin.Begin);
            foreach (var section in sections)
            {
                bw.Write((int)section.Type);
                bw.Write(section.Offset);
                bw.Write(section.Length);
                bw.Write(section.Flags);
            }
        }

        /// <summary>
        /// 将已恢复出的点集重新灌回图元内部缓存，确保后续渲染/命中测试正常。
        /// </summary>
        private static void RehydrateLoadedShapeV1(
            DrawObject shape,
            ShapeType type,
            ShapeHeader header,
            SKMatrix persistedTransform)
        {
            shape.Type = type;

            switch (shape)
            {
                case DrawDot dot when dot.Points.Count >= 1:
                    var persistedDotPoints = CopyPoints(dot.Points);
                    // DrawDot 的本地几何是固定在原点的圆，且 dot.Points 只在首次 UpdateSetProperty 时
                    // 与 SharpCenter 同步——后续 Translate 只更新 Matrix，不回写 Points。
                    // 因此 persisted Points[0] 可能与 header.SharpCenter 不一致；载入时必须以
                    // header.SharpCenter 为权威世界位置，避免 UpdateSetProperty 与 RestoreLoadedTransform
                    // 叠加平移导致重复位移。
                    RestoreLoadedTransform(dot, header);
                    dot.Points = persistedDotPoints;
                    break;
                case DrawPolyLines polyLine when polyLine.Points.Count >= 2:
                    var persistedPolyLinePoints = CopyPoints(polyLine.Points);
                    var polyLineLocalPoints = MapPayloadPointsToLocal(persistedPolyLinePoints, persistedTransform);
                    polyLine.UpdateSetProperty(polyLineLocalPoints);
                    RestoreLoadedTransform(polyLine, header);
                    polyLine.Points = persistedPolyLinePoints;
                    break;
                case DrawRectangle rectangle when rectangle.Points.Count >= 2:
                    var persistedRectanglePoints = CopyPoints(rectangle.Points);
                    var rectangleLocalPoints = MapPayloadPointsToLocal(persistedRectanglePoints, persistedTransform);
                    rectangle.UpdateSetProperty(rectangleLocalPoints);
                    RestoreLoadedTransform(rectangle, header);
                    rectangle.Points = persistedRectanglePoints;
                    break;
                case DrawCircle circle when circle.Points.Count >= 2:
                    var persistedCirclePoints = CopyPoints(circle.Points);
                    var circleRadiusX = circle.RadiusX;
                    var circleRadiusY = circle.RadiusY;
                    var hasPersistedRadius = circleRadiusX > 0.0001f;
                    List<SKPoint> circleLocalPoints;
                    if (hasPersistedRadius)
                    {
                        circleLocalPoints = new List<SKPoint>
                        {
                            new(-circleRadiusX, -circleRadiusY),
                            new(circleRadiusX, circleRadiusY)
                        };
                    }
                    else
                    {
                        circleLocalPoints = MapPayloadPointsToLocal(persistedCirclePoints, persistedTransform);
                    }

                    circle.UpdateSetProperty(circleLocalPoints);
                    RestoreLoadedTransform(circle, header);
                    circle.Points = persistedCirclePoints;
                    break;
                case DrawPolygon polygon when polygon.Points.Count >= 3:
                    var persistedPolygonPoints = CopyPoints(polygon.Points);
                    var polygonLocalPoints = MapPayloadPointsToLocal(persistedPolygonPoints, persistedTransform);
                    polygon.UpdateSetProperty(polygonLocalPoints);
                    RestoreLoadedTransform(polygon, header);
                    polygon.Points = persistedPolygonPoints;
                    break;
                case DrawArc arc when arc.Points.Count >= 3:
                    var persistedArcPoints = CopyPoints(arc.Points);
                    var arcLocalPoints = ResolveArcLocalPoints(persistedArcPoints, header, persistedTransform);
                    arc.UpdateSetProperty(arcLocalPoints);
                    RestoreLoadedArcTransform(arc, header);
                    arc.Points = persistedArcPoints;
                    break;
                case DrawText text when !string.IsNullOrEmpty(text.TextModel?.Text):
                    // DRW 公共头中的 SharpCenter 是文本视觉中心的权威值。
                    // Text payload 保存运行时锚点；旧文件可能保存了中心点，需用 header 兜底反推锚点。
                    RehydrateLoadedText(text, header);
                    break;
                case DrawBezier bezier when bezier.Points.Count >= 2:
                    var persistedBezierPoints = CopyPoints(bezier.Points);
                    var bezierLocalPoints = ResolveBezierLocalPoints(persistedBezierPoints, header, persistedTransform);
                    bezier.UpdateSetProperty(bezierLocalPoints);
                    RestoreLoadedTransform(bezier, header);
                    bezier.Points = persistedBezierPoints;
                    break;
                case DrawArbitraryCurve arbitraryCurve when arbitraryCurve.Points.Count >= 2:
                    var persistedArbitraryCurvePoints = CopyPoints(arbitraryCurve.Points);
                    // DrawArbitraryCurve.Points 是曲线的作者坐标；最终矩阵在渲染时才把
                    // _localPoints 映射到世界空间。该 payload 不是世界点，不能做逆矩阵。
                    var arbitraryCurveLocalPoints = ResolveArbitraryCurveLocalPoints(
                        persistedArbitraryCurvePoints,
                        header,
                        persistedTransform);
                    arbitraryCurve.UpdateSetProperty(arbitraryCurveLocalPoints);
                    RestoreLoadedTransform(arbitraryCurve, header);
                    arbitraryCurve.Points = persistedArbitraryCurvePoints;
                    break;
                case DrawCubicPath cubicPath when cubicPath.Points.Count >= 2:
                    var persistedCubicPathPoints = CopyPoints(cubicPath.Points);
                    var persistedCubicPathHandles = CopyPoints(cubicPath.ControlHandles);
                    var cubicPathLocalPoints = MapPayloadPointsToLocal(persistedCubicPathPoints, persistedTransform);
                    var cubicPathLocalHandles = MapPayloadPointsToLocal(persistedCubicPathHandles, persistedTransform);
                    cubicPath.Initialize(cubicPathLocalPoints, cubicPathLocalHandles);
                    RestoreLoadedTransform(cubicPath, header);
                    cubicPath.Points = persistedCubicPathPoints;
                    cubicPath.ControlHandles = persistedCubicPathHandles;
                    break;
            }
        }

        /// <summary>
        /// 按兴诚 DRW v4 的既有字段语义重建运行时图元。
        /// v4 没有最终矩阵尾字段，必须继续按 header 属性恢复。
        /// </summary>
        private static void RehydrateLoadedShapeV4(
            DrawObject shape,
            ShapeType type,
            ShapeHeader header)
        {
            shape.Type = type;

            switch (shape)
            {
                case DrawDot dot when dot.Points.Count >= 1:
                    dot.UpdateSetProperty(dot.Points);
                    RestoreLoadedTransform(dot, header);
                    break;
                case DrawPolyLines polyLine when polyLine.Points.Count >= 2:
                    polyLine.UpdateSetProperty(polyLine.Points);
                    RestoreLoadedTransform(polyLine, header);
                    break;
                case DrawRectangle rectangle when rectangle.Points.Count >= 2:
                    var rectangleLocalPoints = MapPayloadPointsToHeaderLocal(rectangle.Points, header);
                    rectangle.UpdateSetProperty(rectangleLocalPoints);
                    RestoreLoadedTransform(rectangle, header);
                    break;
                case DrawCircle circle when circle.Points.Count >= 2:
                    RestoreLoadedTransform(circle, header);
                    break;
                case DrawPolygon polygon when polygon.Points.Count >= 3:
                    polygon.UpdateSetProperty(polygon.Points);
                    RestoreLoadedTransform(polygon, header);
                    break;
                case DrawArc arc when arc.Points.Count >= 3:
                    var persistedArcPoints = CopyPoints(arc.Points);
                    var arcLocalPoints = ResolveArcLocalPointsV4(persistedArcPoints, header);
                    arc.UpdateSetProperty(arcLocalPoints);
                    RestoreLoadedTransform(arc, header);
                    arc.Points = persistedArcPoints;
                    break;
                case DrawText text when !string.IsNullOrEmpty(text.TextModel?.Text):
                    RehydrateLoadedText(text, header);
                    break;
                case DrawBezier bezier when bezier.Points.Count >= 2:
                    var persistedBezierPoints = CopyPoints(bezier.Points);
                    var bezierLocalPoints = ResolveBezierLocalPointsV4(persistedBezierPoints, header);
                    bezier.UpdateSetProperty(bezierLocalPoints);
                    RestoreLoadedTransform(bezier, header);
                    bezier.Points = persistedBezierPoints;
                    break;
                case DrawArbitraryCurve arbitraryCurve when arbitraryCurve.Points.Count >= 2:
                    var persistedArbitraryCurvePoints = CopyPoints(arbitraryCurve.Points);
                    var arbitraryCurveLocalPoints = ResolveArbitraryCurveLocalPointsV4(
                        persistedArbitraryCurvePoints,
                        header);
                    arbitraryCurve.UpdateSetProperty(arbitraryCurveLocalPoints);
                    RestoreLoadedTransform(arbitraryCurve, header);
                    arbitraryCurve.Points = persistedArbitraryCurvePoints;
                    break;
                case DrawCubicPath cubicPath when cubicPath.Points.Count >= 2:
                    var cubicPathLocalPoints = OffsetPayloadPoints(
                        cubicPath.Points,
                        -header.SharpCenter.X,
                        -header.SharpCenter.Y);
                    var cubicPathLocalHandles = OffsetPayloadPoints(
                        cubicPath.ControlHandles,
                        -header.SharpCenter.X,
                        -header.SharpCenter.Y);
                    cubicPath.Initialize(cubicPathLocalPoints, cubicPathLocalHandles);
                    RestoreLoadedTransform(cubicPath, header);
                    cubicPath.Points = OffsetPayloadPoints(
                        cubicPathLocalPoints,
                        header.SharpCenter.X,
                        header.SharpCenter.Y);
                    cubicPath.ControlHandles = OffsetPayloadPoints(
                        cubicPathLocalHandles,
                        header.SharpCenter.X,
                        header.SharpCenter.Y);
                    break;
            }
        }

        private static void RestoreLoadedTransform(
            DrawObject shape,
            ShapeHeader header)
        {
            shape.Rotation = 0f;
            shape.ScaleX = 1f;
            shape.ScaleY = 1f;
            shape.SkewX = 0f;
            shape.SkewY = 0f;

            shape.Translate(header.SharpCenter.X, header.SharpCenter.Y, commit: true);

            var hasScale = Math.Abs(header.ScaleX - 1f) > 0.0001f ||
                           Math.Abs(header.ScaleY - 1f) > 0.0001f;
            if (hasScale)
            {
                shape.Scale(
                    header.ScaleX,
                    header.ScaleY,
                    header.SharpCenter,
                    shape.GetWorldRotationRad(),
                    commit: true);
            }

            var hasSkew = Math.Abs(header.SkewX) > 0.0001f ||
                          Math.Abs(header.SkewY) > 0.0001f;
            if (hasSkew)
            {
                var skewTanX = MathF.Tan(header.SkewX * MathF.PI / 180f);
                var skewTanY = MathF.Tan(header.SkewY * MathF.PI / 180f);
                shape.Skew(skewTanX, skewTanY, header.SharpCenter, commit: true);
            }

            var hasRotation = Math.Abs(header.Rotation) > 0.0001f;
            if (hasRotation)
            {
                shape.Rotate(header.Rotation, header.SharpCenter, commit: true);
            }

            shape.Rotation = header.Rotation;
            shape.ScaleX = header.ScaleX;
            shape.ScaleY = header.ScaleY;
            shape.SkewX = header.SkewX;
            shape.SkewY = header.SkewY;
            shape.SetRotationCenter(header.SharpCenter);
        }

        private static void RestoreLoadedArcTransform(
            DrawArc arc,
            ShapeHeader header)
        {
            arc.Rotation = 0f;
            arc.ScaleX = 1f;
            arc.ScaleY = 1f;
            arc.SkewX = 0f;
            arc.SkewY = 0f;

            var hasScale = Math.Abs(header.ScaleX - 1f) > 0.0001f ||
                           Math.Abs(header.ScaleY - 1f) > 0.0001f;
            if (hasScale)
            {
                arc.Scale(
                    header.ScaleX,
                    header.ScaleY,
                    SKPoint.Empty,
                    arc.GetWorldRotationRad(),
                    commit: true);
            }

            var hasSkew = Math.Abs(header.SkewX) > 0.0001f ||
                          Math.Abs(header.SkewY) > 0.0001f;
            if (hasSkew)
            {
                var skewTanX = MathF.Tan(header.SkewX * MathF.PI / 180f);
                var skewTanY = MathF.Tan(header.SkewY * MathF.PI / 180f);
                arc.Skew(skewTanX, skewTanY, SKPoint.Empty, commit: true);
            }

            var hasRotation = Math.Abs(header.Rotation) > 0.0001f;
            if (hasRotation)
            {
                arc.Rotate(header.Rotation, SKPoint.Empty, commit: true);
            }

            var arcBounds = arc.GetOBB();
            var currentCenter = arcBounds.Center;
            var offsetX = header.SharpCenter.X - currentCenter.X;
            var offsetY = header.SharpCenter.Y - currentCenter.Y;
            arc.Translate(offsetX, offsetY, commit: true);
            arc.Rotation = header.Rotation;
            arc.ScaleX = header.ScaleX;
            arc.ScaleY = header.ScaleY;
            arc.SkewX = header.SkewX;
            arc.SkewY = header.SkewY;
            arc.SetRotationCenter(header.SharpCenter);
        }

        private static void RestorePersistedTransform(
            DrawObject shape,
            ShapeHeader header,
            SKMatrix persistedTransform)
        {
            var snapshot = new TransformCommandSnapshot(
                persistedTransform,
                header.Rotation,
                header.ScaleX,
                header.ScaleY,
                header.SkewX,
                header.SkewY,
                header.SharpCenter,
                SKPoint.Empty,
                SKPoint.Empty);
            shape.RestoreTransformCommandSnapshot(snapshot);
        }

        private static List<SKPoint> MapPayloadPointsToLocal(
            IReadOnlyList<SKPoint> points,
            SKMatrix persistedTransform)
        {
            var hasInverse = persistedTransform.TryInvert(out var inverse);
            if (!hasInverse)
            {
                var fallbackPoints = CopyPoints(points);
                return fallbackPoints;
            }

            var localPoints = new List<SKPoint>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                var localPoint = inverse.MapPoint(point);
                localPoints.Add(localPoint);
            }

            return localPoints;
        }

        private static List<SKPoint> MapPayloadPointsToHeaderLocal(
            IReadOnlyList<SKPoint> points,
            ShapeHeader header)
        {
            var headerMatrix = BuildHeaderTransformMatrix(header);
            var hasInverse = headerMatrix.TryInvert(out var inverse);
            if (!hasInverse)
            {
                var fallbackPoints = OffsetPayloadPoints(
                    points,
                    -header.SharpCenter.X,
                    -header.SharpCenter.Y);
                return fallbackPoints;
            }

            var localPoints = new List<SKPoint>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                var localPoint = inverse.MapPoint(point);
                localPoints.Add(localPoint);
            }

            return localPoints;
        }

        private static SKMatrix BuildHeaderTransformMatrix(ShapeHeader header)
        {
            var matrix = SKMatrix.CreateIdentity();

            var hasScale = Math.Abs(header.ScaleX - 1f) > 0.0001f ||
                           Math.Abs(header.ScaleY - 1f) > 0.0001f;
            if (hasScale)
            {
                var scaleMatrix = SKMatrix.CreateScale(header.ScaleX, header.ScaleY, 0f, 0f);
                matrix = matrix.PostConcat(scaleMatrix);
            }

            var hasSkew = Math.Abs(header.SkewX) > 0.0001f ||
                          Math.Abs(header.SkewY) > 0.0001f;
            if (hasSkew)
            {
                var skewTanX = MathF.Tan(header.SkewX * MathF.PI / 180f);
                var skewTanY = MathF.Tan(header.SkewY * MathF.PI / 180f);
                var skewMatrix = SKMatrix.CreateSkew(skewTanX, skewTanY);
                matrix = matrix.PostConcat(skewMatrix);
            }

            var hasRotation = Math.Abs(header.Rotation) > 0.0001f;
            if (hasRotation)
            {
                var rotationMatrix = SKMatrix.CreateRotationDegrees(header.Rotation, 0f, 0f);
                matrix = matrix.PostConcat(rotationMatrix);
            }

            var translationMatrix = SKMatrix.CreateTranslation(
                header.SharpCenter.X,
                header.SharpCenter.Y);
            matrix = matrix.PostConcat(translationMatrix);
            return matrix;
        }

        private static List<SKPoint> OffsetPayloadPoints(IReadOnlyList<SKPoint> points, float offsetX, float offsetY)
        {
            var offsetPoints = new List<SKPoint>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                var offsetPoint = new SKPoint(
                    point.X + offsetX,
                    point.Y + offsetY);
                offsetPoints.Add(offsetPoint);
            }

            return offsetPoints;
        }

        private static List<SKPoint> ResolveBezierLocalPoints(
            IReadOnlyList<SKPoint> persistedPoints,
            ShapeHeader header,
            SKMatrix persistedTransform)
        {
            var offsetLocalPoints = OffsetPayloadPoints(
                persistedPoints,
                -header.SharpCenter.X,
                -header.SharpCenter.Y);

            var inverseLocalPoints = MapPayloadPointsToLocal(persistedPoints, persistedTransform);
            var offsetError = MeasureBezierHeaderError(offsetLocalPoints, header, persistedTransform);
            var inverseError = MeasureBezierHeaderError(inverseLocalPoints, header, persistedTransform);
            var shouldUseInverse = inverseError + 0.01f < offsetError;
            if (shouldUseInverse)
            {
                return inverseLocalPoints;
            }

            return offsetLocalPoints;
        }

        private static List<SKPoint> ResolveBezierLocalPointsV4(
            IReadOnlyList<SKPoint> persistedPoints,
            ShapeHeader header)
        {
            var offsetLocalPoints = OffsetPayloadPoints(
                persistedPoints,
                -header.SharpCenter.X,
                -header.SharpCenter.Y);
            var inverseLocalPoints = MapPayloadPointsToHeaderLocal(persistedPoints, header);
            var offsetError = MeasureBezierHeaderErrorV4(offsetLocalPoints, header);
            var inverseError = MeasureBezierHeaderErrorV4(inverseLocalPoints, header);
            var shouldUseInverse = inverseError + 0.01f < offsetError;
            if (shouldUseInverse)
            {
                return inverseLocalPoints;
            }

            return offsetLocalPoints;
        }

        private static List<SKPoint> ResolveArbitraryCurveLocalPoints(
            IReadOnlyList<SKPoint> persistedPoints,
            ShapeHeader header,
            SKMatrix persistedTransform)
        {
            var rawLocalPoints = CopyPoints(persistedPoints);
            var offsetLocalPoints = OffsetPayloadPoints(
                persistedPoints,
                -header.SharpCenter.X,
                -header.SharpCenter.Y);
            var inverseLocalPoints = MapPayloadPointsToLocal(persistedPoints, persistedTransform);

            var bestPoints = rawLocalPoints;
            var bestError = MeasureArbitraryCurveHeaderError(rawLocalPoints, header, persistedTransform);

            var offsetError = MeasureArbitraryCurveHeaderError(offsetLocalPoints, header, persistedTransform);
            if (offsetError + 0.01f < bestError)
            {
                bestPoints = offsetLocalPoints;
                bestError = offsetError;
            }

            var inverseError = MeasureArbitraryCurveHeaderError(inverseLocalPoints, header, persistedTransform);
            if (inverseError + 0.01f < bestError)
            {
                bestPoints = inverseLocalPoints;
            }

            return bestPoints;
        }

        private static List<SKPoint> ResolveArbitraryCurveLocalPointsV4(
            IReadOnlyList<SKPoint> persistedPoints,
            ShapeHeader header)
        {
            var rawLocalPoints = CopyPoints(persistedPoints);
            var offsetLocalPoints = OffsetPayloadPoints(
                persistedPoints,
                -header.SharpCenter.X,
                -header.SharpCenter.Y);
            var inverseLocalPoints = MapPayloadPointsToHeaderLocal(persistedPoints, header);

            var bestPoints = rawLocalPoints;
            var bestError = MeasureArbitraryCurveHeaderErrorV4(rawLocalPoints, header);

            var offsetError = MeasureArbitraryCurveHeaderErrorV4(offsetLocalPoints, header);
            if (offsetError + 0.01f < bestError)
            {
                bestPoints = offsetLocalPoints;
                bestError = offsetError;
            }

            var inverseError = MeasureArbitraryCurveHeaderErrorV4(inverseLocalPoints, header);
            if (inverseError + 0.01f < bestError)
            {
                bestPoints = inverseLocalPoints;
            }

            return bestPoints;
        }

        private static List<SKPoint> ResolveArcLocalPoints(
            IReadOnlyList<SKPoint> persistedPoints,
            ShapeHeader header,
            SKMatrix persistedTransform)
        {
            var rawLocalPoints = CopyPoints(persistedPoints);
            return rawLocalPoints;
        }

        private static List<SKPoint> ResolveArcLocalPointsV4(
            IReadOnlyList<SKPoint> persistedPoints,
            ShapeHeader header)
        {
            var rawLocalPoints = CopyPoints(persistedPoints);
            var offsetLocalPoints = OffsetPayloadPoints(
                persistedPoints,
                -header.SharpCenter.X,
                -header.SharpCenter.Y);
            var centeredLocalPoints = CenterArcLocalPoints(persistedPoints);
            var inverseLocalPoints = MapPayloadPointsToHeaderLocal(persistedPoints, header);

            var bestPoints = rawLocalPoints;
            var bestError = MeasureArcHeaderErrorV4(rawLocalPoints, header);

            var offsetError = MeasureArcHeaderErrorV4(offsetLocalPoints, header);
            var offsetIsBetter = offsetError + 0.01f < bestError;
            if (offsetIsBetter)
            {
                bestPoints = offsetLocalPoints;
                bestError = offsetError;
            }

            var centeredError = MeasureArcHeaderErrorV4(centeredLocalPoints, header);
            var centeredIsBetter = centeredError + 0.01f < bestError;
            if (centeredIsBetter)
            {
                bestPoints = centeredLocalPoints;
                bestError = centeredError;
            }

            var inverseError = MeasureArcHeaderErrorV4(inverseLocalPoints, header);
            var inverseIsBetter = inverseError + 0.01f < bestError;
            if (inverseIsBetter)
            {
                bestPoints = inverseLocalPoints;
            }

            return bestPoints;
        }

        private static List<SKPoint> CenterArcLocalPoints(IReadOnlyList<SKPoint> points)
        {
            var probePoints = CopyPoints(points);
            var probe = new DrawArc();
            probe.UpdateSetProperty(probePoints);

            var localBounds = probe.GetLocalBounds();
            if (localBounds.IsEmpty)
            {
                var fallbackPoints = CopyPoints(points);
                return fallbackPoints;
            }

            var centeredPoints = OffsetPayloadPoints(points, -localBounds.MidX, -localBounds.MidY);
            return centeredPoints;
        }

        private static float MeasureBezierHeaderError(
            List<SKPoint> localPoints,
            ShapeHeader header,
            SKMatrix persistedTransform)
        {
            var probePoints = CopyPoints(localPoints);
            var probe = new DrawBezier(probePoints);
            RestorePersistedTransform(probe, header, persistedTransform);

            var bounds = probe.GetAABB();
            var centerDeltaX = probe.SharpCenter.X - header.SharpCenter.X;
            var centerDeltaY = probe.SharpCenter.Y - header.SharpCenter.Y;
            var widthDelta = bounds.Width - header.Width;
            var heightDelta = bounds.Height - header.Height;

            var centerError = Math.Abs(centerDeltaX) + Math.Abs(centerDeltaY);
            var sizeError = Math.Abs(widthDelta) + Math.Abs(heightDelta);
            var error = centerError + sizeError;
            return error;
        }

        private static float MeasureBezierHeaderErrorV4(
            List<SKPoint> localPoints,
            ShapeHeader header)
        {
            var probePoints = CopyPoints(localPoints);
            var probe = new DrawBezier(probePoints);
            RestoreLoadedTransform(probe, header);

            var bounds = probe.GetAABB();
            var centerDeltaX = probe.SharpCenter.X - header.SharpCenter.X;
            var centerDeltaY = probe.SharpCenter.Y - header.SharpCenter.Y;
            var widthDelta = bounds.Width - header.Width;
            var heightDelta = bounds.Height - header.Height;

            var centerError = Math.Abs(centerDeltaX) + Math.Abs(centerDeltaY);
            var sizeError = Math.Abs(widthDelta) + Math.Abs(heightDelta);
            var error = centerError + sizeError;
            return error;
        }

        private static float MeasureArbitraryCurveHeaderError(
            List<SKPoint> localPoints,
            ShapeHeader header,
            SKMatrix persistedTransform)
        {
            var probePoints = CopyPoints(localPoints);
            var probe = new DrawArbitraryCurve(probePoints);
            RestorePersistedTransform(probe, header, persistedTransform);

            var bounds = probe.GetAABB();
            var centerDeltaX = probe.SharpCenter.X - header.SharpCenter.X;
            var centerDeltaY = probe.SharpCenter.Y - header.SharpCenter.Y;
            var widthDelta = bounds.Width - header.Width;
            var heightDelta = bounds.Height - header.Height;

            var centerError = Math.Abs(centerDeltaX) + Math.Abs(centerDeltaY);
            var sizeError = Math.Abs(widthDelta) + Math.Abs(heightDelta);
            var error = centerError + sizeError;
            return error;
        }

        private static float MeasureArbitraryCurveHeaderErrorV4(
            List<SKPoint> localPoints,
            ShapeHeader header)
        {
            var probePoints = CopyPoints(localPoints);
            var probe = new DrawArbitraryCurve(probePoints);
            RestoreLoadedTransform(probe, header);

            var bounds = probe.GetAABB();
            var centerDeltaX = probe.SharpCenter.X - header.SharpCenter.X;
            var centerDeltaY = probe.SharpCenter.Y - header.SharpCenter.Y;
            var widthDelta = bounds.Width - header.Width;
            var heightDelta = bounds.Height - header.Height;

            var centerError = Math.Abs(centerDeltaX) + Math.Abs(centerDeltaY);
            var sizeError = Math.Abs(widthDelta) + Math.Abs(heightDelta);
            var error = centerError + sizeError;
            return error;
        }

        private static float MeasureArcHeaderError(
            List<SKPoint> localPoints,
            ShapeHeader header,
            SKMatrix persistedTransform)
        {
            var probePoints = CopyPoints(localPoints);
            var probe = new DrawArc();
            probe.UpdateSetProperty(probePoints);
            RestorePersistedTransform(probe, header, persistedTransform);

            var bounds = probe.GetAABB();
            var centerDeltaX = probe.SharpCenter.X - header.SharpCenter.X;
            var centerDeltaY = probe.SharpCenter.Y - header.SharpCenter.Y;
            var widthDelta = bounds.Width - header.Width;
            var heightDelta = bounds.Height - header.Height;

            var centerError = Math.Abs(centerDeltaX) + Math.Abs(centerDeltaY);
            var sizeError = Math.Abs(widthDelta) + Math.Abs(heightDelta);
            var error = centerError + sizeError;
            return error;
        }

        private static float MeasureArcHeaderErrorV4(
            List<SKPoint> localPoints,
            ShapeHeader header)
        {
            var probePoints = CopyPoints(localPoints);
            var probe = new DrawArc();
            probe.UpdateSetProperty(probePoints);
            RestoreLoadedTransform(probe, header);

            var bounds = probe.GetAABB();
            var centerDeltaX = probe.SharpCenter.X - header.SharpCenter.X;
            var centerDeltaY = probe.SharpCenter.Y - header.SharpCenter.Y;
            var widthDelta = bounds.Width - header.Width;
            var heightDelta = bounds.Height - header.Height;

            var centerError = Math.Abs(centerDeltaX) + Math.Abs(centerDeltaY);
            var sizeError = Math.Abs(widthDelta) + Math.Abs(heightDelta);
            var error = centerError + sizeError;
            return error;
        }

        private static List<SKPoint> CopyPoints(IReadOnlyList<SKPoint> points)
        {
            var copiedPoints = new List<SKPoint>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                copiedPoints.Add(point);
            }

            return copiedPoints;
        }

        private static void RehydrateLoadedText(
            DrawText text,
            ShapeHeader header)
        {
            // 局部路径为字体坐标系（Y 向下），先经统一接口把 Y 翻转烘焙进矩阵，
            // 后续 RestoreLoadedTransform 的各算子在其上叠加，与旧约定（矩阵作用于
            // Y 向上局部路径）产生的世界几何逐点一致。
            text.BakeFontFlipIntoMatrix();

            var textAnchor = ResolveLoadedTextAnchor(text, header.SharpCenter);

            var anchorPoints = new List<SKPoint> { textAnchor };
            text.UpdateSetProperty(anchorPoints);
            RestoreLoadedTransform(text, header);
        }

        private static SKPoint ResolveLoadedTextAnchor(DrawText text, SKPoint persistedSharpCenter)
        {
            if (text.Points.Count > 0)
            {
                var payloadPoint = text.Points[0];
                var payloadIsHeaderCenter = ArePointsClose(payloadPoint, persistedSharpCenter);
                if (!payloadIsHeaderCenter)
                {
                    return payloadPoint;
                }
            }

            var localCenter = MeasureTextLocalCenter(text);
            var textAnchor = new SKPoint(
                persistedSharpCenter.X - localCenter.X,
                persistedSharpCenter.Y - localCenter.Y);
            return textAnchor;
        }

        private static SKPoint MeasureTextLocalCenter(DrawText text)
        {
            var originPoints = new List<SKPoint> { SKPoint.Empty };
            text.UpdateSetProperty(originPoints);

            var localBounds = text.GetLocalBounds();
            if (localBounds.IsEmpty)
            {
                return SKPoint.Empty;
            }

            var localCenter = new SKPoint(localBounds.MidX, localBounds.MidY);
            // 局部包围盒位于字体坐标系（Y 向下），经已烘焙翻转的矩阵映射回画布系，
            // 保持与旧约定下的局部中心值一致（此处矩阵仅含翻转，尚无平移）。
            var localCenterCanvas = text.GetTransformMatrix().MapPoint(localCenter);
            return localCenterCanvas;
        }

        private static bool ArePointsClose(SKPoint first, SKPoint second)
        {
            var deltaX = first.X - second.X;
            var deltaY = first.Y - second.Y;
            var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            var areClose = distanceSquared <= 0.0001f;
            return areClose;
        }

        /// <summary>
        /// 几何加载完成后，恢复填充图形的 TargetShapes 与 HatchParamInfo。
        /// </summary>
        private static void RestoreLoadedHatchState(IReadOnlyList<DrawingLayer> layers)
        {
            var actualShapes = new Dictionary<int, DrawObject>();
            var hatches = new List<DrawingHatch>();

            foreach (var layer in layers)
            {
                foreach (var shape in layer.AllShapesInternal.OfType<DrawObject>())
                {
                    IndexLoadedShapes(shape, actualShapes, hatches);
                }
            }

            foreach (var hatch in hatches)
            {
                RebindLoadedHatchTargets(hatch, actualShapes);
            }
        }

        /// <summary>
        /// 为当前批次加载出的图元建立 UId 索引，同时收集所有填充图元。
        /// </summary>
        private static void IndexLoadedShapes(
            DrawObject shape,
            IDictionary<int, DrawObject> shapeIndex,
            IList<DrawingHatch> hatches)
        {
            if (!shapeIndex.ContainsKey(shape.UId))
            {
                shapeIndex[shape.UId] = shape;
            }

            if (shape is DrawingHatch hatch)
            {
                hatches.Add(hatch);
            }

            if (shape is not IContainer container)
            {
                return;
            }

            foreach (var child in container.Children.OfType<DrawObject>())
            {
                IndexLoadedShapes(child, shapeIndex, hatches);
            }
        }

        /// <summary>
        /// 将 hatch.TargetShapes 中的反序列化副本替换成画布里的真实图元引用。
        /// DrawingHatch.HatchParamInfo 为真相源，将其同步到 TargetShapes 的 IHatchable 供渲染使用。
        /// </summary>
        private static void RebindLoadedHatchTargets(
            DrawingHatch hatch,
            IReadOnlyDictionary<int, DrawObject> shapeIndex)
        {
            if (hatch.Boundaries.Count == 0)
            {
                return;
            }

            var reboundTargets = new List<IShape>(hatch.Boundaries.Count);

            foreach (var targetShape in hatch.Boundaries)
            {
                if (targetShape is DrawObject persistedTarget
                    && shapeIndex.TryGetValue(persistedTarget.UId, out var actualTarget))
                {
                    // drw 中 hatch.TargetShapes 先读出来的是独立副本，这里按 UId 还原为真实对象。
                    // 从 DrawingHatch.HatchParamInfo（真相源）回写到真实图元的 IHatchable，供渲染使用。
                    if (actualTarget is IHatchable actualHatchable)
                    {
                        if (actualHatchable.HatchParamInfo == null && hatch.HatchParamInfo != null)
                        {
                            actualHatchable.HatchParamInfo = CloneHatchParam(hatch.HatchParamInfo);
                        }
                    }
                    // 兼容旧文件：如果 DrawingHatch.HatchParamInfo 为 null 但副本上有参数
                    if (hatch.HatchParamInfo == null
                        && persistedTarget is IHatchable persistedHatchable
                        && persistedHatchable.HatchParamInfo != null)
                    {
                        hatch.HatchParamInfo = CloneHatchParam(persistedHatchable.HatchParamInfo);
                        if (actualTarget is IHatchable actualHatch && actualHatch.HatchParamInfo == null)
                            actualHatch.HatchParamInfo = CloneHatchParam(persistedHatchable.HatchParamInfo);
                    }

                    reboundTargets.Add(actualTarget);
                    continue;
                }

                reboundTargets.Add(targetShape);
            }

            hatch.Boundaries.Clear();
            hatch.Boundaries.AddRange(reboundTargets);
        }

        /// <summary>
        /// 将单个 section 写入正文，并记录其偏移和长度。
        /// </summary>
        private static SectionEntry WriteSection(BinaryWriter bw, SectionType type, Action<BinaryWriter> writeAction, int flags = 0)
        {
            var offset = bw.BaseStream.Position;
            writeAction(bw);
            var length = bw.BaseStream.Position - offset;
            return new SectionEntry(type, offset, length, flags);
        }

        /// <summary>
        /// 写入画布和图层元数据，不包含具体图元。
        /// </summary>
        private static void WriteCanvasMetaSection(BinaryWriter bw, ICanvasData canvas)
        {
            var layers = canvas.Layers;
            bw.Write(layers.Count);
            bw.Write(canvas.Id);
            bw.Write(canvas.Name ?? string.Empty);

            for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                bw.Write(layer.UId);
                bw.Write(layer.Name ?? string.Empty);
                bw.Write(layer.Color ?? "#000000");
                bw.Write(layer.IsVisible);
                bw.Write(layer.IsLocked);
            }
        }

        /// <summary>
        /// 写入所有图层的图元几何数据。
        /// </summary>
        private static void WriteGeometrySection(
            BinaryWriter bw,
            ICanvasData canvas,
            int storageVersion)
        {
            var layers = canvas.Layers;
            bw.Write(layers.Count);
            for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                var shapes = ResolvePersistedLayerShapes(layer);
                bw.Write(shapes.Count);
                for (var i = 0; i < shapes.Count; i++)
                {
                    if (shapes[i] is not DrawObject drawObject)
                        throw new InvalidDataException($"图层包含无法序列化的图元: {shapes[i].GetType().Name}");
                    WriteShapeRecord(bw, drawObject, storageVersion);
                }
            }
        }

        private static List<IShapeData> ResolvePersistedLayerShapes(ILayerData layer)
        {
            var result = new List<IShapeData>();
            if (layer is DrawingLayer drawingLayer)
            {
                var internalShapes = drawingLayer.AllShapesInternal;
                for (var i = 0; i < internalShapes.Count; i++)
                {
                    var shape = internalShapes[i];
                    AddPersistedLayerShape(result, shape);
                }

                return result;
            }

            var publicShapes = layer.Shapes;
            for (var i = 0; i < publicShapes.Count; i++)
            {
                var shape = publicShapes[i];
                AddPersistedLayerShape(result, shape);
            }

            return result;
        }

        private static void AddPersistedLayerShape(List<IShapeData> result, object shape)
        {
            if (shape is DrawCombination combination && combination.IsBatchedBasicShapes)
            {
                var children = combination.Children;
                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    if (child is not IShapeData childShape)
                    {
                        var childTypeName = child?.GetType().Name ?? "<null>";
                        throw new InvalidDataException($"自动批处理组合包含无法序列化的子图元: {childTypeName}");
                    }

                    result.Add(childShape);
                }

                return;
            }

            if (shape is IShapeData shapeData)
            {
                result.Add(shapeData);
                return;
            }

            throw new InvalidDataException($"图层包含无法序列化的图元: {shape.GetType().Name}");
        }

        /// <summary>
        /// 写入单个图元记录。
        /// payload 会先写到临时流中，以便回填长度。
        /// </summary>
        private static void WriteShapeRecord(
            BinaryWriter bw,
            DrawObject shape,
            int storageVersion)
        {
            var persistedType = ResolvePersistedShapeType(shape);
            bw.Write((byte)persistedType);
            WriteShapeHeader(bw, shape);

            using var payloadStream = new MemoryStream();
            using (var payloadWriter = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteSpecificPayload(payloadWriter, shape, storageVersion);
                var isMatrixStorageVersion = storageVersion == MatrixStorageVersion;
                if (isMatrixStorageVersion)
                {
                    var transform = shape.GetTransformMatrix();
                    if (shape is DrawText)
                    {
                        // 运行时矩阵已烘焙字体 Y 翻转（M = M旧×Flip）；持久化仍按旧约定
                        // 剥离翻转后存 M旧（Flip 为对合矩阵），保持 DRW 格式双向兼容，
                        // 加载侧在 RestorePersistedTransform 后补烘焙。
                        transform = transform.PreConcat(SKMatrix.CreateScale(1f, -1f));
                    }
                    WritePersistedTransform(payloadWriter, transform);
                }
            }

            bw.Write((int)payloadStream.Length);
            payloadStream.Position = 0;
            payloadStream.CopyTo(bw.BaseStream);

            if (shape is IContainer container)
            {
                bw.Write(container.Children.Count);
                for (var i = 0; i < container.Children.Count; i++)
                {
                    if (container.Children[i] is not DrawObject child)
                        throw new InvalidDataException($"容器图元包含无法序列化的子节点: {container.Children[i].GetType().Name}");
                    WriteShapeRecord(bw, child, storageVersion);
                }
            }
        }

        /// <summary>
        /// 将运行时图元映射到 drw 中使用的持久化类型。
        /// </summary>
        private static ShapeType ResolvePersistedShapeType(DrawObject shape) => shape switch
        {
            DrawDot => ShapeType.Point,
            DrawPolyLines => ShapeType.PolyLine,
            DrawRectangle => ShapeType.Rectangle,
            DrawCircle => ShapeType.Circle,
            DrawPolygon => ShapeType.Polygon,
            DrawArc => ShapeType.Arc,
            DrawBezier => ShapeType.Bezier,
            DrawText => ShapeType.Text,
            DrawCombination => ShapeType.Combination,
            DrawingGroup => ShapeType.Group,
            DrawingHatch => ShapeType.Hatch,
            DrawCubicPath => ShapeType.CubicPath,
            _ => shape.Type
        };

        /// <summary>
        /// 写入图元的类型专属 payload。
        /// 对实现了 IHatchable 的图元，会在几何后面追加 hatch 参数。
        /// </summary>
        private static void WriteSpecificPayload(
            BinaryWriter bw,
            DrawObject shape,
            int storageVersion)
        {
            switch (shape)
            {
                //case DrawLine line:
                //    WritePoints(bw, line.Points);
                //    return;
                case DrawPolyLines polyLine:
                    bw.Write((byte)polyLine.LineStyle);
                    bw.Write(polyLine.IsClosed);
                    WritePoints(bw, polyLine.Points);
                    break;
                case DrawRectangle rectangle:
                    bw.Write(rectangle.CornerRadiusTopLeft);
                    bw.Write(rectangle.CornerRadiusTopRight);
                    bw.Write(rectangle.CornerRadiusBottomRight);
                    bw.Write(rectangle.CornerRadiusBottomLeft);
                    bw.Write(rectangle.ChamferTopLeft);
                    bw.Write(rectangle.ChamferTopRight);
                    bw.Write(rectangle.ChamferBottomLeft);
                    bw.Write(rectangle.ChamferBottomRight);
                    WritePoints(bw, rectangle.Points);
                    break;
                case DrawCircle circle:
                    bw.Write(circle.DrawingRadiusX);
                    bw.Write(circle.DrawingRadiusY);
                    bw.Write(circle.IsEllipse);
                    WritePoints(bw, circle.Points);
                    break;
                case DrawDot dot:
                    WritePoints(bw, dot.Points);
                    break;
                case DrawArc arc:
                    WritePoints(bw, arc.Points);
                    break;
                case DrawText text:
                    WritePoints(bw, new List<SKPoint> { text.SharpCenter });
                    WriteTextPayload(bw, text);
                    break;
                case DrawBezier bezier:
                    bw.Write(bezier.IsClosed);
                    WritePoints(bw, bezier.Points);
                    break;
                case DrawArbitraryCurve arbitraryCurve:
                    bw.Write(arbitraryCurve.IsClosed);
                    WritePoints(bw, arbitraryCurve.Points);
                    break;
                case DrawPolygon polygon:
                    bw.Write(polygon.SideCount);
                    bw.Write(polygon.IsStar);
                    WritePoints(bw, polygon.Points);
                    break;
                case DrawCubicPath cubicPath:
                    bw.Write(cubicPath.IsClosed);
                    WritePoints(bw, cubicPath.Points);
                    WritePoints(bw, cubicPath.ControlHandles);
                    break;
                case DrawCombination:
                case DrawingGroup:
                    break;
                case DrawingHatch hatch:
                    var boundaries = hatch.Boundaries.OfType<DrawObject>().ToList();
                    WriteShapeList(bw, boundaries, storageVersion);
                    break;
                default:
                    throw new InvalidDataException($"高速 DRW 暂不支持保存图元类型: {shape.Type}");
            }

            if (shape is IHatchable hatchable)
            {
                // DrawingHatch 优先使用自身 HatchParamInfo 作为真相源；
                // 其他 IHatchable 图形仍从自身属性读取。
                var dto = shape is DrawingHatch hatchShape
                    ? hatchShape.HatchParamInfo
                    : hatchable.HatchParamInfo;
                WriteHatchParamPayload(bw, dto);
            }
        }

        private static void WriteTextPayload(BinaryWriter bw, DrawText text)
        {
            var textModel = text.TextModel ?? new TextModel();
            var font = textModel.FontSettings ?? new FontSettings();
            bw.Write(textModel.Text ?? string.Empty);
            bw.Write(font.FontFamily ?? string.Empty);
            bw.Write(font.FontSize);
            bw.Write(font.IsBold);
            bw.Write(font.IsItalic);
            bw.Write(font.IsUnderline);
            bw.Write(font.IsVerticalLayout);
            bw.Write((int)font.HorizontalAlign);
            bw.Write(font.LineHeight);
            bw.Write(font.CharacterSpacing);
            WriteColor(bw, font.TextColor);
        }

        private static void WriteShapeHeader(BinaryWriter bw, DrawObject shape)
        {
            bw.Write(shape.UId);
            bw.Write(shape.LayerId);
            bw.Write(shape.IsVisible);
            bw.Write(shape.IsLocked);
            bw.Write(shape.IsClockwise);
            bw.Write(shape.Name ?? string.Empty);
            bw.Write(shape.SharpCenter.X);
            bw.Write(shape.SharpCenter.Y);
            bw.Write(shape.Width);
            bw.Write(shape.Height);
            bw.Write(shape.Rotation);
            bw.Write(shape.ScaleX);
            bw.Write(shape.ScaleY);
            bw.Write(shape.SkewX);
            bw.Write(shape.SkewY);
        }

        private static SKColor ReadColor(BinaryReader br)
        {
            var red = br.ReadByte();
            var green = br.ReadByte();
            var blue = br.ReadByte();
            var alpha = br.ReadByte();
            return new SKColor(red, green, blue, alpha);
        }

        private static void WriteColor(BinaryWriter bw, SKColor color)
        {
            bw.Write(color.Red);
            bw.Write(color.Green);
            bw.Write(color.Blue);
            bw.Write(color.Alpha);
        }

        /// <summary>
        /// 读取可选的 hatch 参数尾字段。
        /// DrawingHatch 将参数同步到自身 HatchParamInfo（真相源），同时写入 IHatchable 以兼容渲染。
        /// </summary>
        private static void TryReadHatchParamPayload(BinaryReader br, DrawObject shape, long payloadEnd)
        {
            if (shape is not IHatchable hatchable || br.BaseStream.Position >= payloadEnd)
            {
                return;
            }

            var dto = ReadHatchParamPayload(br);
            hatchable.HatchParamInfo = dto;

            // DrawingHatch 以自身为真相源
            if (shape is DrawingHatch hatch)
            {
                hatch.HatchParamInfo = dto;
            }
        }

        /// <summary>
        /// 读取填充参数对象。
        /// </summary>
        private static HatchParamDto ReadHatchParamPayload(BinaryReader br)
        {
            if (!br.ReadBoolean())
            {
                return null;
            }

            return new HatchParamDto
            {
                OutlineColor = br.ReadString(),
                FillColor = br.ReadString(),
                OutlineStyleIndex = br.ReadInt32(),
                FillStyleIndex = br.ReadInt32(),
                Margin = br.ReadDouble(),
                RingSpacing = br.ReadDouble(),
                LineSpacing = br.ReadDouble(),
                Count = br.ReadInt32(),
                StartAngle = br.ReadDouble(),
                IncrementalAngle = br.ReadDouble(),
                Extension = br.ReadDouble(),
                FillTypeIndex = br.ReadInt32(),
                AverageDistribute = br.ReadBoolean(),
                InternalRings = br.ReadInt32(),
                DirectionTypeIndex = br.ReadInt32(),
                RelativeToAngle = br.ReadBoolean(),
                ReverseFillLine = br.ReadBoolean()
            };
        }

        /// <summary>
        /// 写入填充参数对象。
        /// </summary>
        private static void WriteHatchParamPayload(BinaryWriter bw, HatchParamDto hatchParam)
        {
            bw.Write(hatchParam != null);
            if (hatchParam == null)
            {
                return;
            }

            bw.Write(hatchParam.OutlineColor ?? string.Empty);
            bw.Write(hatchParam.FillColor ?? string.Empty);
            bw.Write(hatchParam.OutlineStyleIndex);
            bw.Write(hatchParam.FillStyleIndex);
            bw.Write(hatchParam.Margin);
            bw.Write(hatchParam.RingSpacing);
            bw.Write(hatchParam.LineSpacing);
            bw.Write(hatchParam.Count);
            bw.Write(hatchParam.StartAngle);
            bw.Write(hatchParam.IncrementalAngle);
            bw.Write(hatchParam.Extension);
            bw.Write(hatchParam.FillTypeIndex);
            bw.Write(hatchParam.AverageDistribute);
            bw.Write(hatchParam.InternalRings);
            bw.Write(hatchParam.DirectionTypeIndex);
            bw.Write(hatchParam.RelativeToAngle);
            bw.Write(hatchParam.ReverseFillLine);
        }

        /// <summary>
        /// 复制填充参数，避免把临时反序列化对象直接挂到真实图元上。
        /// </summary>
        private static HatchParamDto CloneHatchParam(HatchParamDto source)
        {
            if (source == null)
            {
                return null;
            }

            return new HatchParamDto
            {
                OutlineColor = source.OutlineColor,
                FillColor = source.FillColor,
                OutlineStyleIndex = source.OutlineStyleIndex,
                FillStyleIndex = source.FillStyleIndex,
                Margin = source.Margin,
                RingSpacing = source.RingSpacing,
                LineSpacing = source.LineSpacing,
                Count = source.Count,
                StartAngle = source.StartAngle,
                IncrementalAngle = source.IncrementalAngle,
                Extension = source.Extension,
                FillTypeIndex = source.FillTypeIndex,
                AverageDistribute = source.AverageDistribute,
                InternalRings = source.InternalRings,
                DirectionTypeIndex = source.DirectionTypeIndex,
                RelativeToAngle = source.RelativeToAngle,
                ReverseFillLine = source.ReverseFillLine
            };
        }

        private static void WritePoints(BinaryWriter bw, List<SKPoint> points)
        {
            bw.Write(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                bw.Write(points[i].X);
                bw.Write(points[i].Y);
            }
        }

        private static void WritePersistedTransform(
            BinaryWriter bw,
            SKMatrix transform)
        {
            bw.Write(PersistedTransformMagic);
            bw.Write(transform.ScaleX);
            bw.Write(transform.SkewX);
            bw.Write(transform.TransX);
            bw.Write(transform.SkewY);
            bw.Write(transform.ScaleY);
            bw.Write(transform.TransY);
        }

        private static void WriteShapeList(
            BinaryWriter bw,
            List<DrawObject> shapes,
            int storageVersion)
        {
            bw.Write(shapes.Count);
            for (var i = 0; i < shapes.Count; i++)
            {
                WriteShapeRecord(bw, shapes[i], storageVersion);
            }
        }

        private static void WriteLayerPayloadEntries(BinaryWriter bw, IReadOnlyDictionary<int, byte[]> layerPayloads)
        {
            bw.Write(layerPayloads.Count);
            foreach (var (layerIndex, value) in layerPayloads.OrderBy(kv => kv.Key))
            {
                bw.Write(layerIndex);
                bw.Write(value.Length);
                bw.Write(value);
            }
        }

        private static void WriteExtensionPayloadEntries(BinaryWriter bw, IReadOnlyDictionary<string, byte[]> extensionPayloads)
        {
            bw.Write(extensionPayloads.Count);
            foreach (var (key, value) in extensionPayloads.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                bw.Write(key);
                bw.Write(value.Length);
                bw.Write(value);
            }
        }

        private readonly record struct SectionEntry(SectionType Type, long Offset, long Length, int Flags);

        private readonly record struct SectionEntryBuilder(
            SectionType Type,
            Action<BinaryWriter> WriteAction,
            int Flags = 0);

        private enum SectionType
        {
            CanvasMeta = 1,
            Geometry = 2,
            MarkCardPayload = 3,
            ExtensionPayloads = 4
        }

        private sealed class ShapeHeader
        {
            public SKPoint SharpCenter { get; init; }
            public float Width { get; init; }
            public float Height { get; init; }
            public float Rotation { get; init; }
            public float ScaleX { get; init; }
            public float ScaleY { get; init; }
            public float SkewX { get; init; }
            public float SkewY { get; init; }
        }
    }
}
