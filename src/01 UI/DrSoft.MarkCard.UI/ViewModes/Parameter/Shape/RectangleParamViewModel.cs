using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Model;

namespace DrSoft.MarkCard.UI.ViewModes.Parameter
{
    public partial class RectangleParamViewModel : BaseParamViewModel<RectangleParameter>
    {
        private IEventBus? _eventBus => EventBus.Instance;
        private bool _isApplying;

        public RectangleParamViewModel()
        {
            EventBus.Instance.Subscribe<ParaSaveEvent>(e =>
            {
                if (e.ParaSaveType == ParaSaveType.Element && e.Trigger && e.TriggerTitle.Equals("矩形"))
                    _ = OnApplyAsync();
            });

            _eventBus?.Subscribe<CanvasChangedEvent>(data =>
            {
                if (_isApplying)
                    return;

                switch (data.ChangeType)
                {
                    case CanvasChangeType.Command:
                    case CanvasChangeType.SelectSharps:
                        _ = LoadParameterAsync();
                        break;
                }
            }, replayLast: true);
        }

        /// <summary>
        /// 矩形是否为圆角模式（计算属性）
        /// </summary>
        public bool IsRound
        {
            get => Model.Mode == (int)CornerMode.Round;
            set
            {
                if (!value || IsRound) return;
                Model = Model with { Mode = (int)CornerMode.Round };
                NotifyRectangleStateChanged();
            }
        }

        /// <summary>
        /// 矩形是否为倒角模式（计算属性）
        /// </summary>
        public bool IsChamfer
        {
            get => Model.Mode == (int)CornerMode.Chamfer;
            set
            {
                if (!value || IsChamfer) return;
                Model = Model with { Mode = (int)CornerMode.Chamfer };
                NotifyRectangleStateChanged();
            }
        }

        public bool IsAllSame
        {
            get => Model.AllCornersSame;
            set
            {
                if (Model.AllCornersSame == value) return;

                Model.AllCornersSame = value;
                if (value)
                {
                    SyncAllCornersToTopLeft();
                }

                NotifyRectangleStateChanged();
            }
        }

        public string UnitModel
        {
            get => Model.Unit;
            set
            {
                if (Model.Unit == value) return;
                Model.Unit = value;
                NotifyRectangleStateChanged();
            }
        }

        public double Topleft
        {
            get => Model.TopLeft;
            set
            {
                if (NearlyEqual(Model.TopLeft, value)) return;

                Model.TopLeft = value;
                if (IsAllSame)
                {
                    SyncAllCornersToTopLeft();
                }

                NotifyRectangleStateChanged();
            }
        }

        // 用 new 隐藏基类的 ApplyCommand，直接绑定到矩形专属应用逻辑，
        // 避免 [RelayCommand] 生成的 command 因虚方法分派问题而始终调用基类逻辑。
        private IAsyncRelayCommand? _applyCommand;
        public new IAsyncRelayCommand ApplyCommand => _applyCommand ??= new AsyncRelayCommand(OnApplyAsync);

        private async Task OnApplyAsync()
        {
            _isApplying = true;

            try
            {
                if (Model.Unit == "%")
                {
                    if (Model.TopLeft > 100 || Model.TopRight > 100 || Model.BottomRight > 100 || Model.BottomLeft > 100)
                    {
                        EventBus.Instance.Publish(new ToastMessageEvent("设置的百分比超限，请重新输入", ToastType.Error));
                        return;
                    }
                }

                await BeforeApplyAsync(Model);

                var mode = Model.Unit == "%" ? RoundMode.Percent : RoundMode.Unit;
                var modecorner = Model.Mode == (int)CornerMode.Round ? CornerMode.Round : CornerMode.Chamfer;

                if (Model.AllCornersSame)
                {
                    SyncAllCornersToTopLeft();
                }

                switch (modecorner)
                {
                    case CornerMode.Chamfer:
                        _drawingService.Shapes.AdjustChamfer(mode, Model.TopLeft, Model.TopRight, Model.BottomRight, Model.BottomLeft);
                        break;
                    case CornerMode.Round:
                        _drawingService.Shapes.AdjustRect(mode, Model.TopLeft, Model.TopRight, Model.BottomRight, Model.BottomLeft);
                        break;
                }

                var parameters = new List<ParameterBase> { Model };
                if (_service != null)
                {
                    await _service.BindParametersAsync(RuntimeContext.ActiveCanvasId, RuntimeContext.Selections, parameters);
                }

                await AfterApplyAsync(Model);
            }
            finally
            {
                _isApplying = false;
            }
        }

        public override Task<RectangleParameter> LoadParameterAsync()
        {
            if (RuntimeContext.Selections == null || RuntimeContext.Selections.Count == 0)
            {
                NotifyRectangleStateChanged();
                return Task.FromResult(Model);
            }

            var result = _drawingService.Shapes.GetSelections();
            if (!result.IsSuccess || result.Value == null || result.Value.Count == 0)
            {
                NotifyRectangleStateChanged();
                return Task.FromResult(Model);
            }

            var shapeData = result.Value[0];
            if (shapeData is not IRectangleShapeData rectangle)
            {
                NotifyRectangleStateChanged();
                return Task.FromResult(Model);
            }

            var resolvedModel = CreateModelFromRectangle(rectangle);
            Model = resolvedModel;
            NotifyRectangleStateChanged();
            return Task.FromResult(Model);
        }

        internal static RectangleParameter CreateModelFromRectangle(IRectangleShapeData rectangle)
        {
            var hasChamfer =
                rectangle.ChamferTopLeft > 0 ||
                rectangle.ChamferTopRight > 0 ||
                rectangle.ChamferBottomRight > 0 ||
                rectangle.ChamferBottomLeft > 0;

            var cornerMode = hasChamfer ? CornerMode.Chamfer : CornerMode.Round;

            double topLeft;
            double topRight;
            double bottomRight;
            double bottomLeft;

            if (cornerMode == CornerMode.Chamfer)
            {
                topLeft = rectangle.ChamferTopLeft;
                topRight = rectangle.ChamferTopRight;
                bottomRight = rectangle.ChamferBottomRight;
                bottomLeft = rectangle.ChamferBottomLeft;
            }
            else
            {
                topLeft = rectangle.CornerRadiusTopLeft;
                topRight = rectangle.CornerRadiusTopRight;
                bottomRight = rectangle.CornerRadiusBottomRight;
                bottomLeft = rectangle.CornerRadiusBottomLeft;
            }

            var hasAnyCornerValue =
                !NearlyEqual(topLeft, 0) ||
                !NearlyEqual(topRight, 0) ||
                !NearlyEqual(bottomRight, 0) ||
                !NearlyEqual(bottomLeft, 0);

            var allCornersSame =
                hasAnyCornerValue &&
                NearlyEqual(topLeft, topRight) &&
                NearlyEqual(topLeft, bottomRight) &&
                NearlyEqual(topLeft, bottomLeft);

            var model = new RectangleParameter
            {
                Unit = "mm",
                Mode = (int)cornerMode,
                AllCornersSame = allCornersSame,
                TopLeft = topLeft,
                TopRight = topRight,
                BottomRight = bottomRight,
                BottomLeft = bottomLeft
            };

            return model;
        }

        private void SyncAllCornersToTopLeft()
        {
            Model = Model with
            {
                TopRight = Model.TopLeft,
                BottomRight = Model.TopLeft,
                BottomLeft = Model.TopLeft
            };
        }

        private void NotifyRectangleStateChanged()
        {
            OnPropertyChanged(nameof(IsRound));
            OnPropertyChanged(nameof(IsChamfer));
            OnPropertyChanged(nameof(IsAllSame));
            OnPropertyChanged(nameof(UnitModel));
            OnPropertyChanged(nameof(Topleft));
            OnPropertyChanged(nameof(Model));
        }

        private static bool NearlyEqual(double left, double right)
        {
            return Math.Abs(left - right) < 0.0001d;
        }
    }
}
