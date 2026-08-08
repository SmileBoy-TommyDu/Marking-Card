using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Event
{
    public sealed class SelectionCapabilities
    {
        public int TotalCount { get; init; }
        public IReadOnlyList<IShapeData> SelectedShapeData { get; init; } = Array.Empty<IShapeData>();

        #region 编辑菜单的命令能力
        public bool CanUndo { get; set; }
        public bool CanRedo { get; set; }
        public bool CanCopy { get; init; }
        public bool CanCut { get; init; }
        public bool CanPaste { get; set; }
        public bool CanDelete { get; init; }
        public bool CanSelectAll { get; init; }
        public bool CanInverseSelect { get; init; }
        public bool CanReplace { get; init; }
        public bool CanCombine { get; init; }
        public bool CanBreak { get; init; }
        public bool CanBreakFill { get; init; }
        public bool CanGroup { get; init; }
        public bool CanUngroup { get; init; }
        public bool CanVectorCombine { get; init; }
        public bool CanMoveToNewLayer { get; init; }
        public bool CanInverse { get; init; }
        public bool CanHorizontalMirrorReflection { get; init; }
        public bool CanVerticalMirrorReflection { get; init; }
        public bool CanMaterialCenter { get; init; }
        public bool CanConvertToPointOrCircle { get; init; }
        public bool CanConvertToCurve { get; init; }
        public bool CanJumpPoint { get; init; }
        public bool CanAlign { get; init; }
        public bool CanDistribution { get; init; }
        public bool CanExtendHeadAndTail { get; init; }
        public bool CanSkyWriting { get; init; }
        public bool CanPartition { get; init; }
        public bool CanLock { get; init; }
        public bool CanExtendNode { get; set; }
        public bool CanEnterNodeEdit { get; set; }
        #endregion

        #region 节点编辑菜单的命令能力
        public bool CanNodeEdit { get; init; }
        public bool CanAddNode { get; init; }
        public bool CanRemoveNode { get; init; }
        #endregion

        public bool CanRotate { get; init; }
        public bool CanScale { get; init; }
        public bool CanFlip { get; init; }
        public bool CanFill { get; init; }  // 含封闭图形

        public bool CanText { get; init; }  // Text

        public bool IsSingleType { get; init; }

        #region 选中类型判断
        public bool IsCircle { get; init; }
        public bool IsLine { get; init; }
        public bool IsPolygon { get; init; }
        public bool IsArc { get; init; }
        public bool IsPoint { get; init; }
        public bool IsRectangle { get; init; }
        public bool IsCurve { get; init; }
        #endregion

        public bool IsLocked { get; set; }

        /// <summary>
        /// 从 SelectionSummary 统一计算所有能力
        /// </summary>
        public static SelectionCapabilities From(IReadOnlyList<IShapeData> shapes)
        {
            if (shapes == null) return Empty;
            bool hasAny = shapes.Count > 0;

            return new SelectionCapabilities
            {
                TotalCount = shapes.Count,
                SelectedShapeData = shapes ?? Array.Empty<IShapeData>(),
                CanCopy = hasAny,
                CanCut = hasAny,
                CanDelete = hasAny,
                CanSelectAll = hasAny,
                CanInverseSelect = hasAny,
                CanInverse = hasAny,
                CanGroup = hasAny,
                CanHorizontalMirrorReflection = ((shapes?.Count == 1 && !shapes.Any(s => s.Type == ShapeType.Hatch)) || shapes.Count > 1) && !shapes.Any(s => s.Type == ShapeType.Point),
                CanVerticalMirrorReflection = ((shapes?.Count == 1 && !shapes.Any(s => s.Type == ShapeType.Hatch)) || shapes.Count > 1) && !shapes.Any(s => s.Type == ShapeType.Point),
                CanMaterialCenter = hasAny,
                CanExtendHeadAndTail = hasAny,
                CanPartition = hasAny,
                CanSkyWriting = hasAny,
                CanMoveToNewLayer = hasAny,
                CanConvertToPointOrCircle = hasAny && !shapes.Any(s => s.Type == ShapeType.Combination) && !shapes.Any(s => s.Type == ShapeType.Group) && !shapes.Any(s => s.Type == ShapeType.Hatch) && !shapes.Any(s => s.Type == ShapeType.Point),
                CanLock = hasAny,
                CanAlign = shapes.Count > 1,
                CanCombine = shapes.Count > 1 && !shapes.Any(s => s.Type == ShapeType.Combination) && !shapes.Any(s => s.Type == ShapeType.Group) && !shapes.Any(s => s.Type == ShapeType.Hatch),
                CanVectorCombine = shapes.Count > 1 && !shapes.Any(s => s.Type == ShapeType.Group) && !shapes.Any(s => s.Type == ShapeType.Point),
                CanDistribution = shapes.Count > 1,
                CanJumpPoint = shapes.Count > 0,
                CanBreak = shapes.Any(s => s.Type == ShapeType.Combination),
                CanBreakFill = shapes.Any(s => s.Type == ShapeType.Hatch),
                CanUngroup = shapes.Any(s => s.Type == ShapeType.Group),
                CanRotate = hasAny,
                CanScale = hasAny,
                CanFlip = hasAny,

                CanText = shapes.Any(s => s.Type == ShapeType.Text),

                IsCircle = shapes.Any(s => s.Type == ShapeType.Circle) && shapes.Count == 1,
                IsLine = shapes.Any(s => s.Type == ShapeType.PolyLine) && shapes.Count == 1,
                IsPolygon = shapes.Any(s => s.Type == ShapeType.Polygon) && shapes.Count == 1,
                IsArc = shapes.Any(s => s.Type == ShapeType.Arc) && shapes.Count == 1,
                IsPoint = shapes.Any(s => s.Type == ShapeType.Point) && shapes.Count == 1,
                IsRectangle = shapes.Any(s => s.Type == ShapeType.Rectangle) && shapes.Count == 1,
                IsCurve = (shapes.Any(s => s.Type == ShapeType.Bezier) || shapes.Any(s => s.Type == ShapeType.CubicPath)) && shapes.Count == 1,

                IsSingleType = shapes.Count == 1,
                //CanUnion = hasAny && !summary.HasType(ShapeType.Combination) && !summary.HasType(ShapeType.Group),
                CanExtendNode = shapes.Count >= 1
                    && !shapes.Any(s => s.Type == ShapeType.Hatch)
                    && !shapes.Any(s => s.Type == ShapeType.Text)
                    && !shapes.Any(s => s.Type == ShapeType.Group)
                    && !shapes.Any(s => s.Type == ShapeType.Point)
            };
        }

        public static SelectionCapabilities Empty { get; } = new SelectionCapabilities();
    }
}
