using DrSoft.Drawing.DTO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace DrSoft.Drawing.Controls.DXFHelper.Parser
{
    // ================================================================
    // 解析选项
    // ================================================================
    public sealed class DxfParseOptions
    {
        /// <summary>批次阈值：积累多少个实体后推入 Channel（默认 10000）</summary>
        public int BatchSize { get; set; } = 10_000;

        /// <summary>进度上报间隔字节数（默认 256KB）</summary>
        public long ProgressIntervalBytes { get; set; } = 256 * 1024;

        /// <summary>嵌套 INSERT 最大展开深度（默认 32）</summary>
        public int MaxExpandDepth { get; set; } = 32;

        /// <summary>
        /// 将 LWPOLYLINE / 老式 POLYLINE+VERTEX 拆为 Line/Arc（默认 true）。
        /// false 时回调中保留 DxfLwPolyline，由调用方处理 Bulge。
        /// </summary>
        public bool ExplodeLwPolyline { get; set; } = true;

        /// <summary>
        /// Channel 背压容量（默认 3）。
        /// 解析线程写满后等待消费线程取走一个槽，两者真正并行。
        /// </summary>
        public int ChannelCapacity { get; set; } = 3;
    }

    // ================================================================
    // 解析汇总
    // ================================================================
    public sealed class ParseSummary
    {
        public int Lines { get; internal set; }
        public int Arcs { get; internal set; }
        public int Circles { get; internal set; }
        public int Ellipses { get; internal set; }  // 新增
        public int Points { get; internal set; }
        public int Polys { get; internal set; }   // 未拆分时的 LWPOLYLINE 数
        public int Expanded { get; internal set; }   // 从 INSERT 展开的实体数

        public int Rectangles { get; internal set; }

        public int Texts { get; internal set; }
        public int Splines { get; internal set; }
        public int Hatches { get; internal set; }
        public double ParseMs { get; internal set; }
        public double TotalMs { get; internal set; }
        public int Total => Lines + Arcs + Circles + Points + Polys + Expanded + Ellipses + Rectangles + Texts + Splines + Hatches;
    }

    // ================================================================
    // 批次快照：Channel 中传递的数据单元
    //
    // 解析器把攒满的 List 引用"移交"给 BatchSnapshot（零拷贝），
    // 换一个新 List 继续填，消费端在回调期间安全持有。
    // ================================================================
    public sealed class BatchSnapshot
    {
        public List<DxfEntity> Entities = null!;
        internal static List<DxfEntity> RentList() => new List<DxfEntity>(16_000);
    }

    // ================================================================
    // 主解析器
    // ================================================================
    public sealed class DxfParser
    {
        private readonly string _path;
        private readonly DxfParseOptions _opts;

        /// <summary>
        /// 批次回调（运行在独立消费线程，与解析线程并行）。
        ///
        ///   · 无需 ToArray()，snap.Entities 在回调期间稳定
        ///   · 无需 Task.Run（已在线程池线程）
        ///   · 推荐同步构建 + 一次 AddRange（锁从 O(n) 降为 O(1)）
        ///   · 返回 Task.CompletedTask 走同步路径，零调度开销
        ///   · ParseAsync 返回时所有批次一定已处理完毕
        /// </summary>
        public Func<BatchSnapshot, Task>? OnBatch { get; set; }

        /// <summary>进度回调 [0.0, 1.0]（解析线程触发，频率由 ProgressIntervalBytes 控制）</summary>
        public Action<double>? OnProgress { get; set; }

        public DxfParser(string path, DxfParseOptions? opts = null)
        {
            _path = path;
            _opts = opts ?? new DxfParseOptions();
        }

        // ================================================================
        // ParseAsync：启动解析（生产者）+ 消费循环（消费者），等两者完成后返回
        //
        // 时序：
        //   解析线程: [P1][P2][P3]...[Complete]
        //   消费线程:   [C1]  [C2]  [C3]...[drain]
        //   ParseAsync:                        ↑ 返回
        // ================================================================
        public async Task<ParseSummary> ParseAsync(CancellationToken ct = default)
        {
            var summary = new ParseSummary();
            var sw = Stopwatch.StartNew();

            var channel = Channel.CreateBounded<BatchSnapshot>(new BoundedChannelOptions(_opts.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = true,
            });

            // 生产者（线程池线程）
            var producerTask = Task.Run(async () =>
            {
                try { await ProduceAsync(channel.Writer, summary, ct); }
                catch (Exception ex) 
                { 
                }
                finally { channel.Writer.Complete(); }
            }, ct);

            // 消费者（ParseAsync 调用处的上下文）
            if (OnBatch != null)
            {
                await foreach (var snap in channel.Reader.ReadAllAsync(ct))
                {
                    try { await OnBatch(snap); }
                    finally { snap.Entities.Clear(); }
                }
            }
            else
            {
                await foreach (var _ in channel.Reader.ReadAllAsync(ct)) { }
            }

            await producerTask;
            summary.TotalMs = sw.Elapsed.TotalMilliseconds;
            OnProgress?.Invoke(1.0);
            return summary;
        }

        // ================================================================
        // ProduceAsync：解析主循环
        //
        // 支持实体类型：
        //   LINE、ARC、CIRCLE、POINT
        //   LWPOLYLINE（新式，顶点内嵌）
        //   POLYLINE + VERTEX + SEQEND（老式，顶点独立）
        //   INSERT（展开 BLOCK，含嵌套）
        // ================================================================
        private async Task ProduceAsync(
            ChannelWriter<BatchSnapshot> writer,
            ParseSummary summary,
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var blocks = new Dictionary<string, BlockDef>(64, StringComparer.Ordinal);
            var batchList = BatchSnapshot.RentList();

            using var gr = new GroupReader(_path);
            long totalBytes = gr.StreamLength;
            long nextReport = _opts.ProgressIntervalBytes;

            var phase = Phase.None;
            string curSection = "";
            string curBlockName = "";

            // 当前实体积累
            DxfLine? curLine = null;
            DxfArc? curArc = null;
            DxfCircle? curCircle = null;
            DxfEllipse? curEllipse = null;   // 添加这行
            DxfPoint? curPoint = null;
            DxfLwPolyline? curLwp = null;   // 用于 LWPOLYLINE 和 老式 POLYLINE
            DxfText? curText = null;
            DxfMText? curMText = null;
            DxfSpline? curSpline = null;
            DxfHatch? curHatch = null;
            var ins = new InsertAcc();

            // 老式 POLYLINE 状态
            // POLYLINE 的顶点是独立的 VERTEX 实体，用 SEQEND 标记结束
            // curLwp 在 POLYLINE 阶段也用于积累顶点（复用 LwVertex 结构）
            bool inOldPolyline = false;   // 正在收集 VERTEX 序列

            while (gr.TryRead(out int code, out string val))
            {
                ct.ThrowIfCancellationRequested();

                // 进度上报
                if (OnProgress != null && totalBytes > 0)
                {
                    long pos = gr.StreamPosition;
                    if (pos >= nextReport)
                    {
                        OnProgress(Math.Min(0.99, (double)pos / totalBytes));
                        nextReport = pos + _opts.ProgressIntervalBytes;
                    }
                }

                if (code == 0)
                {
                    // 注意：此处必须用 string（不能用 ReadOnlySpan<char>），
                    // 因为 tag 的生命周期跨越了下方的 await writer.WriteAsync。
                    // string.Trim() 在无空格时直接返回原引用，几乎无额外开销。
                    string tag = val.Trim();

                    // SEQEND：老式 POLYLINE 的顶点序列结束，提交整条 POLYLINE
                    if (tag == "SEQEND")
                    {
                        if (inOldPolyline && curLwp != null)
                        {
                            CommitLwp(curLwp, curBlockName, blocks, batchList, ref summary);
                            curLwp = null;
                            inOldPolyline = false;
                        }
                        phase = Phase.None;
                        continue;
                    }

                    // VERTEX：老式 POLYLINE 的一个顶点
                    if (tag == "VERTEX" && inOldPolyline)
                    {
                        // 向 curLwp 追加新顶点，Y/Bulge 在后续属性中填入
                        curLwp!.Verts.Add(new LwVertex());
                        phase = Phase.Vertex;
                        continue;
                    }

                    // 提交除 POLYLINE/VERTEX/SEQEND 外的当前实体
                    CommitCurrent(ref phase, ref curLine, ref curArc, ref curCircle, ref curEllipse,
                        ref curPoint, ref curLwp, ref curText, ref curMText, ref curSpline, ref curHatch, ref ins,
                        curBlockName, blocks, batchList, ref summary,
                        ref inOldPolyline);

                    // 批次写入 Channel（await 在此，tag 不能是 Span）
                    if (batchList.Count >= _opts.BatchSize)
                    {
                        var snap = new BatchSnapshot { Entities = batchList };
                        batchList = BatchSnapshot.RentList();
                        await writer.WriteAsync(snap, ct);
                    }

                    if (tag == "EOF") break;
                    if (tag == "SECTION") { phase = Phase.None; continue; }
                    if (tag == "ENDSEC") { curSection = ""; curBlockName = ""; phase = Phase.None; continue; }
                    if (tag == "BLOCK") { phase = Phase.BlockHdr; continue; }
                    if (tag == "ENDBLK") { curBlockName = ""; phase = Phase.None; continue; }

                    // 实体识别（ENTITIES 段 或 BLOCK 内）
                    if (curSection == "ENTITIES" || curBlockName != "")
                    {
                        if (tag == "LINE") { curLine = new DxfLine(); phase = Phase.Line; }
                        else if (tag == "ARC") { curArc = new DxfArc(); phase = Phase.Arc; }
                        else if (tag == "CIRCLE") { curCircle = new DxfCircle(); phase = Phase.Circle; }
                        else if (tag == "ELLIPSE") { curEllipse = new DxfEllipse(); phase = Phase.Ellipse; }
                        else if (tag == "TEXT") { curText = new DxfText(); phase = Phase.Text; }
                        else if (tag == "MTEXT") { curMText = new DxfMText(); phase = Phase.MText; }
                        else if (tag == "POINT") { curPoint = new DxfPoint(); phase = Phase.Point; }
                        else if (tag == "SPLINE") { curSpline = new DxfSpline(); phase = Phase.Spline; }
                        else if (tag == "HATCH") { curHatch = new DxfHatch(); phase = Phase.Hatch; }
                        else if (tag == "LWPOLYLINE") { curLwp = new DxfLwPolyline(); phase = Phase.Lwp; inOldPolyline = false; }
                        else if (tag == "INSERT") { ins.Reset(); phase = Phase.Insert; }
                        else if (tag == "POLYLINE")
                        {
                            // 老式 POLYLINE：先收集 POLYLINE 头属性，顶点在后续 VERTEX 中
                            curLwp = new DxfLwPolyline();
                            inOldPolyline = true;
                            phase = Phase.OldPoly;
                        }
                        else phase = Phase.Skip;
                                    
                    }
                    continue;
                }

                // ── 属性分发 ─────────────────────────────────────────────
                switch (phase)
                {
                    case Phase.None:
                        if (code == 2 && curSection == "")
                            curSection = val.Trim();
                        break;

                    case Phase.BlockHdr:
                        if (code == 2 && curBlockName == "")
                        {
                            curBlockName = val.Trim();
                            if (!blocks.ContainsKey(curBlockName))
                                blocks[curBlockName] = new BlockDef { Name = curBlockName };
                        }
                        else if (code == 10 && curBlockName != "")
                            blocks[curBlockName].BaseX = GroupReader.ToDouble(val);
                        else if (code == 20 && curBlockName != "")
                            blocks[curBlockName].BaseY = GroupReader.ToDouble(val);
                        else if (code == 1)
                            phase = Phase.None;
                        break;

                    case Phase.Line:
                        switch (code)
                        {
                            case 8: curLine!.Layer = val.Trim(); break;
                            case 5: curLine!.Handle = val.Trim(); break;
                            case 10: curLine!.X1 = GroupReader.ToDouble(val); break;
                            case 20: curLine!.Y1 = GroupReader.ToDouble(val); break;
                            case 11: curLine!.X2 = GroupReader.ToDouble(val); break;
                            case 21: curLine!.Y2 = GroupReader.ToDouble(val); break;
                        }
                        break;

                    case Phase.Arc:
                        switch (code)
                        {
                            case 8: curArc!.Layer = val.Trim(); break;
                            case 5: curArc!.Handle = val.Trim(); break;
                            case 10: curArc!.Cx = GroupReader.ToDouble(val); break;
                            case 20: curArc!.Cy = GroupReader.ToDouble(val); break;
                            case 40: curArc!.R = GroupReader.ToDouble(val); break;
                            case 50: curArc!.StartAngle = GroupReader.ToDouble(val); break;
                            case 51: curArc!.EndAngle = GroupReader.ToDouble(val); break;
                            case -3:
                                // 扩展数据开始，下一个组码应为 1001
                                curArc!.InXData = true;
                                break;
                            case 1001:
                                if (curArc!.InXData)
                                {
                                    curArc.XDataApp = val.Trim();
                                }
                                break;
                            case 1040:
                                if (curArc!.InXData && curArc.XDataApp != null)
                                {
                                    curArc.XDataDoubles.Add(GroupReader.ToDouble(val));
                                }
                                break;
                        }
                        break;

                    case Phase.Circle:
                        switch (code)
                        {
                            case 8: curCircle!.Layer = val.Trim(); break;
                            case 5: curCircle!.Handle = val.Trim(); break;
                            case 10: curCircle!.Cx = GroupReader.ToDouble(val); break;
                            case 20: curCircle!.Cy = GroupReader.ToDouble(val); break;
                            case 40: curCircle!.R = GroupReader.ToDouble(val); break;
                        }
                        break;
                    case Phase.Ellipse:
                        switch (code)
                        {
                            case 8: curEllipse!.Layer = val.Trim(); break;
                            case 5: curEllipse!.Handle = val.Trim(); break;
                            case 10: curEllipse!.Cx = GroupReader.ToDouble(val); break;
                            case 20: curEllipse!.Cy = GroupReader.ToDouble(val); break;
                            case 11: curEllipse!.MajorAxisX = GroupReader.ToDouble(val); break;
                            case 21: curEllipse!.MajorAxisY = GroupReader.ToDouble(val); break;
                            case 40: curEllipse!.Ratio = GroupReader.ToDouble(val); break;
                            case 41: curEllipse!.StartParam = GroupReader.ToDouble(val); break;
                            case 42: curEllipse!.EndParam = GroupReader.ToDouble(val); break;
                        }
                        break;
                    case Phase.Point:
                        switch (code)
                        {
                            case 8: curPoint!.Layer = val.Trim(); break;
                            case 5: curPoint!.Handle = val.Trim(); break;
                            case 10: curPoint!.X = GroupReader.ToDouble(val); break;
                            case 20: curPoint!.Y = GroupReader.ToDouble(val); break;
                        }
                        break;

                    // LWPOLYLINE（新式）
                    case Phase.Lwp:
                        switch (code)
                        {
                            case 8: curLwp!.Layer = val.Trim(); break;
                            case 5: curLwp!.Handle = val.Trim(); break;
                            case 70: curLwp!.Closed = (GroupReader.ToInt(val) & 1) != 0; break;
                            case 43: curLwp!.Width = GroupReader.ToDouble(val); break;
                            case 10:
                                curLwp!.Verts.Add(new LwVertex { X = GroupReader.ToDouble(val) });
                                break;
                            case 20:
                                if (curLwp!.Verts.Count > 0)
                                {
                                    var v20 = curLwp.Verts[curLwp.Verts.Count - 1];
                                    v20.Y = GroupReader.ToDouble(val);
                                    curLwp.Verts[curLwp.Verts.Count - 1] = v20;
                                }
                                break;
                            case 42:
                                if (curLwp!.Verts.Count > 0)
                                {
                                    var v42 = curLwp.Verts[curLwp.Verts.Count - 1];
                                    v42.Bulge = GroupReader.ToDouble(val);
                                    curLwp.Verts[curLwp.Verts.Count - 1] = v42;
                                }
                                break;
                            case -3:
                                // 扩展数据开始，下一个组码应为 1001
                                curLwp!.InXData = true;
                                break;
                            case 1001:
                                if (curLwp!.InXData)
                                {
                                    curLwp.XDataApp = val.Trim();
                                }
                                break;
                            case 1040:
                                if (curLwp!.InXData && curLwp.XDataApp != null)
                                {
                                    curLwp.XDataDoubles.Add(GroupReader.ToDouble(val));
                                }
                                break;
                        }
                        break;

                    // 老式 POLYLINE 头属性（code=10/20 是哑坐标，忽略）
                    case Phase.OldPoly:
                        switch (code)
                        {
                            case 8: curLwp!.Layer = val.Trim(); break;
                            case 5: curLwp!.Handle = val.Trim(); break;
                            case 70: curLwp!.Closed = (GroupReader.ToInt(val) & 1) != 0; break;
                            case 40: curLwp!.Width = GroupReader.ToDouble(val); break; // 默认起始宽度
                                                                                       // code=10/20/30 是 POLYLINE 实体坐标（哑值），忽略
                                                                                       // code=66=1 表示有顶点列表，已隐式处理
                        }
                        break;

                    // 老式 POLYLINE 的 VERTEX 属性
                    case Phase.Vertex:
                        if (curLwp!.Verts.Count > 0)
                        {
                            int lastIdx = curLwp.Verts.Count - 1;
                            var vVert = curLwp.Verts[lastIdx];
                            switch (code)
                            {
                                case 8:
                                    // VERTEX 的 layer（通常与 POLYLINE 同，可忽略）
                                    break;
                                case 10: vVert.X = GroupReader.ToDouble(val); break;
                                case 20: vVert.Y = GroupReader.ToDouble(val); break;
                                case 42: vVert.Bulge = GroupReader.ToDouble(val); break;
                            }
                            curLwp.Verts[lastIdx] = vVert;
                        }
                        break;

                    case Phase.Insert:
                        switch (code)
                        {
                            case 2: ins.BlockName = val.Trim(); break;
                            case 8: ins.Layer = val.Trim(); break;
                            case 5: ins.Handle = val.Trim(); break;
                            case 10: ins.X = GroupReader.ToDouble(val); break;
                            case 20: ins.Y = GroupReader.ToDouble(val); break;
                            case 41: ins.SX = GroupReader.ToDouble(val); break;
                            case 42: ins.SY = GroupReader.ToDouble(val); break;
                            case 50: ins.Rot = GroupReader.ToDouble(val); break;
                            case 70: ins.Cols = GroupReader.ToInt(val); break;
                            case 71: ins.Rows = GroupReader.ToInt(val); break;
                            case 44: ins.ColSp = GroupReader.ToDouble(val); break;
                            case 45: ins.RowSp = GroupReader.ToDouble(val); break;
                        }
                        break;
                    case Phase.Text:
                        switch (code)
                        {
                            case 8: curText!.Layer = val.Trim(); break;
                            case 5: curText!.Handle = val.Trim(); break;
                            case 10: curText!.X = GroupReader.ToDouble(val); break;
                            case 20: curText!.Y = GroupReader.ToDouble(val); break;
                            case 40: curText!.Height = GroupReader.ToDouble(val); break;
                            case 1: curText!.Text = UnescapeDxfText(val); break;
                            case 50: curText!.Rotation = GroupReader.ToDouble(val); break;
                            case 51: curText!.Obliquing = GroupReader.ToDouble(val); break;
                            case 7: curText!.FontName = DxfTextFontNameExtensions.ParseFontName(val); break;
                            case 72: curText!.HAlign = GroupReader.ToInt(val); break;
                            case 73: curText!.VAlign = GroupReader.ToInt(val); break;
                        }
                        break;

                    case Phase.MText:
                        switch (code)
                        {
                            case 8: curMText!.Layer = val.Trim(); break;
                            case 5: curMText!.Handle = val.Trim(); break;
                            case 10: curMText!.X = GroupReader.ToDouble(val); break;
                            case 20: curMText!.Y = GroupReader.ToDouble(val); break;
                            case 40: curMText!.Height = GroupReader.ToDouble(val); break;
                            case 3: curMText!.Text += UnescapeDxfText(val); break;  // continuation lines
                            case 1: curMText!.Text += UnescapeDxfText(val); break;  // last line
                            case 50: curMText!.Rotation = GroupReader.ToDouble(val); break;
                            case 7: curMText!.FontName = DxfTextFontNameExtensions.ParseFontName(val); break;
                            case 71: curMText!.AttachmentPoint = GroupReader.ToInt(val); break;
                        }
                        break;

                    case Phase.Spline:
                        switch (code)
                        {
                            case 8: curSpline!.Layer = val.Trim(); break;
                            case 5: curSpline!.Handle = val.Trim(); break;
                            case 70: curSpline!.Closed = (GroupReader.ToInt(val) & 1) != 0; break;
                            case 71: curSpline!.Degree = GroupReader.ToInt(val); break;
                            case 11:
                                curSpline!.FitPoints.Add((GroupReader.ToDouble(val), 0));
                                break;
                            case 21:
                                if (curSpline!.FitPoints.Count > 0)
                                {
                                    var last = curSpline.FitPoints[curSpline.FitPoints.Count - 1];
                                    curSpline.FitPoints[curSpline.FitPoints.Count - 1] = (last.X, GroupReader.ToDouble(val));
                                }
                                break;
                        }
                        break;

                    // HATCH 填充实体：通过 XDATA 读取填充线段和 HatchParam 参数
                    // 导出时写为 HATCH 实体 + XDATA("HATCH_PARAM")
                    case Phase.Hatch:
                        switch (code)
                        {
                            case 8: curHatch!.Layer = val.Trim(); break;
                            case 5: curHatch!.Handle = val.Trim(); break;
                            case 10: curMText!.X = GroupReader.ToDouble(val); break;
                            case 20: curMText!.Y = GroupReader.ToDouble(val); break;
                            case -3:
                                curHatch!.InXData = true;
                                break;
                            case 1001:
                                if (curHatch!.InXData)
                                {
                                    curHatch.XDataApp = val.Trim();
                                }
                                break;
                            case 1040:
                                if (curHatch!.InXData && curHatch.XDataApp != null)
                                {
                                    curHatch.XDataDoubles.Add(GroupReader.ToDouble(val));
                                }
                                break;
                        }
                        break;
                }
            }

            // 末尾实体
            CommitCurrent(ref phase, ref curLine, ref curArc, ref curCircle, ref curEllipse,
                ref curPoint, ref curLwp, ref curText, ref curMText, ref curSpline, ref curHatch, ref ins,
                curBlockName, blocks, batchList, ref summary, ref inOldPolyline);

            if (batchList.Count > 0)
            {
                var snap = new BatchSnapshot { Entities = batchList };
                await writer.WriteAsync(snap, ct);
            }

            summary.ParseMs = sw.Elapsed.TotalMilliseconds;
        }

        // ================================================================
        // CommitCurrent：遇到 code=0（非 VERTEX/SEQEND）时提交当前实体
        // ================================================================
        private void CommitCurrent(
            ref Phase phase,
            ref DxfLine? line, 
            ref DxfArc? arc, 
            ref DxfCircle? circle,
            ref DxfEllipse? ellipse,
            ref DxfPoint? point, 
            ref DxfLwPolyline? lwp,
            ref DxfText? text,
            ref DxfMText? mtext,
            ref DxfSpline? spline,
            ref DxfHatch? hatch,
            ref InsertAcc ins,
            string curBlockName,
            Dictionary<string, BlockDef> blocks,
            List<DxfEntity> batchList,
            ref ParseSummary summary,
            ref bool inOldPolyline)
        {
            bool inBlock = curBlockName != "";

            switch (phase)
            {
                case Phase.Line:
                    if (line != null)
                    {
                        if (inBlock) blocks[curBlockName].Entities.Add(line);
                        else { batchList.Add(line); summary.Lines++; }
                        line = null;
                    }
                    break;

                case Phase.Arc:
                    if (arc != null)
                    {
                        if (inBlock) blocks[curBlockName].Entities.Add(arc);
                        else { batchList.Add(arc); summary.Arcs++; }
                        arc = null;
                    }
                    break;

                case Phase.Circle:
                    if (circle != null)
                    {
                        if (inBlock) blocks[curBlockName].Entities.Add(circle);
                        else { batchList.Add(circle); summary.Circles++; }
                        circle = null;
                    }
                    break;
                case Phase.Ellipse:
                    if (ellipse != null)
                    {
                        if (inBlock) blocks[curBlockName].Entities.Add(ellipse);
                        else { batchList.Add(ellipse); summary.Ellipses++; }
                        ellipse = null;
                    }
                    break;


                case Phase.Point:
                    if (point != null)
                    {
                        if (inBlock) blocks[curBlockName].Entities.Add(point);
                        else { batchList.Add(point); summary.Points++; }
                        point = null;
                    }
                    break;

                case Phase.Lwp:
                    // 新式 LWPOLYLINE
                    if (lwp != null)
                    {
                        CommitLwp(lwp, curBlockName, blocks, batchList, ref summary);
                        lwp = null;
                        inOldPolyline = false;
                    }
                    break;

                case Phase.OldPoly:
                    // 老式 POLYLINE：头已读完，等待 VERTEX
                    // 本身不提交，等 SEQEND 时通过 CommitLwp 提交
                    // 如果遇到非 VERTEX/SEQEND 的 code=0（不规范文件），丢弃
                    if (!inOldPolyline)
                    {
                        lwp = null;
                    }
                    break;

                case Phase.Text:
                    if (text != null)
                    {
                        if (inBlock) blocks[curBlockName].Entities.Add(text);
                        else { batchList.Add(text); summary.Texts++; }
                        text = null;
                    }
                    break;

                case Phase.MText:
                    if (mtext != null)
                    {
                        // 在 strip 格式化之前，先提取 {\Q...;} 倾斜角度
                        mtext.Obliquing = ExtractObliquing(mtext.Text);
                        // 检测下划线格式化代码 {\L 或 \L
                        mtext.IsUnderline = HasUnderline(mtext.Text);
                        // Strip MTEXT formatting codes (e.g. {\fArial|...;text}) and convert \P to newline
                        mtext.Text = StripMTextFormatting(mtext.Text);
                        if (inBlock) blocks[curBlockName].Entities.Add(mtext);
                        else { batchList.Add(mtext); summary.Texts++; }
                        mtext = null;
                    }
                    break;

                case Phase.Spline:
                    if (spline != null)
                    {
                        if (inBlock) blocks[curBlockName].Entities.Add(spline);
                        else { batchList.Add(spline); summary.Splines++; }
                        spline = null;
                    }
                    break;

                case Phase.Hatch:
                    if (hatch != null)
                    {
                        if (inBlock) blocks[curBlockName].Entities.Add(hatch);
                        else { batchList.Add(hatch); summary.Hatches++; }
                        hatch = null;
                    }
                    break;

                case Phase.Insert:
                    if (ins.BlockName != "")
                    {
                        if (inBlock)
                        {
                            blocks[curBlockName].Entities.Add(new BlockDefInsert
                            {
                                Layer = ins.Layer,
                                Handle = ins.Handle,
                                BlockName = ins.BlockName,
                                X = ins.X,
                                Y = ins.Y,
                                SX = ins.SX,
                                SY = ins.SY,
                                Rot = ins.Rot,
                                Cols = ins.Cols,
                                Rows = ins.Rows,
                                ColSp = ins.ColSp,
                                RowSp = ins.RowSp,
                            });
                        }
                        else
                        {
                            CommitInsert(ref ins, blocks, batchList, ref summary, 0);
                        }
                    }
                    ins.Reset();
                    break;
             
            }
        }

        // ================================================================
        // CommitLwp：提交一条 LwPolyline（新式或老式均调用此方法）
        // ================================================================
        private void CommitLwp(
            DxfLwPolyline lwp,
            string curBlockName,
            Dictionary<string, BlockDef> blocks,
            List<DxfEntity> batchList,
            ref ParseSummary summary)
        {
            bool inBlock = curBlockName != "";

            // 尝试转换为矩形（仅识别本软件导出的 DR_RECT XDATA 标记，不影响外部 DXF）
            if (TryGetRectangleFromLwp(lwp, out DxfRectangle dxfRectangle))
            {
                if (inBlock)
                {
                    blocks[curBlockName].Entities.Add(dxfRectangle);
                }
                else
                {
                    batchList.Add(dxfRectangle);
                    summary.Polys++;
                }
                return;
            }

            if (inBlock)
            {
                // BLOCK 内：存入 BlockDef，等展开时处理
                blocks[curBlockName].Entities.Add(lwp);
            }
            else if (_opts.ExplodeLwPolyline)
            {
                ExplodeLwp(lwp, batchList, ref summary);
            }
            else
            {
                batchList.Add(lwp);
                summary.Polys++;
            }
        }

        // ================================================================
        // CommitInsert：递归展开 INSERT（支持嵌套、阵列）
        // ================================================================
        private void CommitInsert(
            ref InsertAcc ins,
            Dictionary<string, BlockDef> blocks,
            List<DxfEntity> batchList,
            ref ParseSummary summary,
            int depth)
        {
            if (depth > _opts.MaxExpandDepth) return;
            if (!blocks.TryGetValue(ins.BlockName, out var blk)) return;

            double rad = ins.Rot * (Math.PI / 180.0);
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            int cols = Math.Max(1, ins.Cols), rows = Math.Max(1, ins.Rows);

            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    double ix = ins.X + col * ins.ColSp;
                    double iy = ins.Y + row * ins.RowSp;

                    foreach (var ent in blk.Entities)
                    {
                        switch (ent)
                        {
                            case DxfLine src:
                                {
                                    var (x1, y1) = Xform(src.X1, src.Y1, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                    var (x2, y2) = Xform(src.X2, src.Y2, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                    batchList.Add(new DxfLine { Layer = Or(src.Layer, ins.Layer), Handle = src.Handle, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
                                    summary.Lines++; summary.Expanded++; break;
                                }
                            case DxfArc src:
                                {
                                    var (cx, cy) = Xform(src.Cx, src.Cy, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                    double r = src.R * Math.Max(Math.Abs(ins.SX), Math.Abs(ins.SY));
                                    batchList.Add(new DxfArc { Layer = Or(src.Layer, ins.Layer), Handle = src.Handle, Cx = cx, Cy = cy, R = r, StartAngle = Norm360(src.StartAngle + ins.Rot), EndAngle = Norm360(src.EndAngle + ins.Rot) });
                                    summary.Arcs++; summary.Expanded++; break;
                                }
                            case DxfCircle src:
                                {
                                    var (cx, cy) = Xform(src.Cx, src.Cy, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                    double r = src.R * Math.Max(Math.Abs(ins.SX), Math.Abs(ins.SY));
                                    batchList.Add(new DxfCircle { Layer = Or(src.Layer, ins.Layer), Handle = src.Handle, Cx = cx, Cy = cy, R = r });
                                    summary.Circles++; summary.Expanded++; break;
                                }
                            case DxfEllipse src:
                                {
                                    var (cx, cy) = Xform(src.Cx, src.Cy, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                    // 主轴向量需要变换（方向旋转 + 缩放）
                                    double majorLen = Math.Sqrt(src.MajorAxisX * src.MajorAxisX + src.MajorAxisY * src.MajorAxisY);
                                    double majorAngle = Math.Atan2(src.MajorAxisY, src.MajorAxisX);
                                    double newMajorLen = majorLen * Math.Max(Math.Abs(ins.SX), Math.Abs(ins.SY));
                                    double newMajorAngle = majorAngle + ins.Rot * (Math.PI / 180.0);
                                    double newMajorX = newMajorLen * Math.Cos(newMajorAngle);
                                    double newMajorY = newMajorLen * Math.Sin(newMajorAngle);

                                    batchList.Add(new DxfEllipse
                                    {
                                        Layer = Or(src.Layer, ins.Layer),
                                        Handle = src.Handle,
                                        Cx = cx,
                                        Cy = cy,
                                        MajorAxisX = newMajorX,
                                        MajorAxisY = newMajorY,
                                        Ratio = src.Ratio,
                                        StartParam = src.StartParam,
                                        EndParam = src.EndParam
                                    });
                                    summary.Ellipses++;
                                    summary.Expanded++;
                                    break;
                                }
                            case DxfPoint src:
                                {
                                    var (px, py) = Xform(src.X, src.Y, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                    batchList.Add(new DxfPoint { Layer = Or(src.Layer, ins.Layer), Handle = src.Handle, X = px, Y = py });
                                    summary.Points++; summary.Expanded++; break;
                                }
                            case DxfLwPolyline src:
                                {
                                    // 变换顶点后再拆分
                                    var xf = new DxfLwPolyline { Layer = Or(src.Layer, ins.Layer), Handle = src.Handle, Closed = src.Closed, Width = src.Width * Math.Max(Math.Abs(ins.SX), Math.Abs(ins.SY)) };
                                    foreach (var v in src.Verts)
                                    {
                                        var (vx, vy) = Xform(v.X, v.Y, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                        xf.Verts.Add(new LwVertex { X = vx, Y = vy, Bulge = v.Bulge });
                                    }
                                    if (_opts.ExplodeLwPolyline) ExplodeLwp(xf, batchList, ref summary);
                                    else { batchList.Add(xf); summary.Polys++; }
                                    summary.Expanded++; break;
                                }
                            case DxfText src:
                                {
                                    var (px, py) = Xform(src.X, src.Y, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                    batchList.Add(new DxfText
                                    {
                                        Layer = Or(src.Layer, ins.Layer),
                                        Handle = src.Handle,
                                        X = px,
                                        Y = py,
                                        Height = src.Height * Math.Max(Math.Abs(ins.SX), Math.Abs(ins.SY)),
                                        Text = src.Text,
                                        Rotation = Norm360(src.Rotation + ins.Rot),
                                        FontName = src.FontName,
                                        HAlign = src.HAlign,
                                        VAlign = src.VAlign
                                    });
                                    summary.Texts++;
                                    summary.Expanded++;
                                    break;
                                }
                            case DxfMText src:
                                {
                                    var (px, py) = Xform(src.X, src.Y, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                    batchList.Add(new DxfMText
                                    {
                                        Layer = Or(src.Layer, ins.Layer),
                                        Handle = src.Handle,
                                        X = px,
                                        Y = py,
                                        Height = src.Height * Math.Max(Math.Abs(ins.SX), Math.Abs(ins.SY)),
                                        Text = src.Text,
                                        Rotation = Norm360(src.Rotation + ins.Rot),
                                        FontName = src.FontName,
                                        AttachmentPoint = src.AttachmentPoint
                                    });
                                    summary.Texts++;
                                    summary.Expanded++;
                                    break;
                                }
                            case DxfRectangle src:
                                {
                                    // INSERT 展开矩形：尖角点随块变换，圆角/倒角按块缩放均值折算，
                                    // 旋转角叠加 INSERT 的旋转角
                                    var pts = new List<(double X, double Y)>(src.Points.Count);
                                    foreach (var p in src.Points)
                                    {
                                        var (px, py) = Xform(p.X, p.Y, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                        pts.Add((px, py));
                                    }
                                    double cornerScale = (Math.Abs(ins.SX) + Math.Abs(ins.SY)) / 2.0;
                                    List<double>? concor = null;
                                    if (src.Concor != null)
                                    {
                                        concor = new List<double>(src.Concor.Count);
                                        foreach (var r in src.Concor) concor.Add(r * cornerScale);
                                    }
                                    List<double>? chamfer = null;
                                    if (src.Chamfer != null)
                                    {
                                        chamfer = new List<double>(src.Chamfer.Count);
                                        foreach (var r in src.Chamfer) chamfer.Add(r * cornerScale);
                                    }
                                    batchList.Add(new DxfRectangle
                                    {
                                        Layer = Or(src.Layer, ins.Layer),
                                        Handle = src.Handle,
                                        Points = pts,
                                        Rotation = Norm360(src.Rotation + ins.Rot),
                                        Concor = concor,
                                        Chamfer = chamfer
                                    });
                                    summary.Rectangles++;
                                    summary.Expanded++;
                                    break;
                                }
                            case BlockDefInsert nested:
                                {
                                    var (tx, ty) = Xform(nested.X, nested.Y, blk, ix, iy, ins.SX, ins.SY, cos, sin);
                                    var ni = new InsertAcc { BlockName = nested.BlockName, Layer = Or(nested.Layer, ins.Layer), Handle = nested.Handle, X = tx, Y = ty, SX = nested.SX * ins.SX, SY = nested.SY * ins.SY, Rot = Norm360(nested.Rot + ins.Rot), Cols = nested.Cols, Rows = nested.Rows, ColSp = nested.ColSp, RowSp = nested.RowSp };
                                    CommitInsert(ref ni, blocks, batchList, ref summary, depth + 1);
                                    break;
                                }
                        }
                    }
                }
        }
        private static bool TryGetRectangleFromLwp(DxfLwPolyline lwp, out double x1, out double y1, out double x2, out double y2)
        {
                x1 = y1 = x2 = y2 = 0;
                var verts = lwp.Verts;
                if (!lwp.Closed || verts.Count != 4) return false;

                // 提取四个顶点（按顺序）
                var p = new (double x, double y)[4];
                for (int i = 0; i < 4; i++)
                {
                    p[i].x = verts[i].X;
                    p[i].y = verts[i].Y;
                }

                // 检查相邻边是否垂直（点积 ≈ 0）
                for (int i = 0; i < 4; i++)
                {
                    var v1 = (p[(i + 1) % 4].x - p[i].x, p[(i + 1) % 4].y - p[i].y);
                    var v2 = (p[(i + 2) % 4].x - p[(i + 1) % 4].x, p[(i + 2) % 4].y - p[(i + 1) % 4].y);
                    double dot = v1.Item1 * v2.Item1 + v1.Item2 * v2.Item2;
                    if (Math.Abs(dot) > 1e-8) return false;
                }

                // 计算中心点
                double cx = (p[0].x + p[1].x + p[2].x + p[3].x) / 4.0;
                double cy = (p[0].y + p[1].y + p[2].y + p[3].y) / 4.0;

                // 计算第一条边的边长（宽度）和第二条边的边长（高度）
                double dx = p[1].x - p[0].x, dy = p[1].y - p[0].y;
                double width = Math.Sqrt(dx * dx + dy * dy);
                double dx2 = p[2].x - p[1].x, dy2 = p[2].y - p[1].y;
                double height = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

                // 局部坐标系的对角点（中心为原点，未旋转状态）
                // Y1=maxY（上），Y2=minY（下），与 DrawRectangle.UpdateSetProperty 约定一致
                x1 = cx - width / 2.0;
                y1 = cy + height / 2.0;
                x2 = cx + width / 2.0;
                y2 = cy - height / 2.0;
                return true;
        }


        /// <summary>
        /// 识别本软件导出的圆角/倒角矩形：仅当 LWPOLYLINE 带 DR_RECT XDATA 时命中。
        /// XDATA 布局（16 个 1040）：4 个尖角世界点(x,y 共 8) + 4 个圆角半径 + 4 个倒角长度。
        /// 顶点坐标不取自 Verts（圆角矩形的轮廓顶点数不固定），而取自 XDATA 中的尖角点。
        /// 外部软件生成的普通矩形不带该标记，维持多段线导入路径不变。
        /// </summary>
        private static bool TryGetRectangleFromLwp(DxfLwPolyline lwp, out DxfRectangle dxfRectangle)
        {
            dxfRectangle = null!;

            if (!lwp.Closed) return false;
            if (lwp.XDataApp != "DR_RECT" || lwp.XDataDoubles.Count < 16) return false;

            var d = lwp.XDataDoubles;
            dxfRectangle = new DxfRectangle
            {
                Layer = lwp.Layer,
                Handle = lwp.Handle,
                Points = new List<(double X, double Y)>
                {
                    (d[0], d[1]),
                    (d[2], d[3]),
                    (d[4], d[5]),
                    (d[6], d[7])
                },
                Concor = new List<double> { d[8], d[9], d[10], d[11] },
                Chamfer = new List<double> { d[12], d[13], d[14], d[15] },
            };

            return true;
        }

        // ================================================================
        // ExplodeLwp：LWPOLYLINE（新式或老式）→ Line/Arc 片段
        // ================================================================
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExplodeLwp(DxfLwPolyline lwp, List<DxfEntity> list, ref ParseSummary summary)
        {
            var verts = lwp.Verts;
            int n = verts.Count;
            if (n < 2) return;
            int segCount = lwp.Closed ? n : n - 1;

            for (int i = 0; i < segCount; i++)
            {
                ref var p1 = ref CollectionsMarshal.AsSpan(verts)[i];
                ref var p2 = ref CollectionsMarshal.AsSpan(verts)[(i + 1) % n];

                if (Math.Abs(p1.Bulge) < 1e-12)
                {
                    if (p1.X == p2.X && p1.Y == p2.Y) continue;
                    list.Add(new DxfLine { Layer = lwp.Layer, Handle = lwp.Handle, X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y });
                    summary.Lines++;
                }
                else
                {
                    double dx = p2.X - p1.X, dy = p2.Y - p1.Y, chord2 = dx * dx + dy * dy;
                    if (chord2 < 1e-24) continue;
                    double chord = Math.Sqrt(chord2), ab = Math.Abs(p1.Bulge), b2 = p1.Bulge * p1.Bulge;
                    double r = chord * (1 + b2) / (4 * ab), dtc = chord * (1 - b2) / (4 * ab);
                    double px = -dy / chord, py = dx / chord, sg = p1.Bulge > 0 ? 1.0 : -1.0;
                    double cx = (p1.X + p2.X) * 0.5 + sg * dtc * px, cy = (p1.Y + p2.Y) * 0.5 + sg * dtc * py;
                    double sa = Norm360(Math.Atan2(p1.Y - cy, p1.X - cx) * (180 / Math.PI));
                    double ea = Norm360(Math.Atan2(p2.Y - cy, p2.X - cx) * (180 / Math.PI));
                    if (p1.Bulge < 0) (sa, ea) = (ea, sa);
                    list.Add(new DxfArc
                    {
                        Layer = lwp.Layer, Handle = lwp.Handle,
                        Cx = cx, Cy = cy, R = r, StartAngle = sa, EndAngle = ea,
                        ExactStartX = p1.X, ExactStartY = p1.Y,
                        ExactEndX = p2.X, ExactEndY = p2.Y
                    });
                    summary.Arcs++;
                }
            }
        }

        // ── 工具方法 ──────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (double x, double y) Xform(double lx, double ly, BlockDef blk, double ix, double iy, double sx, double sy, double cos, double sin)
        {
            double rx = (lx - blk.BaseX) * sx, ry = (ly - blk.BaseY) * sy;
            return (rx * cos - ry * sin + ix, rx * sin + ry * cos + iy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Norm360(double d) { d %= 360; return d < 0 ? d + 360 : d; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string Or(string a, string b) => a.Length > 0 ? a : b;

        /// <summary>
        /// 处理 DXF 文本中的特殊转义序列。
        /// %%d → °, %%p → ±, %%c → ⌀, %%% → %, \\U+XXXX → Unicode 字符
        /// </summary>
        private static string UnescapeDxfText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (i + 2 < text.Length && text[i] == '%' && text[i + 1] == '%')
                {
                    char c = text[i + 2];
                    switch (c)
                    {
                        case 'd': sb.Append('°'); i += 2; continue;
                        case 'p': sb.Append('±'); i += 2; continue;
                        case 'c': sb.Append('⌀'); i += 2; continue;
                        case '%': sb.Append('%'); i += 2; continue;
                    }
                }
                if (i + 6 < text.Length && text[i] == '\\' && text[i + 1] == 'U' && text[i + 2] == '+')
                {
                    var hex = text.AsSpan(i + 3, 4);
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int codePoint))
                    {
                        sb.Append(char.ConvertFromUtf32(codePoint));
                        i += 6;
                        continue;
                    }
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Strip MTEXT formatting codes and convert \P to newline.
        /// Common MTEXT formatting codes:
        ///   \P            → newline
        ///   \N            → newline (alternative)
        ///   {\fFont|...;} → font change (strip the whole block)
        ///   {\H...;}      → height change (strip)
        ///   {\C...;}      → color change (strip)
        ///   {\W...;}      → width change (strip)
        ///   {\T...;}      → tracking change (strip)
        ///   {\Q...;}      → oblique angle (strip)
        ///   {\S...;}      → stacking (strip)
        ///   {\A...;}      → alignment (strip)
        ///   {\L           → underline start (strip)
        ///   \l}           → underline end (strip)
        ///   {\O           → overline start (strip)
        ///   \o}           → overline end (strip)
        ///   {\K...;}      → strike-through (strip)
        ///   {...}         → brace grouping (strip formatting, keep content)
        ///   \\           → literal backslash
        ///   \{            → literal {
        ///   \}            → literal }
        /// </summary>
        private static string StripMTextFormatting(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var sb = new System.Text.StringBuilder(text.Length);
            int depth = 0;  // brace nesting depth
            int i = 0;
            int n = text.Length;

            while (i < n)
            {
                char c = text[i];

                // Handle backslash escape sequences
                if (c == '\\' && i + 1 < n)
                {
                    char next = text[i + 1];

                    // \P → newline (even inside braces)
                    if (next == 'P' || next == 'p')
                    {
                        sb.Append('\n');
                        i += 2;
                        continue;
                    }
                    // \N → newline
                    if (next == 'N' || next == 'n')
                    {
                        sb.Append('\n');
                        i += 2;
                        continue;
                    }
                    // \\ → literal backslash
                    if (next == '\\')
                    {
                        sb.Append('\\');
                        i += 2;
                        continue;
                    }
                    // \{ → literal {
                    if (next == '{')
                    {
                        sb.Append('{');
                        i += 2;
                        continue;
                    }
                    // \} → literal }
                    if (next == '}')
                    {
                        sb.Append('}');
                        i += 2;
                        continue;
                    }

                    // Inside braces: skip formatting codes like \fArial|b0|i0|c0|p0;
                    // Also skip \H...; \W...; \T...; \Q...; \A...; \S...; \C...;
                    if (depth > 0)
                    {
                        // Skip the entire formatting code until semicolon or end of brace group
                        i += 2; // skip backslash and command letter
                        while (i < n && text[i] != ';' && text[i] != '}' && text[i] != '{')
                            i++;
                        if (i < n && text[i] == ';') i++; // skip semicolon
                        continue;
                    }

                    // Outside braces: skip standalone formatting codes
                    i += 2; // skip backslash and command letter
                    while (i < n && text[i] != ';' && text[i] != '}' && text[i] != '{' && text[i] != '\\')
                        i++;
                    if (i < n && text[i] == ';') i++; // skip semicolon
                    continue;
                }

                // Handle braces
                if (c == '{')
                {
                    depth++;
                    i++;
                    continue;
                }
                if (c == '}')
                {
                    if (depth > 0) depth--;
                    i++;
                    continue;
                }

                // Tilde (non-breaking space in MTEXT, replace with space)
                if (c == '~' && depth > 0)
                {
                    sb.Append(' ');
                    i++;
                    continue;
                }

                // Carat (non-breaking hyphen in MTEXT, replace with hyphen)
                if (c == '^' && i + 1 < n && text[i + 1] == ' ')
                {
                    // Skip the carat, keep the space
                    i++;
                    continue;
                }

                // Regular character (outside braces, or inside braces as content)
                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        // ── 内部类型 ──────────────────────────────────────────────────
        /// <summary>
        /// 从 MTEXT 内容中提取 {\Qvalue;} 倾斜角度。
        /// 匹配模式：\Q 后跟数字（可含小数点和负号），以 ; 结尾。
        /// 返回提取到的角度值，未找到返回 0。
        /// </summary>
        private static double ExtractObliquing(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int idx = text.IndexOf("\\Q", StringComparison.Ordinal);
            if (idx < 0) return 0;
            int start = idx + 2;
            int end = text.IndexOf(';', start);
            if (end < 0) return 0;
            var span = text.AsSpan(start, end - start);
            if (double.TryParse(span, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double angle))
                return angle;
            return 0;
        }

        /// <summary>
        /// 检测 MTEXT 内容是否包含下划线格式化代码 {\L 或 \L。
        /// 返回 true 表示有下划线。
        /// </summary>
        private static bool HasUnderline(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // MTEXT 下划线格式：{\L 开始下划线，\l} 结束
            return text.IndexOf("{\\L", StringComparison.Ordinal) >= 0
                || text.IndexOf("\\L", StringComparison.Ordinal) >= 0;
        }

        private enum Phase : byte { None, Skip, BlockHdr, Line, Arc, Circle, Point, Lwp, OldPoly, Vertex, Insert, Ellipse, Text, MText, Spline, Hatch }

        private struct InsertAcc
        {
            public string BlockName, Layer, Handle;
            public double X, Y, SX, SY, Rot, ColSp, RowSp;
            public int Cols, Rows;
            public void Reset() { BlockName = ""; Layer = "0"; Handle = ""; X = Y = Rot = ColSp = RowSp = 0; SX = SY = 1; Cols = Rows = 1; }
        }
    }

    internal sealed class BlockDefInsert : DxfEntity
    {
        public string BlockName = "";
        public double X, Y, SX = 1, SY = 1, Rot, ColSp, RowSp;
        public int Cols = 1, Rows = 1;
    }
}
