using DrSoft.Drawing.Controls.DXFHelper.Parser;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Controls.DXFHelper
{
    public sealed class DxfImporter
    {
        public event Action<double>? OnProgress;
        public event Action<SceneStore, ParseSummary>? OnCompleted;

        public event Action<CanvasSnapshotDto, ParseSummary>? OnNewCompleted;
        public event Action<Exception>? OnFailed;

        public async Task ImportAsync(string path, CancellationToken ct = default)
        {
            try
            {
                var store = new SceneStore();

                var opts = new DxfParseOptions
                {
                    BatchSize = 20_000,
                    ProgressIntervalBytes = 512 * 1024,
                    ExplodeLwPolyline = false,
                    ChannelCapacity = 3,
                };

                var parser = new DxfParser(path, opts);
                parser.OnProgress = p => OnProgress?.Invoke(p);

                parser.OnBatch = snap =>
                {
                    var ents = snap.Entities;
                    int n = ents.Count;
                    store.EnsureDtoCapacity(store.DtoCount + n);
                    string? lastLayerName = null;
                    int lastLayerIndex = -1;
                    bool showJumpLine = store.DtoCount + n < 500; // 是否显示加工jump路径；大文件按累计数量关闭
                    for (int i = 0; i < n; i++)
                    {
                        var ent = ents[i];
                        int li;
                        if (lastLayerIndex >= 0 && string.Equals(ent.Layer, lastLayerName, StringComparison.OrdinalIgnoreCase))
                        {
                            li = lastLayerIndex;
                        }
                        else
                        {
                            li = store.GetOrAddLayer(ent.Layer);
                            lastLayerName = ent.Layer;
                            lastLayerIndex = li;
                        }

                        switch (ent)
                        {
                            case DxfLine e: store.AddLine((float)e.X1, (float)e.Y1, (float)e.X2, (float)e.Y2, li, showJumpLine); break;
                            case DxfArc e:

                                bool hasExact = !double.IsNaN(e.ExactStartX) && !double.IsNaN(e.ExactStartY)
                                                 && !double.IsNaN(e.ExactEndX) && !double.IsNaN(e.ExactEndY);
                                store.AddArc(e.Cx, e.Cy, e.R,
                                             e.StartAngle, e.EndAngle, li, showJumpLine,
                                             hasExact ? e.ExactStartX : null,
                                             hasExact ? e.ExactStartY : null,
                                             hasExact ? e.ExactEndX : null,
                                             hasExact ? e.ExactEndY : null);
                                // 检查是否为三点圆弧 XDATA
                                //if (e.XDataApp == "THREE_POINT_ARC" && e.XDataDoubles.Count >= 6)
                                //{
                                //    var p1 = ((float)e.XDataDoubles[0], (float)e.XDataDoubles[1]);
                                //    var p2 = ((float)e.XDataDoubles[2], (float)e.XDataDoubles[3]);
                                //    var p3 = ((float)e.XDataDoubles[4], (float)e.XDataDoubles[5]);
                                //    store.AddArcThreePoints(p1, p2, p3, li, showJumpLine);
                                //}
                                //else
                                //{
                                //    // 如果圆弧来自 LWPOLYLINE bulge 转换，使用其记录的精确端点坐标，
                                //    // 避免从中心/半径/角度重新计算带来的浮点误差。
                                //    bool hasExact = !double.IsNaN(e.ExactStartX) && !double.IsNaN(e.ExactStartY)
                                //                    && !double.IsNaN(e.ExactEndX) && !double.IsNaN(e.ExactEndY);
                                //    store.AddArc(e.Cx, e.Cy, e.R,
                                //                 e.StartAngle, e.EndAngle, li, showJumpLine,
                                //                 hasExact ? e.ExactStartX : null,
                                //                 hasExact ? e.ExactStartY : null,
                                //                 hasExact ? e.ExactEndX : null,
                                //                 hasExact ? e.ExactEndY : null);
                                //}
                                break;
                            case DxfCircle e: store.AddCircle((float)e.Cx, (float)e.Cy, (float)e.R, li, showJumpLine); break;
                            case DxfPoint e: store.AddPoint((float)e.X, (float)e.Y, li, showJumpLine); break;
                            case DxfLwPolyline e: store.AddPolyLinePoints(e.Closed, e.Width, e.Verts, li, showJumpLine); break;
                            case DxfEllipse e:
                                // DXF ELLIPSE: MajorAxisX/Y 是从中心指向长轴端点的向量
                                // 长半径 = 向量长度，旋转角度 = 向量方向角
                                // 短半径 = 长半径 × Ratio
                                double majorLen = Math.Sqrt(e.MajorAxisX * e.MajorAxisX + e.MajorAxisY * e.MajorAxisY);
                                double rotDeg = Math.Atan2(e.MajorAxisY, e.MajorAxisX) * 180.0 / Math.PI;
                                float rx = (float)majorLen;
                                float ry = (float)(majorLen * e.Ratio);
                                store.AddEllipse((float)e.Cx, (float)e.Cy, rx, ry, (float)rotDeg, li, showJumpLine);
                                break;
                            case DxfRectangle e:
                                store.AddRectangle(e.Points,   e.Concor ,  e.Chamfer ,  li, showJumpLine, e.Rotation); break;
                            case DxfText e:
                                store.AddText(e.Text, (float)e.X, (float)e.Y, (float)e.Height, (float)e.Rotation, e.FontName.ToFontString(), li, showJumpLine, e.HAlign, e.VAlign, e.Obliquing, e.IsUnderline);
                                break;
                            case DxfMText e:
                                {
                                    // MTEXT attachment point (71) encodes both H+V alignment:
                                    // 1=TL,2=TC,3=TR, 4=ML,5=MC,6=MR, 7=BL,8=BC,9=BR
                                    int hAlign = e.AttachmentPoint switch
                                    {
                                        2 or 5 or 8 => 1, // Center
                                        3 or 6 or 9 => 2, // Right
                                        _ => 0           // Left
                                    };
                                    int vAlign = e.AttachmentPoint switch
                                    {
                                        1 or 2 or 3 => 3, // Top
                                        4 or 5 or 6 => 2, // Middle
                                        7 or 8 or 9 => 1, // Bottom
                                        _ => 0           // Baseline
                                    };
                                    store.AddText(e.Text, (float)e.X, (float)e.Y, (float)e.Height, (float)e.Rotation, e.FontName.ToFontString(), li, showJumpLine, hAlign, vAlign, e.Obliquing, e.IsUnderline);
                                }
                                break;
                            case DxfSpline e:
                                if (e.Degree == 3)
                                    store.AddBezier(e.FitPoints, li, showJumpLine);
                                else
                                    store.AddArbitraryCurve(e.FitPoints, e.Closed, li, showJumpLine);
                                break;
                       
                        }
                    }
                    return Task.CompletedTask;
                };

         

                var summary = await parser.ParseAsync(ct);
                store.Build();
                Trace.WriteLine($"解析完成:{DateTime.Now.ToString()}");
                CanvasSnapshotDto snapshotDto = new()
                {
                    Id = 1,
                    Name = Path.GetFileName(path)
                };
                int layerCount = store.Layers.Length;
                // 直接构建 DrawingLayer
                var drawingLayers = new List<ILayerData>(layerCount);
                for (int i = 0; i < layerCount; i++)
                {
                    var layer = store.Layers[i];
                    var drawingLayer = new DrawingLayer
                    {
                        IsVisible = true,
                        Color = $"#{layer.Color.Red:X2}{layer.Color.Green:X2}{layer.Color.Blue:X2}",
                        Name = layer.Name ?? $"E{i}",
                    };
                    drawingLayer.AddShapes(store.ItemsDto.Where(x => x.LayerId == layer.Idx).ToList());
                    drawingLayers.Add(drawingLayer);
                }
                snapshotDto.Layers = drawingLayers;
                Trace.WriteLine($"转换完成:{DateTime.Now.ToString()}");
                OnNewCompleted?.Invoke(snapshotDto, summary);
              //  OnCompleted?.Invoke(store, summary);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { OnFailed?.Invoke(ex); }
        }



    }
}
