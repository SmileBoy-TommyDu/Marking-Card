using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.Interface
{
    /// <summary>
    /// 图形命令生成器接口，用于根据图形对象生成对应的打标命令
    /// </summary>
    public interface IShapeCommandGenerator
    {
        /// <summary>
        /// 支持的图形类型
        /// </summary>
        ShapeType SupportedType { get; }

        /// <summary>
        /// 生成打标命令
        /// </summary>
        /// <param name="draw">图形对象</param>
        /// <param name="processParam">工艺参数</param>
        /// <param name="currentProcessParam">当前工艺参数（用于变更检测）</param>
        /// <returns>打标命令集合</returns>
        IEnumerable<IMarkCommand> Generate(IShapeData draw, ProcessParam processParam,AdvancedFeatureParam? advancedFeatureParam,ref ProcessParam? currentProcessParam);

        /// <summary>
        /// 图形数据有效性验证
        /// </summary>
        /// <param name="draw">图形对象</param>
        /// <returns>是否有效</returns>
        bool Validate(IShapeData draw);
    }

    /// <summary>
    /// 图形命令生成器泛型基类，提供类型安全和通用逻辑
    /// </summary>
    /// <typeparam name="T">图形对象类型</typeparam>
    public abstract class ShapeCommandGeneratorBase<T> : IShapeCommandGenerator where T : class, IShapeData
    {
        public abstract ShapeType SupportedType { get; }

        public bool Validate(IShapeData draw)
        {
            if(draw == null) return false;
            if (draw is not T typed)
                return false;

            return ValidateCore(typed);
        }


        public static PointF GetCompensationVector(PointF p0, PointF p1, float length)
        {
            PointF vec = new PointF(p1.X - p0.X, p1.Y - p0.Y);

            float dist = (float)Math.Sqrt(vec.X * vec.X + vec.Y * vec.Y);

            if (dist > 0)
            {
                vec.X = vec.X / dist * length;
                vec.Y = vec.Y / dist * length;
            }

            return vec;
        }

        /// <summary>
        /// 图形数据有效性验证（由子类实现）
        /// </summary>
        protected abstract bool ValidateCore(T draw);

        public IEnumerable<IMarkCommand> Generate(IShapeData draw, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam, ref ProcessParam? currentProcessParam)
        {
            if (draw is not T typed)
                return Enumerable.Empty<IMarkCommand>();

            var raw = GenerateCore(typed, processParam, advancedFeatureParam, ref currentProcessParam);

            // 统一后处理：将相交镂空点附近的直线段用抬笔来断开，
            // 从而在实际打标时避免交点被前后图形重复打标。
            return ApplyIntersectionSkip(raw.ToList(), draw);
        }

        /// <summary>
        /// 生成图形特定的打标命令（由子类实现）
        /// </summary>
        protected abstract IEnumerable<IMarkCommand> GenerateCore(T draw, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam,ref ProcessParam? currentProcessParam);

        /// <summary>
        /// 确保工艺参数命令已添加（如果参数发生变化）
        /// </summary>
        public static List<IMarkCommand> EnsureParamCommands(ProcessParam processParam,
            ref ProcessParam? currentProcessParam)
        {
            var commands = new List<IMarkCommand>();
            if (NeedUpdateProcessParam(currentProcessParam, processParam))
            {
                commands.AddRange(CreateParamCommands(processParam));
                currentProcessParam = processParam;
            }
            return commands;
        }

        /// <summary>
        /// 检查工艺参数是否需要更新
        /// </summary>
        public static bool NeedUpdateProcessParam(ProcessParam? current, ProcessParam next)
        {
            if (current == null) return true;
            return current.Frequency != next.Frequency
            || current.Pulse != next.Pulse
            || current.Power != next.Power
            || current.MarkSpeed != next.MarkSpeed
            || current.JumpSpeed != next.JumpSpeed
            || current.MarkDelay != next.MarkDelay
            || current.JumpDelay != next.JumpDelay
            || current.PolyDelay != next.PolyDelay
            || current.LaserOnDelay != next.LaserOnDelay
            || current.LaserOffDelay != next.LaserOffDelay;
        }

        /// <summary>
        /// 创建工艺参数命令
        /// </summary>
        public static List<IMarkCommand> CreateParamCommands(ProcessParam param)
        {
            return new List<IMarkCommand>
            {
                new ModifyFrequencyAndPulsesWidthCommand { Frequency = (float)param.Frequency, PulsesWidth = (float)param.Pulse },
                new ModifyPowerCommand { Power = (float)param.Power },
                new ModifySpeedCommand { MarkSpeed = (float)param.MarkSpeed, JumpSpeed = (float)param.JumpSpeed },
                new ModifyScannerDelayCommand { MarkDelay = (int)param.MarkDelay, JumpDelay = (int)param.JumpDelay, CornerDelay = (int)param.PolyDelay },
                new ModifyLaserDelayCommand { LaserOnDelay = (int)param.LaserOnDelay, LaserOffDelay = (int)param.LaserOffDelay }
            };
        }

        public static PointF ToPointF((float X, float Y) p) => new PointF(p.X, p.Y);

        public static double DistanceTo((float X, float Y) point1, (float X, float Y) point2) => Distance(point1, point2);

        public static double Distance((float X, float Y) a, (float X, float Y) b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static (double X, double Y) Normalize(double x, double y)
        {
            double len = Math.Sqrt(x * x + y * y);
            if (len <= 1e-12) return (0, 0);
            return (x / len, y / len);
        }

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// 构造圆角几何
        /// </summary>
        public static CornerFillet BuildCornerFillet((float X, float Y) prev, (float X, float Y) current, (float X, float Y) next, double cornerRadius)
        {
            var prevVec = Normalize(prev.X - current.X, prev.Y - current.Y);
            var nextVec = Normalize(next.X - current.X, next.Y - current.Y);

            if (cornerRadius <= 1e-9 || (Math.Abs(prevVec.X + nextVec.X) < 1e-9 && Math.Abs(prevVec.Y + nextVec.Y) < 1e-9))
            {
                return new CornerFillet(current, current, current, 0, 0, false);
            }

            double lenPrev = Distance(prev, current);
            double lenNext = Distance(next, current);
            double dot = Clamp(prevVec.X * nextVec.X + prevVec.Y * nextVec.Y, -1.0, 1.0);
            double theta = Math.Acos(dot);

            if (theta < 1e-6 || Math.Abs(Math.PI - theta) < 1e-6)
            {
                return new CornerFillet(current, current, current, 0, 0, false);
            }

            double maxRadiusByEdges = Math.Min(lenPrev, lenNext) * Math.Tan(theta / 2.0);
            double radius = Math.Min(cornerRadius, maxRadiusByEdges);

            if (radius <= 1e-9)
            {
                return new CornerFillet(current, current, current, 0, 0, false);
            }

            double offset = radius / Math.Tan(theta / 2.0);

            var start = (X: (float)(current.X + prevVec.X * offset),
                         Y: (float)(current.Y + prevVec.Y * offset));

            var end = (X: (float)(current.X + nextVec.X * offset),
                       Y: (float)(current.Y + nextVec.Y * offset));

            var inDir = Normalize(current.X - prev.X, current.Y - prev.Y);
            var outDir = Normalize(next.X - current.X, next.Y - current.Y);
            double sweepRad = Math.Atan2(inDir.X * outDir.Y - inDir.Y * outDir.X, inDir.X * outDir.X + inDir.Y * outDir.Y);
            double sweepDeg = sweepRad * 180.0 / Math.PI;

            // 计算圆弧几何圆心：从 start 点沿入边方向后退 radius 距离（旋转90°朝向内侧）
            double sweepSign = Math.Sign(sweepDeg);
            var center = (
                X: (float)(start.X - sweepSign * radius * inDir.Y),
                Y: (float)(start.Y + sweepSign * radius * inDir.X)
            );

            return new CornerFillet(start, end, center, radius, sweepDeg, true);
        }

        /// <summary>
        /// 追加单个圆角圆弧命令
        /// </summary>
        public static void AddCornerArc(List<IMarkCommand> commands, CornerFillet fillet)
        {
            if (!fillet.HasArc) return;

            commands.Add(new MarkCircleCommand
            {
                StartPoint = ToPointF(fillet.Start),
                Center = ToPointF(fillet.Center),
                Angle = (float)fillet.SweepDeg,
                Radius = (float)fillet.Radius,
            });
        }

        /// <summary>
        /// 圆角半径归一化
        /// </summary>
        public static List<float> NormalizeCornerRadius(List<float>? radius)
        {
            var result = new List<float> { 0f, 0f, 0f, 0f };
            if (radius == null || radius.Count == 0) return result;

            if (radius.Count == 1)
            {
                float r = Math.Max(0, radius[0]);
                result[0] = r; result[1] = r; result[2] = r; result[3] = r;
                return result;
            }

            for (int i = 0; i < 4 && i < radius.Count; i++)
            {
                result[i] = Math.Max(0, radius[i]);
            }

            return result;
        }

        /// <summary>
        /// 多段线命令生成（共享逻辑，供 PolyLine、Polygon、Rectangle 共用）
        /// </summary>
        protected static List<IMarkCommand> GeneratePolyLineCommands(IShapeData draw, ProcessParam processParam, bool isClose, AdvancedFeatureParam? advancedFeatureParam, ref ProcessParam? currentProcessParam)
        {
            var commands = EnsureParamCommands(processParam, ref currentProcessParam);

            // 轮廓点副本（需要写入补偿）
            var pts = draw.OutlinePoints.Select(p => (p.X, p.Y)).ToList();

            if (!draw.IsClockwise)
            {
                pts.Reverse();
            }

            //封闭图形不允许收尾延申
            if (advancedFeatureParam != null&&!isClose)
            {
                // 入刀延伸：起点沿反方向（远离 pts[1]）延伸
                if (Math.Abs(advancedFeatureParam.RunInCompensationLength) > 0.00001)
                {
                    var compensationVector = GetCompensationVector(new PointF(pts[1].X, pts[1].Y), new PointF(pts[0].X, pts[0].Y), advancedFeatureParam.RunInCompensationLength);
                    pts[0] = (pts[0].X + compensationVector.X, pts[0].Y + compensationVector.Y);
                }

                // 出刀延伸：终点沿正方向（远离前一节点）延伸
                if (Math.Abs(advancedFeatureParam.RunOutCompensationLength) > 0.00001)
                {
                    int lastIndex = pts.Count - 1;
                    PointF p0 = new PointF(pts[lastIndex - 1].X, pts[lastIndex - 1].Y);
                    PointF p1 = new PointF(pts[lastIndex].X, pts[lastIndex].Y);

                    //if (isClose)
                    //{
                    //    p0 = new PointF(pts[lastIndex].X, pts[lastIndex].Y);
                    //    p1 = new PointF(pts[0].X, pts[0].Y);
                    //}

                    var compensationVector = GetCompensationVector(p0, p1, advancedFeatureParam.RunOutCompensationLength);

                    //if (isClose)
                    //    pts[0] = (pts[0].X + compensationVector.X, pts[0].Y + compensationVector.Y);
                    //else
                        pts[lastIndex] = (pts[lastIndex].X + compensationVector.X, pts[lastIndex].Y + compensationVector.Y);
                }
            }

            for (int j = 0; j < Math.Max(1, processParam.RepeatCount); j++)
            {
                if (j == 0 && pts.Count > 1)
                    commands.Add(new JumpCommand { Point = new PointF(pts[0].X, pts[0].Y) });

                for (int i = 1; i < pts.Count; i++)
                    commands.Add(new MarkLineCommand { EndPoint = new PointF(pts[i].X, pts[i].Y) });

                if (isClose && pts.Count > 2)
                    commands.Add(new MarkLineCommand { EndPoint = new PointF(pts[0].X, pts[0].Y) });
            }

            return commands;
        }

        #region 虚线命令生成

        /// <summary>段虚线默认实线段长度（mm）</summary>
        private const float DefaultDashedSolidLength = 1.0f;
        /// <summary>段虚线默认空白段长度（mm）</summary>
        private const float DefaultDashedGapLength = 0.5f;
        /// <summary>点虚线默认实线段长度（mm）</summary>
        private const float DefaultDottedSolidLength = 0.3f;
        /// <summary>点虚线默认空白段长度（mm）</summary>
        private const float DefaultDottedGapLength = 0.4f;

        /// <summary>
        /// 根据轮廓样式获取虚线参数
        /// </summary>
        private static (float solidLen, float gapLen) GetDashParams(OutlineStyle style)
        {
            return style switch
            {
                OutlineStyle.Dotted => (DefaultDottedSolidLength, DefaultDottedGapLength),
                _ => (DefaultDashedSolidLength, DefaultDashedGapLength),
            };
        }

        /// <summary>
        /// 虚线多段线命令生成（共享逻辑，供 PolyLine、Polygon 等使用）。
        /// <para>
        /// 将多段线的每条边按 (solidLen + gapLen) 周期分解为 MarkDashedLineCommand，
        /// 空白段以 JumpCommand 跳过。
        /// </para>
        /// </summary>
        protected static List<IMarkCommand> GenerateDashedPolyLineCommands(
            IShapeData draw, ProcessParam processParam, bool isClose,
            AdvancedFeatureParam? advancedFeatureParam, ref ProcessParam? currentProcessParam)
        {
            var commands = EnsureParamCommands(processParam, ref currentProcessParam);
            var pts = draw.OutlinePoints.Select(p => (p.X, p.Y)).ToList();

            if (pts.Count < 2) return commands;

            if (!draw.IsClockwise)
                pts.Reverse();

            // 入刀延伸：起点沿反方向（远离 pts[1]）延伸
            if (advancedFeatureParam != null && Math.Abs(advancedFeatureParam.RunInCompensationLength) > 0.00001)
            {
                var cv = GetCompensationVector(
                    new PointF(pts[1].X, pts[1].Y),
                    new PointF(pts[0].X, pts[0].Y),
                    advancedFeatureParam.RunInCompensationLength);
                pts[0] = (pts[0].X + cv.X, pts[0].Y + cv.Y);
            }

            // 出刀延伸：终点沿正方向（远离前一节点）延伸
            if (advancedFeatureParam != null && Math.Abs(advancedFeatureParam.RunOutCompensationLength) > 0.00001)
            {
                int lastIndex = pts.Count - 1;
                PointF p0, p1;
                if (isClose)
                {
                    p0 = new PointF(pts[lastIndex].X, pts[lastIndex].Y);
                    p1 = new PointF(pts[0].X, pts[0].Y);
                }
                else
                {
                    p0 = new PointF(pts[lastIndex - 1].X, pts[lastIndex - 1].Y);
                    p1 = new PointF(pts[lastIndex].X, pts[lastIndex].Y);
                }
                var cv = GetCompensationVector(p0, p1, advancedFeatureParam.RunOutCompensationLength);
                if (isClose)
                    pts[0] = (pts[0].X + cv.X, pts[0].Y + cv.Y);
                else
                    pts[lastIndex] = (pts[lastIndex].X + cv.X, pts[lastIndex].Y + cv.Y);
            }

            var (solidLen, gapLen) = GetDashParams(draw.OutlineStyle);
            int repeatCount = Math.Max(1, processParam.RepeatCount);

            for (int rep = 0; rep < repeatCount; rep++)
            {
                // 跳转到起点
                commands.Add(new JumpCommand { Point = new PointF(pts[0].X, pts[0].Y) });

                // 构建边列表（含闭合边）
                var edges = new List<(PointF Start, PointF End)>();
                for (int i = 0; i < pts.Count - 1; i++)
                    edges.Add((new PointF(pts[i].X, pts[i].Y), new PointF(pts[i + 1].X, pts[i + 1].Y)));
                if (isClose && pts.Count > 2)
                    edges.Add((new PointF(pts[pts.Count - 1].X, pts[pts.Count - 1].Y), new PointF(pts[0].X, pts[0].Y)));

                // 逐边分解虚线
                foreach (var (edgeStart, edgeEnd) in edges)
                {
                    float dx = edgeEnd.X - edgeStart.X;
                    float dy = edgeEnd.Y - edgeStart.Y;
                    float edgeLen = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (edgeLen < 0.001f) continue;

                    float nx = dx / edgeLen;
                    float ny = dy / edgeLen;

                    var dashPoints = new List<PointF>();
                    float pos = 0;
                    bool isDash = true;

                    while (pos < edgeLen - 0.001f)
                    {
                        float segLen = isDash ? solidLen : gapLen;
                        float end = Math.Min(pos + segLen, edgeLen);
                        var pt = new PointF(edgeStart.X + nx * end, edgeStart.Y + ny * end);

                        if (isDash)
                        {
                            dashPoints.Add(pt);
                        }
                        else
                        {
                            // 空白段：先提交已累积的实线段，再跳转
                            if (dashPoints.Count > 0)
                            {
                                commands.Add(new MarkDashedLineCommand { DashArray = new List<PointF>(dashPoints) });
                                dashPoints.Clear();
                            }
                            //commands.Add(new JumpCommand { Point = pt });
                        }

                        pos = end;
                        isDash = !isDash;
                    }

                    // 提交该边剩余的实线段
                    if (dashPoints.Count > 0)
                    {
                        commands.Add(new MarkDashedLineCommand { DashArray = new List<PointF>(dashPoints) });
                    }
                }
            }

            return commands;
        }

        #endregion

        

        public readonly record struct CornerFillet((float X, float Y) Start, (float X, float Y) End, (float X, float Y) Center, double Radius, double SweepDeg, bool HasArc);

        /// <summary>
        /// 相交镂空后处理：扫描命令序列，将所有 MarkLineCommand/MarkDashedLineCommand 线段被
        /// 镂空圈覆盖的部分用 JumpCommand 断开（抬笔跳过）；MarkPointCommand 在圈内则丢弃。
        /// 本版本暂不对 MarkCircleCommand/MarkEllipseCommand 做裁剪（维持原样）。
        /// </summary>
        protected static List<IMarkCommand> ApplyIntersectionSkip(List<IMarkCommand> commands, IShapeData draw)
        {
            if (commands == null || commands.Count == 0) return commands ?? new List<IMarkCommand>();
            if (draw == null) return commands;
            var skips = draw.IntersectionSkipPoints;
            float r = draw.IntersectionSkipRadius;
            if (skips == null || skips.Count == 0 || r <= 0f) return commands;

            int selfCount = draw.SelfIntersectionSkipCount;

            // 自交点单侧裁剪：路径第一次经过自交点时为“over”线段（不裁剪），
            // 第二次经过时为“under”线段（裁剪）。
            // selfPassageCount[i] 记录第 i 个自交点已被“通过”的次数（连续线段组算一次）。
            // selfInsideCircle[i] 记录上一段是否在该圆内，用于检测“进入”边界。
            int[]? selfPassageCount = selfCount > 0 ? new int[selfCount] : null;
            bool[]? selfInsideCircle = selfCount > 0 ? new bool[selfCount] : null;

            var result = new List<IMarkCommand>(commands.Count);
            PointF cursor = PointF.Empty;
            bool hasCursor = false;

            foreach (var cmd in commands)
            {
                switch (cmd)
                {
                    case JumpCommand jc:
                        cursor = jc.Point;
                        hasCursor = true;
                        result.Add(cmd);
                        ResetSelfInsideState(selfInsideCircle);
                        break;

                    case MarkLineCommand ml when hasCursor:
                    {
                        // 获取对当前线段有效的跳点列表（自交点需第二次经过才生效）
                        var effectiveSkips = GetEffectiveSkips(cursor, ml.EndPoint, skips, r, selfCount, selfPassageCount, selfInsideCircle);
                        var segs = effectiveSkips.Count > 0
                            ? ClipSegmentByCircles(cursor, ml.EndPoint, effectiveSkips, r)
                            : new List<(PointF, PointF)> { (cursor, ml.EndPoint) };
                        if (segs.Count == 0)
                        {
                            // 整条线段都落在镂空圈内 -> 直接抬笔跳到终点
                            result.Add(new JumpCommand { Point = ml.EndPoint });
                        }
                        else
                        {
                            for (int i = 0; i < segs.Count; i++)
                            {
                                var (s, e) = segs[i];
                                if (i == 0)
                                {
                                    if (!PointEquals(s, cursor))
                                        result.Add(new JumpCommand { Point = s });
                                    result.Add(new MarkLineCommand { EndPoint = e });
                                }
                                else
                                {
                                    result.Add(new JumpCommand { Point = s });
                                    result.Add(new MarkLineCommand { EndPoint = e });
                                }
                            }
                        }
                        cursor = ml.EndPoint;
                        break;
                    }

                    //case MarkDashedLineCommand dashed:
                    //{
                    //    var dashArray = dashed.DashArray;
                    //    if (dashArray == null || dashArray.Count == 0)
                    //    {
                    //        result.Add(cmd);
                    //        break;
                    //    }

                    //    // 确保有 cursor（使用 StartPoint 或第一个点作为起点）
                    //    if (!hasCursor)
                    //    {
                    //        cursor = dashed.StartPoint ?? dashArray[0];
                    //        hasCursor = true;
                    //    }

                    //    // 对每个虚线段执行跳点裁剪：
                    //    // DashArray 交替排列：偶数索引=实线终点（mark），奇数索引=空白终点（jump）
                    //    PointF pos = cursor;
                    //    for (int i = 0; i < dashArray.Count; i++)
                    //    {
                    //        if (i % 2 == 0)
                    //        {
                    //            // 实线段：用跳点圈裁剪
                    //            var segs = ClipSegmentByCircles(pos, dashArray[i], skips, r);
                    //            if (segs.Count == 0)
                    //            {
                    //                result.Add(new JumpCommand { Point = dashArray[i] });
                    //            }
                    //            else
                    //            {
                    //                for (int si = 0; si < segs.Count; si++)
                    //                {
                    //                    var (s, e) = segs[si];
                    //                    if (si == 0)
                    //                    {
                    //                        if (!PointEquals(s, pos))
                    //                            result.Add(new JumpCommand { Point = s });
                    //                        result.Add(new MarkLineCommand { EndPoint = e });
                    //                    }
                    //                    else
                    //                    {
                    //                        result.Add(new JumpCommand { Point = s });
                    //                        result.Add(new MarkLineCommand { EndPoint = e });
                    //                    }
                    //                }
                    //            }
                    //        }
                    //        else
                    //        {
                    //            // 空白段：直接跳转
                    //            result.Add(new JumpCommand { Point = dashArray[i] });
                    //        }
                    //        pos = dashArray[i];
                    //    }
                    //    cursor = pos;
                    //    break;
                    //}

                    case MarkCircleCommand circle when hasCursor:
                    {
                        // 将圆弧离散为线段，逐段裁剪
                        var arcSegments = SampleArcToSegments(circle);
                        if (arcSegments.Count == 0)
                        {
                            result.Add(cmd);
                        }
                        else
                        {
                            PointF arcCursor = cursor;
                            for (int ai = 0; ai < arcSegments.Count; ai++)
                            {
                                var (arcStart, arcEnd) = arcSegments[ai];
                                var effectiveSkips = GetEffectiveSkips(arcStart, arcEnd, skips, r, selfCount, selfPassageCount, selfInsideCircle);
                                var segs = effectiveSkips.Count > 0
                                    ? ClipSegmentByCircles(arcStart, arcEnd, effectiveSkips, r)
                                    : new List<(PointF, PointF)> { (arcStart, arcEnd) };
                                    if (segs.Count == 0)
                                {
                                    result.Add(new JumpCommand { Point = arcEnd });
                                }
                                else
                                {
                                    for (int si = 0; si < segs.Count; si++)
                                    {
                                        var (s, e) = segs[si];
                                        if (si == 0)
                                        {
                                            if (!PointEquals(s, arcCursor))
                                                result.Add(new JumpCommand { Point = s });
                                            result.Add(new MarkLineCommand { EndPoint = e });
                                        }
                                        else
                                        {
                                            result.Add(new JumpCommand { Point = s });
                                            result.Add(new MarkLineCommand { EndPoint = e });
                                        }
                                    }
                                }
                                arcCursor = arcEnd;
                            }
                        }
                        // 圆弧结束后 cursor 位于弧终点
                        cursor = ComputeArcEndPoint(circle);
                        break;
                    }

                    case MarkEllipseCommand ellipse when hasCursor:
                    {
                        // 将椭圆离散为线段，逐段裁剪
                        var ellipseSegments = SampleEllipseToSegments(ellipse);
                        if (ellipseSegments.Count == 0)
                        {
                            result.Add(cmd);
                        }
                        else
                        {
                            PointF eCursor = cursor;
                            for (int ei = 0; ei < ellipseSegments.Count; ei++)
                            {
                                var (eStart, eEnd) = ellipseSegments[ei];
                                var effectiveSkips = GetEffectiveSkips(eStart, eEnd, skips, r, selfCount, selfPassageCount, selfInsideCircle);
                                var segs = effectiveSkips.Count > 0
                                    ? ClipSegmentByCircles(eStart, eEnd, effectiveSkips, r)
                                    : new List<(PointF, PointF)> { (eStart, eEnd) };
                                    if (segs.Count == 0)
                                {
                                    result.Add(new JumpCommand { Point = eEnd });
                                }
                                else
                                {
                                    for (int si = 0; si < segs.Count; si++)
                                    {
                                        var (s, e) = segs[si];
                                        if (si == 0)
                                        {
                                            if (!PointEquals(s, eCursor))
                                                result.Add(new JumpCommand { Point = s });
                                            result.Add(new MarkLineCommand { EndPoint = e });
                                        }
                                        else
                                        {
                                            result.Add(new JumpCommand { Point = s });
                                            result.Add(new MarkLineCommand { EndPoint = e });
                                        }
                                    }
                                }
                                eCursor = eEnd;
                            }
                        }
                        cursor = ComputeEllipseEndPoint(ellipse);
                        break;
                    }

                    case MarkPointCommand mp:
                        if (!IsInAnySkipCircle(mp.Point, skips, r))
                            result.Add(cmd);
                        cursor = mp.Point;
                        hasCursor = true;
                        break;

                    default:
                        result.Add(cmd);
                        break;
                }
            }
            return result;
        }

        /// <summary>
        /// 获取对当前线段有效的跳点列表。
        /// 对于自交点（前 selfCount 个），第一次通过时为“over”线段不裁剪，
        /// 第二次通过时为“under”线段才裁剪。
        /// “通过”指连续线段组从圈外进入圈内的一次完整穿越。
        /// 多图形交叉点（selfCount 之后的）始终裁剪。
        /// </summary>
        private static IReadOnlyList<(float X, float Y)> GetEffectiveSkips(
            PointF segStart, PointF segEnd,
            IReadOnlyList<(float X, float Y)> allSkips, float radius,
            int selfCount, int[]? selfPassageCount, bool[]? selfInsideCircle)
        {
            if (selfCount <= 0 || selfPassageCount == null || selfInsideCircle == null)
                return allSkips; // 无自交点，全部生效

            float dx = segEnd.X - segStart.X;
            float dy = segEnd.Y - segStart.Y;
            float lenSq = dx * dx + dy * dy;

            // 更新通过计数：检测“进入”边界（上一段不在圈内，当前段在圈内）
            for (int i = 0; i < selfCount; i++)
            {
                bool nowInside = SegmentPassesCircle(segStart, segEnd, allSkips[i], radius, lenSq);
                if (nowInside && !selfInsideCircle[i])
                {
                    // 新的一次通过（进入边界）
                    selfPassageCount[i]++;
                }
                selfInsideCircle[i] = nowInside;
            }

            // 检查是否有自交点需排除（仅通过一次的 = "over"）
            bool anyExcluded = false;
            for (int i = 0; i < selfCount; i++)
            {
                if (selfInsideCircle[i] && selfPassageCount[i] < 2)
                {
                    anyExcluded = true;
                    break;
                }
            }

            if (!anyExcluded)
                return allSkips; // 所有自交点均已是第二次通过，全部生效

            // 过滤出生效的跳点
            var filtered = new List<(float, float)>(allSkips.Count);
            for (int i = 0; i < allSkips.Count; i++)
            {
                if (i < selfCount)
                {
                    // 自交点：仅当通过次数 >= 2 时生效（“under”线段）
                    if (selfPassageCount[i] >= 2)
                        filtered.Add(allSkips[i]);
                }
                else
                {
                    // 多图形交叉点：始终生效
                    filtered.Add(allSkips[i]);
                }
            }
            return filtered;
        }

        /// <summary>
        /// Jump 命令后重置自交点“在圈内”状态（跳转中断了连续性）。
        /// </summary>
        private static void ResetSelfInsideState(bool[]? selfInsideCircle)
        {
            if (selfInsideCircle == null) return;
            Array.Clear(selfInsideCircle, 0, selfInsideCircle.Length);
        }

        /// <summary>
        /// 判断线段 [a,b] 是否经过圆盘 (cx,cy,r)。
        /// </summary>
        private static bool SegmentPassesCircle(PointF a, PointF b, (float X, float Y) center, float radius, float lenSq)
        {
            if (lenSq <= 1e-12f) return false;
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            return TryGetCircleSegmentRange(center.X, center.Y, radius, a.X, a.Y, dx, dy, lenSq, out _, out _);
        }

        /// <summary>
        /// 将线段 [a, b] 被一组圆盘镂空后返回保留的子线段列表（按参数 t 从小到大）。
        /// </summary>
        private static List<(PointF Start, PointF End)> ClipSegmentByCircles(
            PointF a, PointF b, IReadOnlyList<(float X, float Y)> skips, float radius)
        {
            var keep = new List<(float, float)> { (0f, 1f) };
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq <= 1e-12f) return new List<(PointF, PointF)>();

            foreach (var sp in skips)
            {
                if (!TryGetCircleSegmentRange(sp.X, sp.Y, radius, a.X, a.Y, dx, dy, lenSq, out float tEnter, out float tExit))
                    continue;
                keep = SubtractRange(keep, tEnter, tExit);
                if (keep.Count == 0) break;
            }

            var result = new List<(PointF, PointF)>(keep.Count);
            foreach (var (t0, t1) in keep)
            {
                var p0 = new PointF(a.X + dx * t0, a.Y + dy * t0);
                var p1 = new PointF(a.X + dx * t1, a.Y + dy * t1);
                if (PointEquals(p0, p1)) continue;
                result.Add((p0, p1));
            }
            return result;
        }

        /// <summary>
        /// 求线段参数 t 与圆盘(cx,cy,r)的交集（被 cover 的 t 区间）。
        /// </summary>
        private static bool TryGetCircleSegmentRange(
            float cx, float cy, float r,
            float ax, float ay, float dx, float dy, float lenSq,
            out float tEnter, out float tExit)
        {
            tEnter = 0f; tExit = 0f;
            float fx = ax - cx;
            float fy = ay - cy;
            float A = lenSq;
            float B = 2f * (fx * dx + fy * dy);
            float C = fx * fx + fy * fy - r * r;
            float disc = B * B - 4f * A * C;
            if (disc < 0) return false;
            float sq = (float)Math.Sqrt(disc);
            float t1 = (-B - sq) / (2f * A);
            float t2 = (-B + sq) / (2f * A);
            if (t2 < 0f || t1 > 1f) return false;
            tEnter = Math.Max(0f, t1);
            tExit = Math.Min(1f, t2);
            return tExit > tEnter;
        }

        /// <summary>
        /// 从区间列表中减去 [lo, hi] 区间，返回裁剪后的列表。
        /// </summary>
        private static List<(float, float)> SubtractRange(List<(float, float)> ranges, float lo, float hi)
        {
            var result = new List<(float, float)>();
            foreach (var (s, e) in ranges)
            {
                if (hi <= s || lo >= e)
                {
                    result.Add((s, e));
                    continue;
                }
                if (lo > s) result.Add((s, lo));
                if (hi < e) result.Add((hi, e));
            }
            return result;
        }

        private static bool IsInAnySkipCircle(PointF p, IReadOnlyList<(float X, float Y)> skips, float r)
        {
            float r2 = r * r;
            foreach (var sp in skips)
            {
                float dx = sp.X - p.X;
                float dy = sp.Y - p.Y;
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }

        private static bool PointEquals(PointF a, PointF b)
        {
            return Math.Abs(a.X - b.X) < 1e-4f && Math.Abs(a.Y - b.Y) < 1e-4f;
        }

        #region 圆弧/椭圆离散采样（用于跳点裁剪）

        /// <summary>圆弧离散步长（度）</summary>
        private const float ArcSampleStepDeg = 2f;

        /// <summary>
        /// 将 MarkCircleCommand 圆弧离散为线段序列。
        /// </summary>
        private static List<(PointF Start, PointF End)> SampleArcToSegments(MarkCircleCommand circle)
        {
            var result = new List<(PointF, PointF)>();
            float cx = circle.Center.X;
            float cy = circle.Center.Y;
            float radius = circle.Radius;
            float totalAngle = circle.Angle; // 度，正=逆时针，负=顺时针

            if (radius <= 0 || Math.Abs(totalAngle) < 0.01f)
                return result;

            // 计算起始角度
            float startAngleRad = (float)Math.Atan2(
                circle.StartPoint.Y - cy,
                circle.StartPoint.X - cx);

            float totalAngleRad = totalAngle * (float)(Math.PI / 180.0);
            int segCount = Math.Max(1, (int)Math.Ceiling(Math.Abs(totalAngle) / ArcSampleStepDeg));
            float stepRad = totalAngleRad / segCount;

            PointF prev = circle.StartPoint;
            for (int i = 1; i <= segCount; i++)
            {
                float angle = startAngleRad + stepRad * i;
                var pt = new PointF(
                    cx + radius * (float)Math.Cos(angle),
                    cy + radius * (float)Math.Sin(angle));
                result.Add((prev, pt));
                prev = pt;
            }
            return result;
        }

        /// <summary>
        /// 计算圆弧终点。
        /// </summary>
        private static PointF ComputeArcEndPoint(MarkCircleCommand circle)
        {
            float cx = circle.Center.X;
            float cy = circle.Center.Y;
            float radius = circle.Radius;
            float startAngleRad = (float)Math.Atan2(
                circle.StartPoint.Y - cy,
                circle.StartPoint.X - cx);
            float totalAngleRad = circle.Angle * (float)(Math.PI / 180.0);
            float endAngle = startAngleRad + totalAngleRad;
            return new PointF(
                cx + radius * (float)Math.Cos(endAngle),
                cy + radius * (float)Math.Sin(endAngle));
        }

        /// <summary>
        /// 将 MarkEllipseCommand 椭圆（弧）离散为线段序列。
        /// 支持完整椭圆和部分椭圆弧。
        /// </summary>
        private static List<(PointF Start, PointF End)> SampleEllipseToSegments(MarkEllipseCommand ellipse)
        {
            var result = new List<(PointF, PointF)>();
            float cx = ellipse.Center.X;
            float cy = ellipse.Center.Y;
            float a = (float)ellipse.MajorRadius;
            float b = (float)ellipse.MinorRadius;
            float alphaRad = (float)(ellipse.Alpha * Math.PI / 180.0);

            if (a <= 0 || b <= 0)
                return result;

            float cosAlpha = (float)Math.Cos(alphaRad);
            float sinAlpha = (float)Math.Sin(alphaRad);

            float startRad = (float)(ellipse.StartAngle * Math.PI / 180.0);
            float sweepRad = (float)(ellipse.SweepAngle * Math.PI / 180.0);
            float totalDeg = (float)Math.Abs(ellipse.SweepAngle);

            int segCount = Math.Max(4, (int)Math.Ceiling(totalDeg / ArcSampleStepDeg));
            float stepRad = sweepRad / segCount;

            PointF prev = EllipsePointAt(cx, cy, a, b, cosAlpha, sinAlpha, startRad);

            for (int i = 1; i <= segCount; i++)
            {
                float t = startRad + stepRad * i;
                var pt = EllipsePointAt(cx, cy, a, b, cosAlpha, sinAlpha, t);
                result.Add((prev, pt));
                prev = pt;
            }
            return result;
        }

        /// <summary>
        /// 计算椭圆上指定参数角度的世界坐标点。
        /// </summary>
        private static PointF EllipsePointAt(
            float cx, float cy, float a, float b,
            float cosAlpha, float sinAlpha, float t)
        {
            float localX = a * (float)Math.Cos(t);
            float localY = b * (float)Math.Sin(t);
            return new PointF(
                cx + localX * cosAlpha - localY * sinAlpha,
                cy + localX * sinAlpha + localY * cosAlpha);
        }

        /// <summary>
        /// 计算椭圆（弧）终点。
        /// </summary>
        private static PointF ComputeEllipseEndPoint(MarkEllipseCommand ellipse)
        {
            float cx = ellipse.Center.X;
            float cy = ellipse.Center.Y;
            float a = (float)ellipse.MajorRadius;
            float b = (float)ellipse.MinorRadius;
            float alphaRad = (float)(ellipse.Alpha * Math.PI / 180.0);
            float cosAlpha = (float)Math.Cos(alphaRad);
            float sinAlpha = (float)Math.Sin(alphaRad);
            float endRad = (float)((ellipse.StartAngle + ellipse.SweepAngle) * Math.PI / 180.0);
            return EllipsePointAt(cx, cy, a, b, cosAlpha, sinAlpha, endRad);
        }

        #endregion
    }
}
