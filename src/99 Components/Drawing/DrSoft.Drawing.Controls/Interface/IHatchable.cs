using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.DTO;
using SkiaSharp;
using System.Runtime.CompilerServices;

namespace DrSoft.Drawing.Controls.Interface
{
    public interface IHatchable
    {
        HatchParamDto HatchParamInfo { get; set; }

        HatchPatternObjects CreateHatchPattern();

        List<DrawObject> ExpandHatchObject(List<(SKPoint Start, SKPoint End)> hatchLineObjects);

        // ── 内部状态存储 ─────────────────────────────────────────
        private static readonly ConditionalWeakTable<IHatchable, HatchState> _states = new();
        private HatchState State => _states.GetOrCreateValue(this);

        // ── 带回调注入的 HatchInfo 快捷重载 ─────────────────────
        void SetHatchParam(HatchParamDto info)
        {
            HatchParamInfo = info;       // 触发 InvalidateHatch → 脏标记
            InvalidateHatch();
        }

        // ── 缓存属性：读取时惰性构建，构建后触发回调 ────────────
        HatchPatternObjects? HatchPattern
        {
            get
            {
                var state = State;
                if (state.IsDirty)
                {
                    state.Pattern = CreateHatchPattern();
                    state.IsDirty = false;
                }
                return state.Pattern;
            }
        }

        // ── 打脏：外部主动失效时也可选择立即重建并回调 ──────────
        void InvalidateHatch(bool rebuildImmediately = false)
        {
            var state = State;
            state.IsDirty = true;

            if (rebuildImmediately)
            {
                state.Pattern = CreateHatchPattern();
                state.IsDirty = false;
            }
        }

        // ── 状态载体 ─────────────────────────────────────────────
        private class HatchState
        {
            public HatchPatternObjects? Pattern;
            public bool IsDirty = true;
        }
    }



    public class HatchPatternObjects
    {
        public List<DrawObject>? HatchObjects { get; set; } = new();
        public List<(SKPoint Start, SKPoint End)>? HatchLineObjects { get; set; } = new();
    }
}
