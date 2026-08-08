using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.DTO;
using DrSoft.MarkCard.Model.EditMenu;
using System.Collections.Concurrent;

namespace DrSoft.MarkCard.Service
{
    public class MarkParamService
    {
        private readonly IMarkingParam _markingParam;
        private readonly IDrawingService _drawingService;
        public MarkParamService(IMarkingParam markingParam, IDrawingService drawingService)
        {
            _markingParam = markingParam;
            _drawingService = drawingService;
        }

        public async Task BindParametersAsync(int canvasId, List<int> entityIds, IList<ParameterBase> param)
        {
            await _markingParam.BindParametersAsync(canvasId, entityIds, param);
        }

        public IList<ParameterBase>? GetParameters(int canvasId, int entityId)
        {
            return _markingParam.GetParameters(canvasId, entityId);
        }

        public async Task<T> GetParameterAsync<T>(int canvasId, int entityId) where T : ParameterBase, new()
        {
            return await _markingParam.GetParameterAsync<T>(canvasId, entityId);
        }

        /// <summary>
        /// 获取全局加工参数（新建图形的默认参数模板）
        /// </summary>
        public IList<ParameterBase> GetGlobalParameters()
        {
            return _markingParam.GetGlobalParameters();
        }

        /// <summary>
        /// 设置全局加工参数
        /// </summary>
        public void SetGlobalParameters(IList<ParameterBase> parameters)
        {
            _markingParam.SetGlobalParameters(parameters);
        }

        /// <summary>
        /// 将全局加工参数绑定到指定的图形列表（用于校正图形等批量创建场景）
        /// </summary>
        public async Task BindGlobalParametersToEntitiesAsync(int canvasId, IEnumerable<int> entityIds)
        {
            var globalParams = GetGlobalParameters();
            if (globalParams != null && globalParams.Count > 0)
            {
                await _markingParam.BindParametersAsync(canvasId, entityIds.ToList(), globalParams);
            }
        }

        /// <summary>
        /// 构建打标作业。selectedEntityIds 为 null 或空时包含全部图形，否则仅包含指定图形。
        /// </summary>
        public Task<MarkingJobDto> BuildMarkingJobAsync(int canvasId, List<int>? selectedEntityIds = null)
        {
            ICanvasData? canvas = _drawingService.CanvasService.GetActiveCanvasData();
            if (canvas == null) return Task.FromResult<MarkingJobDto>(null!);

            bool filterBySelection = selectedEntityIds != null && selectedEntityIds.Count > 0;
            var selectedIdSet = filterBySelection ? new HashSet<int>(selectedEntityIds!) : null;

            // 1. 收集顶层图形
            var targetTopShapes = canvas.Layers.SelectMany(l => l.Shapes).ToList();
            if (filterBySelection)
                targetTopShapes = targetTopShapes.Where(s => selectedIdSet!.Contains(s.UId)).ToList();

            // 2. 构建参数映射
            var allParams = _markingParam.GetParameters(canvasId);
            var parameters = _markingParam.FlattenParameters(canvas, allParams);

            if (filterBySelection)
            {
                var targetLeafIds = new HashSet<int>(
                    targetTopShapes.SelectMany(s => GetLeafShapes(s)).Select(s => s.UId));
                parameters = parameters
                    .Where(kvp => targetLeafIds.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            // 3. 并行构建加工参数和高级特性参数
            var (processParamMap, advancedFeatureParamMap) = BuildProcessParamMaps(parameters);

            // 4. 收集所有叶子图形
            var leafShapes = targetTopShapes.SelectMany(s => GetLeafShapes(s)).ToList();

            // 5. 计算虚线段
            ComputeDashSegments(leafShapes, parameters);

            // 6. 设置填充对象的激光行进方向（偶数行 false，奇数行 true）
            ApplyHatchFillDirection(targetTopShapes);

            var job = new MarkingJobDto
            {
                Shapes = leafShapes,
                ParameterMap = processParamMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                AdvancedFeatureParamMap = advancedFeatureParamMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };
            return Task.FromResult(job);
        }

        /// <summary>
        /// 并行构建每个叶子的 ProcessParam 和 AdvancedFeatureParam
        /// </summary>
        private static (ConcurrentDictionary<int, ProcessParam>, ConcurrentDictionary<int, AdvancedFeatureParam>)
            BuildProcessParamMaps(Dictionary<int, IList<ParameterBase>> parameters)
        {
            var processParamMap = new ConcurrentDictionary<int, ProcessParam>();
            var advancedFeatureParamMap = new ConcurrentDictionary<int, AdvancedFeatureParam>();

            Parallel.ForEach(parameters, kvp =>
            {
                int entityId = kvp.Key;
                IList<ParameterBase> properties = kvp.Value;

                var engravingParam = properties.OfType<EngravingParameter>().FirstOrDefault() ?? new EngravingParameter();
                var delayParam = properties.OfType<DelayParameter>().FirstOrDefault() ?? new DelayParameter();
                var skyWriting = properties.OfType<SkyWritingSettingsModel>().FirstOrDefault();
                var extendHeadTail = properties.OfType<ExtendHeadTailSettingsModel>().FirstOrDefault();

                processParamMap[entityId] = new ProcessParam
                {
                    Power = engravingParam.Power,
                    Frequency = engravingParam.Frequency,
                    RepeatCount = engravingParam.EngraveCount,
                    DotDuration = (int)(engravingParam.DotEngraveTime * 1000),
                    MarkSpeed = engravingParam.Speed,

                    JumpSpeed = delayParam.JumpSpeed,
                    MarkDelay = delayParam.EngraveDelay * 1000,
                    JumpDelay = delayParam.JumpDelay * 1000,
                    PolyDelay = delayParam.CornerDelay * 1000,
                    LaserOnDelay = delayParam.StartDelay * 1000,
                    LaserOffDelay = delayParam.EndDelay * 1000,
                };

                if (skyWriting != null)
                {
                    advancedFeatureParamMap[entityId] = new AdvancedFeatureParam
                    {
                        SkyWritingModel = skyWriting.SkyWritingModel,
                        DelayTime = skyWriting.DelayTime,
                        LaserOnDelay = (int)skyWriting.LaserOnDelay,
                        RunInTime = (int)skyWriting.RunInTime,
                        RunOutTime = (int)skyWriting.RunOutTime,
                        ExtremeAngle = skyWriting.ExtremeAngle,
                    };
                }

                if (extendHeadTail != null)
                {
                    if (!advancedFeatureParamMap.ContainsKey(entityId))
                        advancedFeatureParamMap[entityId] = new AdvancedFeatureParam();
                    advancedFeatureParamMap[entityId].RunInCompensationLength = extendHeadTail.HeadExtendLength;
                    advancedFeatureParamMap[entityId].RunOutCompensationLength = extendHeadTail.TailExtendLength;
                }
            });

            return (processParamMap, advancedFeatureParamMap);
        }

        /// <summary>
        /// 为折线叶子图形计算虚线段（当 DashSettingParameter.OutputAsDashed 为 true 时）
        /// </summary>
        private static void ComputeDashSegments(
            List<IShapeData> leafShapes,
            Dictionary<int, IList<ParameterBase>> parameters)
        {
            foreach (var leaf in leafShapes)
            {
                if (leaf is not IPolyLineShapeData polyLine) continue;
                if (!parameters.TryGetValue(leaf.UId, out var leafParams)) continue;

                var settings = leafParams.OfType<DashSettingParameter>().FirstOrDefault();
                if (settings == null || !settings.OutputAsDashed) continue;

                var dashGroups = settings.DashGroups
                    .Select(g => (g.A, g.B))
                    .ToList();

                var segments = DashSegmentGenerator.Generate(
                    dashGroups,
                    polyLine.Vertices,
                    polyLine.IsClosed,
                    settings.IsOddEvenAlign,
                    settings.EvenRowOffset);

                if (segments.Count > 0)
                    polyLine.DashSegments = segments;
            }
        }

        /// <summary>
        /// 为填充对象（Hatch）的子折线设置激光行进方向：偶数行 false，奇数行 true
        /// </summary>
        private static void ApplyHatchFillDirection(IEnumerable<IShapeData> topShapes)
        {
            foreach (var shape in topShapes)
            {
                ApplyHatchFillDirectionRecursive(shape);
            }
        }

        private static void ApplyHatchFillDirectionRecursive(IShapeData shape)
        {
            if (shape.Type == ShapeType.Hatch)
            {
                int rowIndex = 0;
                foreach (var child in shape.ChildShapes)
                {
                    if (child is IShape mutableChild)
                    {
                        mutableChild.IsClockwise = (rowIndex % 2 != 0);
                    }
                    rowIndex++;
                }
            }

            foreach (var child in shape.ChildShapes)
            {
                ApplyHatchFillDirectionRecursive(child);
            }
        }

        /// <summary>
        /// 递归获取叶子图形（无子图形的基础图形元素）
        /// </summary>
        private static IEnumerable<IShapeData> GetLeafShapes(IShapeData shape)
        {
            if (shape.ChildShapes.Count == 0)
            {
                yield return shape;
            }
            else
            {
                foreach (var child in shape.ChildShapes)
                    foreach (var leaf in GetLeafShapes(child))
                        yield return leaf;
            }
        }

        /// <summary>
        /// 递归判断容器图形是否包含选中的子节点
        /// </summary>
        private static bool ContainsSelectedChild(IShapeData shape, HashSet<int> selectedIds)
        {
            foreach (var child in shape.ChildShapes)
            {
                if (selectedIds.Contains(child.UId))
                    return true;
                if (ContainsSelectedChild(child, selectedIds))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 删除指定画布的所有参数
        /// </summary>
        public bool RemoveCanvasParameters(int canvasId)
        {
            return _markingParam.RemoveCanvasParameters(canvasId);
        }

        /// <summary>
        /// 删除指定图形的加工参数
        /// </summary>
        public bool RemoveEntityParameters(int canvasId, int entityId)
        {
            return _markingParam.RemoveEntityParameters(canvasId, entityId);
        }

        /// <summary>
        /// 批量删除指定图形的加工参数
        /// </summary>
        public void RemoveEntityParameters(int canvasId, IEnumerable<int> entityIds)
        {
            _markingParam.RemoveEntityParameters(canvasId, entityIds);
        }
    }
}
