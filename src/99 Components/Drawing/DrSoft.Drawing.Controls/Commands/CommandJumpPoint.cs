using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 跳点命令：支持跳点设置的撤销/重做
    /// </summary>
    internal class CommandJumpPoint : IDrawingCommand
    {
        public string Description => "跳点设置";

        private readonly List<JumpPointSnapshot> _snapshots;

        /// <summary>
        /// 构造跳点命令，在执行操作前捕获图形的当前跳点状态。
        /// 调用方需先调用 Execute() 传入操作后状态，再交给 CommandManager。
        /// </summary>
        public CommandJumpPoint(IEnumerable<DrawObject> draws)
        {
            _snapshots = draws.Select(d => new JumpPointSnapshot(
                d,
                d.IntersectionSkipPoints.ToList(),
                d.IntersectionSkipRadius,
                d.SelfIntersectionSkipCount,
                d.IntersectionSkipBridgeDirections.ToList()
            )).ToList();
        }

        /// <summary>
        /// 捕获操作后的跳点状态（用于 Redo 时恢复）。
        /// 应在跳点操作完成后、CommandManager.Execute 之前调用。
        /// </summary>
        public void CaptureAfterState()
        {
            foreach (var s in _snapshots)
            {
                s.AfterSkipPoints = s.Shape.IntersectionSkipPoints.ToList();
                s.AfterSkipRadius = s.Shape.IntersectionSkipRadius;
                s.AfterSelfIntersectionSkipCount = s.Shape.SelfIntersectionSkipCount;
                s.AfterBridgeDirections = s.Shape.IntersectionSkipBridgeDirections.ToList();
            }
        }

        /// <summary>
        /// 恢复操作后的跳点状态（由 Redo 调用）。
        /// </summary>
        public void Execute()
        {
            foreach (var s in _snapshots)
            {
                if (s.AfterSkipPoints != null)
                {
                    s.Shape.IntersectionSkipPoints = s.AfterSkipPoints.ToList();
                    s.Shape.IntersectionSkipRadius = s.AfterSkipRadius;
                    s.Shape.SelfIntersectionSkipCount = s.AfterSelfIntersectionSkipCount;
                    s.Shape.IntersectionSkipBridgeDirections = s.AfterBridgeDirections?.ToList() ?? new List<SKPoint>();
                }
            }
        }

        /// <summary>
        /// 恢复操作前的跳点状态（由 Undo 调用）。
        /// </summary>
        public bool Undo()
        {
            foreach (var s in _snapshots)
            {
                s.Shape.IntersectionSkipPoints = s.BeforeSkipPoints.ToList();
                s.Shape.IntersectionSkipRadius = s.BeforeSkipRadius;
                s.Shape.SelfIntersectionSkipCount = s.BeforeSelfIntersectionSkipCount;
                s.Shape.IntersectionSkipBridgeDirections = s.BeforeBridgeDirections.ToList();
            }
            return true;
        }

        private record JumpPointSnapshot(
            DrawObject Shape,
            List<SKPoint> BeforeSkipPoints,
            float BeforeSkipRadius,
            int BeforeSelfIntersectionSkipCount,
            List<SKPoint> BeforeBridgeDirections)
        {
            public List<SKPoint>? AfterSkipPoints { get; set; }
            public float AfterSkipRadius { get; set; }
            public int AfterSelfIntersectionSkipCount { get; set; }
            public List<SKPoint>? AfterBridgeDirections { get; set; }
        }
    }
}
