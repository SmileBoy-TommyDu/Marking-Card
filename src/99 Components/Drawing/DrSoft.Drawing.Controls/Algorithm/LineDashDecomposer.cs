using System;
using System.Collections.Generic;
using System.Drawing;

namespace DrSoft.Drawing.Controls.Algorithm
{
    /// <summary>
    /// 虚线类型
    /// </summary>
    public enum DashType
    {
        /// <summary>点虚线（实心点）</summary>
        DotDash,

        /// <summary>段虚线（实线段 + 空白段）</summary>
        SegmentDash
    }

    /// <summary>
    /// 虚线生成参数
    /// </summary>
    public class DashParameters
    {
        /// <summary>虚线类型</summary>
        public DashType Type { get; set; } = DashType.SegmentDash;

        /// <summary>实线长度（像素）</summary>
        public float SolidLength { get; set; } = 5f;

        /// <summary>空白长度（像素）</summary>
        public float BlankLength { get; set; } = 3f;

        /// <summary>点虚线专用：点半径（像素）</summary>
        public float DotRadius { get; set; } = 1.5f;

        /// <summary>是否保持两端长度一致（仅对段虚线有效）</summary>
        public bool KeepEndsEqual { get; set; } = true;

        /// <summary>总长小于此值时强制显示为实线</summary>
        public float MinLengthForDash { get; set; } = 1f;
    }

    /// <summary>
    /// 虚线段结果
    /// </summary>
    public struct DashSegment
    {
        public PointF Start;
        public PointF End;
        public bool IsSolid;  // true=实线，false=空白（跳过）

        public DashSegment(PointF start, PointF end, bool isSolid)
        {
            Start = start;
            End = end;
            IsSolid = isSolid;
        }

        public override string ToString()
        {
            return $"{(IsSolid ? "实线" : "空白")}: ({Start.X:F1},{Start.Y:F1}) → ({End.X:F1},{End.Y:F1})";
        }
    }

    /// <summary>
    /// 虚线分解器
    /// </summary>
    public class LineDashDecomposer
    {
        private DashParameters _params;

        public LineDashDecomposer(DashParameters parameters)
        {
            _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        /// <summary>
        /// 更新参数
        /// </summary>
        public void UpdateParameters(DashParameters parameters)
        {
            _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        /// <summary>
        /// 分解直线为虚线段（仅返回实线段）
        /// </summary>
        public List<DashSegment> Decompose(PointF start, PointF end)
        {
            float totalLength = Distance(start, end);

            if (totalLength < _params.MinLengthForDash)
            {
                return new List<DashSegment> { new DashSegment(start, end, true) };
            }

            switch (_params.Type)
            {
                case DashType.DotDash:
                    return DecomposeDotDash(start, end);
                case DashType.SegmentDash:
                    return DecomposeSegmentDash(start, end);
                default:
                    return DecomposeSegmentDash(start, end);
            }
        }

        /// <summary>
        /// 分解直线为虚线段（包含空白段，用于调试）
        /// </summary>
        public List<DashSegment> DecomposeWithBlanks(PointF start, PointF end)
        {
            float totalLength = Distance(start, end);

            if (totalLength < _params.MinLengthForDash)
            {
                return new List<DashSegment> { new DashSegment(start, end, true) };
            }

            switch (_params.Type)
            {
                case DashType.DotDash:
                    return DecomposeDotDashWithBlanks(start, end);
                case DashType.SegmentDash:
                    return DecomposeSegmentDashWithBlanks(start, end);
                default:
                    return DecomposeSegmentDashWithBlanks(start, end);
            }
        }

        /// <summary>
        /// 点虚线分解（仅实线）
        /// </summary>
        private List<DashSegment> DecomposeDotDash(PointF start, PointF end)
        {
            List<DashSegment> result = new List<DashSegment>();
            float totalLength = Distance(start, end);
            float solidLen = _params.SolidLength;
            float blankLen = _params.BlankLength;
            float dotRadius = _params.DotRadius;
            float step = solidLen + blankLen;

            if (step <= 0) return result;

            Vector2 dir = new Vector2(end.X - start.X, end.Y - start.Y);
            dir = dir.Normalized();
            Vector2 perpendicular = new Vector2(-dir.Y, dir.X);

            float currentPos = 0;

            while (currentPos <= totalLength)
            {
                float centerPos = currentPos + solidLen / 2;

                if (centerPos <= totalLength)
                {
                    PointF center = GetPointOnLine(start, dir, centerPos);

                    PointF dotStart = new PointF(
                        center.X - dotRadius * perpendicular.X,
                        center.Y - dotRadius * perpendicular.Y
                    );
                    PointF dotEnd = new PointF(
                        center.X + dotRadius * perpendicular.X,
                        center.Y + dotRadius * perpendicular.Y
                    );
                    result.Add(new DashSegment(dotStart, dotEnd, true));
                }

                currentPos += step;
            }

            return result;
        }

        /// <summary>
        /// 点虚线分解（包含空白段）
        /// </summary>
        private List<DashSegment> DecomposeDotDashWithBlanks(PointF start, PointF end)
        {
            List<DashSegment> result = new List<DashSegment>();
            float totalLength = Distance(start, end);
            float solidLen = _params.SolidLength;
            float blankLen = _params.BlankLength;
            float step = solidLen + blankLen;

            if (step <= 0) return result;

            Vector2 dir = new Vector2(end.X - start.X, end.Y - start.Y);
            dir = dir.Normalized();
            Vector2 perpendicular = new Vector2(-dir.Y, dir.X);
            float dotRadius = _params.DotRadius;

            float currentPos = 0;

            while (currentPos <= totalLength)
            {
                // 实线点
                float centerPos = currentPos + solidLen / 2;
                if (centerPos <= totalLength)
                {
                    PointF center = GetPointOnLine(start, dir, centerPos);
                    PointF dotStart = new PointF(
                        center.X - dotRadius * perpendicular.X,
                        center.Y - dotRadius * perpendicular.Y
                    );
                    PointF dotEnd = new PointF(
                        center.X + dotRadius * perpendicular.X,
                        center.Y + dotRadius * perpendicular.Y
                    );
                    result.Add(new DashSegment(dotStart, dotEnd, true));
                }

                // 空白段
                float blankStartPos = currentPos + solidLen;
                float blankEndPos = Math.Min(blankStartPos + blankLen, totalLength);
                if (blankStartPos < totalLength)
                {
                    PointF blankStart = GetPointOnLine(start, dir, blankStartPos);
                    PointF blankEnd = GetPointOnLine(start, dir, blankEndPos);
                    result.Add(new DashSegment(blankStart, blankEnd, false));
                }

                currentPos += step;
            }

            return result;
        }

        /// <summary>
        /// 段虚线分解（仅实线）
        /// </summary>
        private List<DashSegment> DecomposeSegmentDash(PointF start, PointF end)
        {
            List<DashSegment> result = new List<DashSegment>();
            float totalLength = Distance(start, end);
            float solidLen = _params.SolidLength;
            float blankLen = _params.BlankLength;
            float patternLen = solidLen + blankLen;

            if (patternLen <= 0) return result;

            // 计算首尾实际长度（保持两端相等）
            float firstSolidLen = solidLen;
            float lastSolidLen = solidLen;

            if (_params.KeepEndsEqual && totalLength > patternLen)
            {
                int fullCycles = (int)((totalLength - solidLen) / patternLen);

                if (fullCycles >= 1)
                {
                    float remaining = totalLength - fullCycles * patternLen;
                    firstSolidLen = remaining / 2;
                    lastSolidLen = remaining / 2;

                    // 限制最大长度，避免首尾过长
                    if (firstSolidLen > solidLen * 1.5f)
                    {
                        firstSolidLen = solidLen;
                        lastSolidLen = solidLen;
                    }
                    if (firstSolidLen < _params.MinLengthForDash)
                    {
                        firstSolidLen = solidLen;
                        lastSolidLen = solidLen;
                    }
                }
            }

            Vector2 dir = new Vector2(end.X - start.X, end.Y - start.Y);
            dir = dir.Normalized();

            float currentPos = 0;
            int segmentIndex = 0;
            bool isSolid = true;

            while (currentPos < totalLength - 0.001f)
            {
                float segLen;

                if (isSolid)
                {
                    if (segmentIndex == 0 && _params.KeepEndsEqual)
                        segLen = firstSolidLen;
                    else if (IsLastSolidSegment(currentPos, totalLength, blankLen, solidLen) && _params.KeepEndsEqual)
                        segLen = lastSolidLen;
                    else
                        segLen = solidLen;
                }
                else
                {
                    segLen = blankLen;
                }

                // 最后一段处理
                if (currentPos + segLen > totalLength)
                {
                    segLen = totalLength - currentPos;

                    if (!isSolid && segLen < _params.MinLengthForDash)
                        break;
                }

                PointF segStart = GetPointOnLine(start, dir, currentPos);
                PointF segEnd = GetPointOnLine(start, dir, currentPos + segLen);

                if (isSolid && segLen > 0.001f)
                {
                    result.Add(new DashSegment(segStart, segEnd, true));
                }

                currentPos += segLen;
                isSolid = !isSolid;
                segmentIndex++;
            }

            return result;
        }

        /// <summary>
        /// 段虚线分解（包含空白段）
        /// </summary>
        private List<DashSegment> DecomposeSegmentDashWithBlanks(PointF start, PointF end)
        {
            List<DashSegment> result = new List<DashSegment>();
            float totalLength = Distance(start, end);
            float solidLen = _params.SolidLength;
            float blankLen = _params.BlankLength;
            float patternLen = solidLen + blankLen;

            if (patternLen <= 0) return result;

            // 计算首尾实际长度（保持两端相等）
            float firstSolidLen = solidLen;
            float lastSolidLen = solidLen;

            if (_params.KeepEndsEqual && totalLength > patternLen)
            {
                int fullCycles = (int)((totalLength - solidLen) / patternLen);

                if (fullCycles >= 1)
                {
                    float remaining = totalLength - fullCycles * patternLen;
                    firstSolidLen = remaining / 2;
                    lastSolidLen = remaining / 2;

                    if (firstSolidLen > solidLen * 1.5f)
                    {
                        firstSolidLen = solidLen;
                        lastSolidLen = solidLen;
                    }
                    if (firstSolidLen < _params.MinLengthForDash)
                    {
                        firstSolidLen = solidLen;
                        lastSolidLen = solidLen;
                    }
                }
            }

            Vector2 dir = new Vector2(end.X - start.X, end.Y - start.Y);
            dir = dir.Normalized();

            float currentPos = 0;
            int segmentIndex = 0;
            bool isSolid = true;

            while (currentPos < totalLength - 0.001f)
            {
                float segLen;

                if (isSolid)
                {
                    if (segmentIndex == 0 && _params.KeepEndsEqual)
                        segLen = firstSolidLen;
                    else if (IsLastSolidSegment(currentPos, totalLength, blankLen, solidLen) && _params.KeepEndsEqual)
                        segLen = lastSolidLen;
                    else
                        segLen = solidLen;
                }
                else
                {
                    segLen = blankLen;
                }

                if (currentPos + segLen > totalLength)
                {
                    segLen = totalLength - currentPos;
                }

                PointF segStart = GetPointOnLine(start, dir, currentPos);
                PointF segEnd = GetPointOnLine(start, dir, currentPos + segLen);

                result.Add(new DashSegment(segStart, segEnd, isSolid));

                currentPos += segLen;
                isSolid = !isSolid;
                segmentIndex++;
            }

            return result;
        }

        /// <summary>
        /// 判断当前是否是最后一个实线段
        /// </summary>
        private bool IsLastSolidSegment(float currentPos, float totalLength, float blankLen, float solidLen)
        {
            float remaining = totalLength - currentPos;
            // 如果剩余长度刚好是一个实线段，或者是一个实线段加一个空白段
            return Math.Abs(remaining - solidLen) < 0.01f ||
                   (remaining > solidLen && remaining < solidLen + blankLen + 0.01f);
        }

        private float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private PointF GetPointOnLine(PointF start, Vector2 dir, float distance)
        {
            return new PointF(
                start.X + dir.X * distance,
                start.Y + dir.Y * distance
            );
        }

        /// <summary>
        /// 二维向量结构体
        /// </summary>
        private struct Vector2
        {
            public float X, Y;

            public Vector2(float x, float y)
            {
                X = x; Y = y;
            }

            public float Length()
            {
                return (float)Math.Sqrt(X * X + Y * Y);
            }

            public Vector2 Normalized()
            {
                float len = Length();
                if (len < 1e-6f) return new Vector2(0, 0);
                return new Vector2(X / len, Y / len);
            }
        }
    }

    /// <summary>
    /// Graphics 扩展方法
    /// </summary>
    public static class GraphicsDashExtensions
    {
        /// <summary>
        /// 绘制虚线（使用自定义虚线分解器）
        /// </summary>
        public static List<(PointF start, PointF end)> DrawCustomDashLine(PointF start, PointF end, DashParameters parameters)
        {
            List<(PointF start, PointF end)> result = new List<(PointF start, PointF end)>();
            var decomposer = new LineDashDecomposer(parameters);
            var segments = decomposer.Decompose(start, end);

            foreach (var seg in segments)
            {
                if (seg.IsSolid)
                {
                    result.Add((seg.Start, seg.End));
                }
            }

            return result;
        }

        /// <summary>
        /// 绘制点虚线
        /// </summary>
        public static List<(PointF start, PointF end)> DrawDotDashLine(PointF start, PointF end,
            float dotLength = 3f, float blankLength = 5f, float dotRadius = 1.5f)
        {
            var parameters = new DashParameters
            {
                Type = DashType.DotDash,
                SolidLength = dotLength,
                BlankLength = blankLength,
                DotRadius = dotRadius
            };
            return DrawCustomDashLine(start, end, parameters);
        }

        /// <summary>
        /// 绘制段虚线（两端相等）
        /// </summary>
        public static List<(PointF start, PointF end)> DrawSegmentDashLine(PointF start, PointF end,
            float solidLength = 8f, float blankLength = 4f, bool keepEndsEqual = true)
        {
            var parameters = new DashParameters
            {
                Type = DashType.SegmentDash,
                SolidLength = solidLength,
                BlankLength = blankLength,
                KeepEndsEqual = keepEndsEqual
            };
            return DrawCustomDashLine(start, end, parameters);
        }

        /// <summary>
        /// 绘制标准 CAD 样式虚线（长实线 + 短空白）
        /// </summary>
        public static List<(PointF start, PointF end)> DrawCADDashLine(PointF start, PointF end)
        {
            var parameters = new DashParameters
            {
                Type = DashType.SegmentDash,
                SolidLength = 12f,
                BlankLength = 3f,
                KeepEndsEqual = true
            };
            return DrawCustomDashLine(start, end, parameters);
        }

        /// <summary>
        /// 绘制标准 CAD 样式点划线（长实线 + 短实线 + 空白）
        /// </summary>
        public static List<(PointF start, PointF end)> DrawCADCenterLine(PointF start, PointF end)
        {
            // 点划线：长实线(12) + 空白(2) + 短实线(3) + 空白(2)
            var parameters = new DashParameters
            {
                Type = DashType.SegmentDash,
                SolidLength = 12f,
                BlankLength = 2f,
                KeepEndsEqual = true
            };
            return DrawCustomDashLine(start, end, parameters);
        }
    }
}
