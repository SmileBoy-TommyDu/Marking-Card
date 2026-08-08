using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.MarkCard.Service;
using DrSoft.MarkCard.UI.ViewModes.EditMenu;
using DrSoft.MarkCard.UI.ViewModes.Tools;
using DrSoft.MarkCard.UI.Views.Config;
using System.Windows;
using ConfigModel = DrSoft.MarkCard.Model.Config.Config;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class MainViewModel : ObservableObject
    {
        public EditMenuViewModel? EditMenuVm { get; }
        public TextToolViewModel? TextToolVm { get; }
        public EditPathNodesToolViewModel? EditPathNodesToolVm { get; }
        public ToolbarViewModel? ToolbarVm { get; }

        private readonly ConfigModel _config;
        private ConfigDialog? _configDialog; // 单实例

        public FileViewModel? FileVm { get; }

        public MainViewModel(MarkService service, FileViewModel fileVm, EditMenuViewModel editMenuViewModel, TextToolViewModel textToolViewModel, EditPathNodesToolViewModel editPathNodesToolVm
            , ConfigModel config, ToolbarViewModel toolbarViewModel)
        {
            FileVm = fileVm;
            EditMenuVm = editMenuViewModel;
            TextToolVm = textToolViewModel;
            EditPathNodesToolVm = editPathNodesToolVm;
            ToolbarVm = toolbarViewModel;// new ToolbarViewModel(EditMenuVm, FileVm, TextToolVm);
            _config = config;
        }

        [RelayCommand]
        private void OpenPreferences()
        {
            if (_configDialog is not null)
            {
                if (_configDialog.WindowState == WindowState.Minimized)
                {
                    _configDialog.WindowState = WindowState.Normal;
                }

                _configDialog.Activate();
                return;
            }

            _configDialog = new ConfigDialog(_config);

            foreach (var window in Application.Current.Windows)
            {
                if (window is MainWindow mainWnd)
                {
                    _configDialog.Owner = mainWnd;
                    break;
                }
            }

            _configDialog.Closed += (_, _) => _configDialog = null;
            _configDialog.Show();
            _configDialog.Activate();
        }
    }
}
