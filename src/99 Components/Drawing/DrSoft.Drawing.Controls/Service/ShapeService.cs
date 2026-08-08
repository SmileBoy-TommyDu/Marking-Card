using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Mapping;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace DrSoft.Drawing.Controls.Service
{
    /// <summary>
    /// 图形应用服务聚合根。
    /// 对外实现多个细分服务接口，并将 UI/业务请求转发到当前活动画布。
    /// </summary>
    public sealed partial class ShapeService : IShapeService
    {
        private readonly DocumentContext _context;
        private readonly MultiCanvas _multiCanvas;
        public Dictionary<string, bool> EditCommandsStatus = new();
        private bool _isSettingJumpPoint = false;

        /// <summary>
        /// 获取当前活动画布（随画布切换动态更新）
        /// </summary>
        private DrawingCanvas? ActiveCanvas => _context.ActiveCanvas as DrawingCanvas;

        /// <summary>
        /// 绑定多画布上下文，并注册图形变换后的跳点重算回调。
        /// </summary>
        public ShapeService(CanvasViewModel canvasViewModel)
        {
            _multiCanvas = canvasViewModel.MultiCanvas;
            _context = _multiCanvas.Context;

            // 注入图形变换后的跳点自动重算回调
            _context.RecalculateJumpPointsAction = () =>
            {
                if (_isSettingJumpPoint) return;

                // 仅在多选且存在有效跳点半径时重算，避免普通编辑路径上的额外开销。
                var draws = _context.ActiveCanvas?.Selection.OfType<DrawObject>().ToList();
                if (draws == null || draws.Count < 2) return;

                float skipRadius = draws.FirstOrDefault(d => d.IntersectionSkipRadius > 0f)?.IntersectionSkipRadius ?? 0f;
                if (skipRadius <= 0f) return;

                draws.ApplyJumpPointState(
                    skipRadius,
                    static (left, right) => left.ComputePathIntersections(right));

                _context.RequestRedraw();
            };
        }

    }
    /// <summary>
    /// 选区相关能力入口。
    /// </summary>
    public sealed partial class ShapeService : IShapeSelectionService
    {
        public GraphicResult SelectAll()
        {
            return ActiveCanvas?.SelectAll();
        }

        public GraphicResult SelectInvert()
        {
            return ActiveCanvas?.SelectInvert();
        }

        public GraphicResult ClearSelection()
        {
            return ActiveCanvas?.ClearSelection();
        }
    }

    /// <summary>
    /// 编辑命令入口。
    /// </summary>
    public sealed partial class ShapeService : IShapeEditService
    {
        public GraphicResult Undo()
        {
            return ActiveCanvas?.Undo();
        }

        public GraphicResult Redo()
        {
            return ActiveCanvas?.Redo();
        }

        public GraphicResult Copy()
        {
            return ActiveCanvas?.Copy();
        }

        public GraphicResult Cut()
        {
            return ActiveCanvas?.Cut();
        }

        public GraphicResult Paste(bool useMousePosition, bool suppressSelectionPublish = false)
        {
            return ActiveCanvas?.Paste(useMousePosition, suppressSelectionPublish);
        }

        public GraphicResult Delete()
        {
            return ActiveCanvas?.Delete();
        }

        public GraphicResult Replace()
        {
            return ActiveCanvas?.Replace();
        }

        #region 节点编辑方法
        public GraphicResult EditNodes(bool turnOn)
        {
            return ActiveCanvas?.EditNodes(turnOn);
        }

        /// <summary>直接读取 DocumentContext.IsNodeEditing，与画布侧保持一致。</summary>
        public bool IsNodeEditing => _context.IsNodeEditing;

        public GraphicResult AddNodes(bool turnOn)
        {
            return ActiveCanvas?.AddNodes(turnOn);
        }
        public GraphicResult DeleteNodes(bool turnOn)
        {
            return ActiveCanvas?.DeleteNodes(turnOn);
        }

        public GraphicResult SeparateNodes(bool turnOn)
        {
            return ActiveCanvas?.SeparateNodes(turnOn) ?? GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);
        }

        public GraphicResult MoveNodes(bool turnOn)
        {
            return ActiveCanvas?.MoveNodes(turnOn) ?? GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);
        }

        public GraphicResult ExtendNodes(bool turnOn)
        {
            return ActiveCanvas?.ExtendNodes(turnOn) ?? GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);
        }

        public GraphicResult ConnectNodes(bool turnOn)
        {
            return ActiveCanvas?.ConnectNodes(turnOn) ?? GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);
        }

        public GraphicResult SelectNodes(bool turnOn)
        {
            return ActiveCanvas?.SelectNodes(turnOn) ?? GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);
        }
        #endregion

        public GraphicResult ChangeSelectedState(int state)
        {
            return ActiveCanvas?.ChangeSelectedState(state);
        }

        public GraphicResult UpdateRotateCenterIcon(double x, double y, bool isShow)
        {
            return ActiveCanvas?.UpdateRotateCenterIcon(x, y, isShow);
        }

        public GraphicResult UpdateSkewCenterIcon(double x, double y, bool isShow)
        {
            return ActiveCanvas?.UpdateSkewCenterIcon(x, y, isShow);
        }
    }

    /// <summary>
    /// 几何变换入口。
    /// </summary>
    public sealed partial class ShapeService : IShapeTransformService
    {
        public GraphicResult SetCenter(double cx, double cy)
        {
            return ActiveCanvas?.SetCenter(cx, cy);
        }

        public GraphicResult SetDimension(double width, double height)
        {
            return ActiveCanvas?.SetDimension(width, height);
        }

        public GraphicResult SetTranslate(double cx, double cy)
        {
            return ActiveCanvas?.SetTranslate(cx, cy);
        }

        public GraphicResult SetScale(double cx, double cy, double scaleX, double scaleY)
        {
            return ActiveCanvas?.SetScale(cx, cy, scaleX, scaleY);
        }

        public GraphicResult SetAbsoluteRotation(double cx, double cy, double angle)
        {
            return ActiveCanvas?.SetAbsoluteRotation(cx, cy, angle);
        }

        public GraphicResult SetRotation(double cx, double cy, double angle)
        {
            return ActiveCanvas?.SetRotation(cx, cy, angle);
        }

        public GraphicResult SetSkew(double angleX, double angleY)
        {
            return ActiveCanvas?.SetSkew(angleX, angleY);
        }

        public GraphicResult SetAbsoluteSkew(double cx, double cy, double angleX, double angleY)
        {
            return ActiveCanvas?.SetAbsoluteSkew(cx, cy, angleX, angleY);
        }

        public GraphicResult SetSkew(double cx, double cy, double angleX, double angleY)
        {
            return ActiveCanvas?.SetSkew(cx, cy, angleX, angleY);
        }

        public GraphicResult HorizontalMirror()
        {
            return ActiveCanvas?.HorizontalMirror();
        }

        public GraphicResult VerticalMirror()
        {
            return ActiveCanvas?.VerticalMirror();
        }
    }

    /// <summary>
    /// 图形结构调整入口，如组合、打散、分层和参数复制。
    /// </summary>
    public sealed partial class ShapeService : IShapeStructureService
    {
        public GraphicResult Combine()
        {
            return ActiveCanvas?.Combine();
        }

        public GraphicResult Break()
        {
            return ActiveCanvas?.Separate();
        }

        public GraphicResult Group()
        {
            return ActiveCanvas?.Group();
        }

        public GraphicResult Ungroup()
        {
            return ActiveCanvas?.Ungroup();
        }

        public GraphicResult Reverse(bool isReverse)
        {
            if (!isReverse) return GraphicResult.Ok();
            return ActiveCanvas?.Reverse();
        }

        public GraphicResult Lock()
        {
            return ActiveCanvas?.Lock();
        }

        public GraphicResult SetTextFont(FontSettingsDto dto, string text = null, FontSettingsFields updatedFields = FontSettingsFields.All)
        {
            var settings = DrawTextMapper.Map(dto);
            _context.CurrentTextFontSettings = CloneFontSettings(settings);
            ((DrawingCanvas?)_context.ActiveCanvas)?.SetFontSettings(settings, text, updatedFields);
            _context.RequestRedraw();
            return GraphicResult.Ok();
        }

        /// <summary>
        /// 复制字体设置，避免 UI 层缓存与画布当前对象共享同一可变实例。
        /// </summary>
        private static FontSettings CloneFontSettings(FontSettings source)
        {
            if (source == null)
            {
                return new FontSettings();
            }

            return new FontSettings
            {
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                IsBold = source.IsBold,
                IsItalic = source.IsItalic,
                IsUnderline = source.IsUnderline,
                IsVerticalLayout = source.IsVerticalLayout,
                HorizontalAlign = source.HorizontalAlign,
                VerticalAlign = source.VerticalAlign,
                LineHeight = source.LineHeight,
                CharacterSpacing = source.CharacterSpacing,
                TextColor = source.TextColor
            };
        }

        public GraphicResult VectorCombine()
        {
            return ActiveCanvas?.VectorCombine(SKPathOp.Union);
        }

        /// <summary>
        /// 打散填满物件（如 Hatch 填充），将其转换为普通的线段/曲线图形，便于编辑。
        /// </summary>
        /// <returns></returns>
        public GraphicResult BreakFill()
        {
            return ActiveCanvas?.BreakFill();
        }

        public GraphicResult MoveToNewLayer()
        {
            return ActiveCanvas?.MoveToNewLayer();
        }
        public GraphicResult ConvertToCurve()
        {
            return ActiveCanvas?.ConvertToCurve();
        }

        public GraphicResult ConvertToDot(ConvertToDotSettingsDto settings)
        {
            return ActiveCanvas?.ConvertToDot(settings);
        }


        /// <summary>
        /// 依分区打断物件：仅保留原图形轮廓曲线在各分区内的部分，不引入分割矩形边。
        /// 通过线段裁剪算法（Liang-Barsky）将原路径采样后的线段裁剪到分区矩形内，
        /// 每个分区仅包含原轨迹落入该区域的片段，适用于大图形的分割打标场景。
        /// 支持撤销（通过 CompositeCommand 组合 CommandRemove + CommandAdd）。
        /// </summary>
        /// <param name="partWidth">分割区块长度（mm）</param>
        /// <param name="partHeight">分割区块宽度（mm）</param>
        /// <param name="overlapX">X方向重叠长度（mm）</param>
        /// <param name="overlapY">Y方向重叠长度（mm）</param>
        public GraphicResult Partition(double partWidth, double partHeight, double overlapX, double overlapY)
        {
            return ActiveCanvas?.Partition(partWidth, partHeight, overlapX, overlapY);
        }


        public GraphicResult ExtendHeadAndTail()
        {
            return ActiveCanvas?.ExtendHeadAndTail();
        }

        /// <summary>
        /// 对齐选中的图形对象
        /// </summary>
        public GraphicResult Align(AlignSettingsDto settings)
        {
            return ActiveCanvas?.Align(settings);
        }
        public GraphicResult Distribute(DistributeSettingsDto settings)
        {
            return ActiveCanvas?.Distribute(settings);
        }


        public GraphicResult SetSkyWriting(SkyWritingSettingsDto settings)
        {
            return ActiveCanvas?.SetSkyWriting(settings);
        }
    }

    /// <summary>
    /// 填充与重填充入口。
    /// </summary>
    public sealed partial class ShapeService : IShapeFillService
    {
        public GraphicResult<int>? Fill(HatchParamDto hatchParam)
        {
            return ActiveCanvas?.Fill(hatchParam);
        }

        public GraphicResult<List<int>>? Refill(HatchParamDto hatchParam)
        {
            return ActiveCanvas?.Refill(hatchParam);
        }

        public GraphicResult<HatchParamDto?> GetHatchParam()
        {
            var canvas = ActiveCanvas;
            if (canvas == null)
                return GraphicResult<HatchParamDto?>.Fail(GraphicErrorCode.CanvasNotFound, "No active canvas");

            return canvas.GetHatchParam();
        }
    }

    /// <summary>
    /// IShapeQueryService — 零拷贝，直接返回 IShapeData 只读接口，无需 DTO 转换
    /// </summary>
    public sealed partial class ShapeService : IShapeQueryService
    {
        GraphicResult<IReadOnlyList<IShapeData>> IShapeQueryService.GetSelections()
        {
            var canvas = ActiveCanvas;
            if (canvas == null)
                return GraphicResult<IReadOnlyList<IShapeData>>.Fail(GraphicErrorCode.CanvasNotFound, "No active canvas");

            var result = canvas.Selection
                .OfType<IShapeData>()
                .ToArray();
            return GraphicResult<IReadOnlyList<IShapeData>>.Ok(result);
        }

        GraphicResult<IReadOnlyList<IShapeData>> IShapeQueryService.GetAllShapes()
        {
            var canvas = ActiveCanvas;
            if (canvas == null)
                return GraphicResult<IReadOnlyList<IShapeData>>.Fail(GraphicErrorCode.CanvasNotFound, "No active canvas");

            var result = canvas.AllShapes
                .OfType<IShapeData>()
                .ToArray();
            return GraphicResult<IReadOnlyList<IShapeData>>.Ok(result);
        }

        GraphicResult<IReadOnlyList<IShapeData>> IShapeQueryService.GetAllShapes(int canvasId)
        {
            // 多画布场景：从所有画布中找到指定 ID 的画布
            var canvas = _multiCanvas.CanvasCollection
                .OfType<DrawingCanvas>()
                .FirstOrDefault(c => c.Id == canvasId);
            if (canvas == null)
                return GraphicResult<IReadOnlyList<IShapeData>>.Fail(GraphicErrorCode.CanvasNotFound, $"Canvas {canvasId} not found");

            var result = canvas.AllShapes
                .OfType<IShapeData>()
                .ToArray();
            return GraphicResult<IReadOnlyList<IShapeData>>.Ok(result);
        }
    }

    /// <summary>
    /// 图形参数调整入口。
    /// </summary>
    public sealed partial class ShapeService : IShapeAdjustService
    {
        public GraphicResult AdjustRect(RoundMode mode, double lt, double rt, double rb, double lb)
        {
            return ActiveCanvas?.AdjustRect(mode, lt, rt, rb, lb);
        }

        public GraphicResult AdjustChamfer(RoundMode mode, double lt, double rt, double rb, double lb)
        {
            return ActiveCanvas?.AdjustChamfer(mode, lt, rt, rb, lb);
        }
        public GraphicResult AdjustCircle(double cx, double cy, double rx, double ry)
        {
            return ActiveCanvas?.AdjustCircle(cx, cy, rx, ry);
        }

        public GraphicResult AdjustArc(double cx, double cy, double rx, double ry,
 double sAngle, double eAngle)
        {
            return ActiveCanvas?.AdjustArc(cx, cy, rx, ry, sAngle, eAngle);
        }

        public GraphicResult AdjustArcThreePoint(
       float p0x, float p0y,
       float p1x, float p1y,
       float p2x, float p2y)
        {
            return ActiveCanvas?.AdjustArcThreePoint(p0x, p0y, p1x, p1y, p2x, p2y);
        }

        public GraphicResult SetJumpPoint(JumpSettingsDto jumpSettings)
        {
            // 阻止设置跳点时触发回调二次进入，避免重复计算和状态抖动。
            _isSettingJumpPoint = true;
            try
            {
                return ActiveCanvas?.SetJumpPoint(jumpSettings);
            }
            finally
            {
                _isSettingJumpPoint = false;
            }
        }

        public GraphicResult AdjustPolygon(int sideCount, PolygonType polygonType)
        {
            return ActiveCanvas?.AdjustPolygon(sideCount, polygonType)
                ?? GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);
        }

        public GraphicResult ClosePath()
        {
            return ActiveCanvas!.ClosePath();
        }

        /// <summary>
        /// 设置选中图形的外框颜色和样式。外框颜色优先级高于图层颜色。
        /// outlineColor 非 null 时创建自定义画笔（覆盖图层颜色），为 null 时清除自定义画笔（回退到图层颜色）。
        /// 通过 CommandManager 执行，支持撤销/重做。
        /// </summary>
        public GraphicResult SetOutlineStyle(string? outlineColor, int outlineStyleIndex)
        {
            var canvas = ActiveCanvas;
            if (canvas == null)
                return GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);

            var selectedShapes = canvas.Selection.OfType<DrawObject>().ToList();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            var cmd = new CommandSetOutlineStyle(selectedShapes, outlineColor, outlineStyleIndex);
            canvas.CommandHistory.Execute(cmd);

            _context.RequestRedraw();
            return GraphicResult.Ok();
        }
    }

    /// <summary>
    /// 阵列复制入口。
    /// </summary>
    public sealed partial class ShapeService : IShapeMatrixCopyService
    {
        public GraphicResult CircleCopy(double Radius, int Count, double StartAngle, double IntervalAngle, bool IsAverageDistribute, bool IsObjectRotate, bool IsCounterClockwise)
        {
            return ActiveCanvas?.CircleCopy(Radius, Count, StartAngle, IntervalAngle, IsAverageDistribute, IsObjectRotate, IsCounterClockwise)
                ?? GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);
        }

        public GraphicResult MatrixCopy(int colunmnCount, double columnSpace, int rowCount, double rowSpace)
        {
            return ActiveCanvas?.MatrixCopy(colunmnCount, columnSpace, rowCount, rowSpace) ?? GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);
        }
    }

    /// <summary>
    /// 向量布尔运算入口。
    /// </summary>
    public sealed partial class ShapeService : IShapeVectorService
    {
        /// <summary>
        /// 交集
        /// </summary>
        /// <returns></returns>
        public GraphicResult Intersect()
        {
            return ActiveCanvas?.VectorCombine(SKPathOp.Intersect);
        }

        /// <summary>
        /// 主物件保留
        /// </summary>
        /// <returns></returns>
        public GraphicResult KeepMain()
        {
            return ActiveCanvas?.KeepMain();
        }

        /// <summary>
        /// 裁剪
        /// </summary>
        /// <returns></returns>
        public GraphicResult Trim()
        {
            return ActiveCanvas?.VectorCombine(SKPathOp.ReverseDifference);
        }

        public GraphicResult Union()
        {
            return ActiveCanvas?.VectorCombine(SKPathOp.Union);
        }
    }
}
