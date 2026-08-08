using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.UI.Models;
using System.Windows.Input;

namespace DrSoft.MarkCard.UI.ViewModes.Tools
{
    public partial class VectorToolViewModel : ObservableObject
    {
        private readonly IEventBus _eventBus;

        private readonly IShapeService _shapeService;


        public ICommand unionCommand { get; }
        public ICommand intersectCommand { get; }
        public ICommand trimCommand { get; }
        public ICommand keepMainCommand { get; }

        public List<MenuToolCommand<VectorCommandType, bool, GraphicResult>> Commands { get; set; }

        public Dictionary<VectorCommandType, MenuToolCommand<VectorCommandType, bool, GraphicResult>> CommandMap => Commands?.ToDictionary(c => c.Command);

        public VectorToolViewModel(IShapeService shapeService)
        {
            _shapeService = shapeService;
            _eventBus = EventBus.Instance;

            unionCommand = new RelayCommand<string>(CommandExcute, CanExcute);
            intersectCommand = new RelayCommand<string>(CommandExcute, CanExcute);
            trimCommand = new RelayCommand<string>(CommandExcute, CanExcute);
            keepMainCommand = new RelayCommand<string>(CommandExcute, CanExcute);

            // 初始化命令列表
            Commands = new List<MenuToolCommand<VectorCommandType, bool, GraphicResult>>
            {
                    new MenuToolCommand<VectorCommandType, bool, GraphicResult> { Command = VectorCommandType.Union, UICommand = unionCommand, Action = new UniversalAction(() => _shapeService.Union()), Active = false, IsDialogConfirmed = false },
                    new MenuToolCommand<VectorCommandType, bool, GraphicResult> { Command = VectorCommandType.Intersect, UICommand = intersectCommand, Action = new UniversalAction(() =>_shapeService.Intersect()), Active = false, IsDialogConfirmed = false },
                    new MenuToolCommand<VectorCommandType, bool, GraphicResult> { Command = VectorCommandType.Trim, UICommand = trimCommand, Action = new UniversalAction(() =>_shapeService.Trim()), Active = false, IsDialogConfirmed = false },
                    new MenuToolCommand<VectorCommandType, bool, GraphicResult> { Command = VectorCommandType.KeepMain, UICommand = keepMainCommand, Action =  new UniversalAction(() => _shapeService.KeepMain()), Active = false, IsDialogConfirmed = false },
            };

            _eventBus.Subscribe<CommandCapabilityChangedEvent>(args =>
            {
                var cap = args.Capabilities;
                foreach (var cmd in Commands)
                {
                    switch (cmd.Command)
                    {
                        case VectorCommandType.Union:
                        case VectorCommandType.Intersect:
                        case VectorCommandType.Trim:
                        case VectorCommandType.KeepMain:
                            cmd.Active = cap.CanVectorCombine; 
                            break;
                        default:
                            break;
                    }
                }
            });
        }

        public bool CanExcute(string parameter)
        {
            // 根据业务逻辑返回 true/false
            bool canExcute = false;
            if (Enum.TryParse<VectorCommandType>(parameter, true, out var nodeCommand))
            {
                MenuToolCommand<VectorCommandType, bool, GraphicResult> cmd = CommandMap.GetValueOrDefault(nodeCommand);
                canExcute = cmd.Active;
            }
            return canExcute;
        }
        [RelayCommand]
        public void CommandExcute(string parameter)
        {
            if (Enum.TryParse<VectorCommandType>(parameter, true, out var menuChooseCommand))
            {
                MenuToolCommand<VectorCommandType, bool, GraphicResult> cmd = CommandMap.GetValueOrDefault(menuChooseCommand);
                if (!cmd.Active) return;
                // 在这里处理命令逻辑，例如发布事件或调用服务
                //Console.WriteLine($"执行命令: {cmd.Command}");

                //cmd.IsChecked = false;

                // 命令触发需要对话框确认的处理逻辑
                if (cmd.IsDialogConfirmed)
                {
                    //TriggerCommandNeedDialogConfirm(cmd);
                }
                else // 其他命令的处理逻辑
                    cmd.Action?.Invoke(false);
            }
        }

    }
}
