using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.CommonUI.UserControls;
using DrSoft.MarkCard.Model.Parameter;
using DrSoft.MarkCard.UI.UIConfig;
using DrSoft.MarkCard.UI.UserControls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class DrawingBoardViewModel : BaseParamViewModel<DrawingBoardParameter>
    {
        private CanvasSystemConfig canvasSystemConfig;

        [ObservableProperty]
        private bool isBoardLocked=true;

        public DrawingBoardViewModel()
        {
            canvasSystemConfig = App.GetService<CanvasSystemConfig>();
            EventBus.Instance.Subscribe<ParaSaveEvent>(OnSaveAll);
            if (canvasSystemConfig.DrawingBoardParameter != null)
            {
                _drawingService.CanvasService.SetGridSize((float)canvasSystemConfig.DrawingBoardParameter.GridSizeW, (float)canvasSystemConfig.DrawingBoardParameter.GridSizeH);
                _drawingService.CanvasService.SetMachineBounds((float)canvasSystemConfig.DrawingBoardParameter.BoardW, (float)canvasSystemConfig.DrawingBoardParameter.BoardH);
                _drawingService.CanvasService.SetMicroMove((float)canvasSystemConfig.DrawingBoardParameter.MicroMoveX, (float)canvasSystemConfig.DrawingBoardParameter.MicroMoveY);
            }
        }

        private void OnSaveAll(ParaSaveEvent @event)
        {
            if(@event.ParaSaveType==ParaSaveType.Canvas&&@event.Trigger)
            {
                SaveFun();
            }
        }

        [RelayCommand]
        private void LockCanvasWH()
        {
            IsBoardLocked = !IsBoardLocked;
            Model.IsBoardLocked = IsBoardLocked;
        }


        [RelayCommand]
        private void LockGridWH()
        {
            Model.IsGridLocked = true;
        }

        [RelayCommand]
        private void GoCenter()
        {
            _drawingService.CanvasService.UpdateCanvasCenterPoint(Model.OriginX,Model.OriginY);
        }

        public ICommand OnGridSizeCommittedCommand => new RelayCommand<SizeLockEventArg>(OnGridSizeCommitted);

        protected void OnGridSizeCommitted(SizeLockEventArg value)
        {
            var x = value.WidthValue;
            var y = value.HeightValue;
            _drawingService.CanvasService.SetGridSize((float)x, (float)y);
        }

        protected override Task ExecuteApplyAsync()
        {
            SaveFun();
            return Task.CompletedTask;
        }
        private void SaveFun()
        {
            // _drawingService.Workspace.UpdateCanvasCenterPoint(Model.OriginX, Model.OriginY);
            _drawingService.CanvasService.SetGridSize((float)Model.GridSizeW, (float)Model.GridSizeH);
            _drawingService.CanvasService.SetMachineBounds((float)Model.BoardW, (float)Model.BoardH);
            _drawingService.CanvasService.SetMicroMove((float)Model.MicroMoveX, (float)Model.MicroMoveY);
            canvasSystemConfig.DrawingBoardParameter = Model;
            canvasSystemConfig.SaveToFile();
        }
    }
}
