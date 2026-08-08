using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.UI.ViewModes;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views
{
    public abstract class ParamViewHelper
    {
        public static void InitializeParameter<TViewModel, TParameter>(UserControl view)
        where TViewModel : BaseParamViewModel<TParameter>
        where TParameter : ParameterBase, new()
        {
            var vm = App.GetService<TViewModel>();
            _ = vm.LoadParameterAsync();
            view.DataContext = vm;
        }
    }
}
