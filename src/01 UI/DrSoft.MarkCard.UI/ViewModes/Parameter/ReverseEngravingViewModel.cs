using DrSoft.MarkCard.Model.Parameter;
using DrSoft.MarkCard.UI.Views;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class ReverseEngravingViewModel : BaseParamViewModel<ReverseEngravingParameter>
    {
        public ReverseEngravingViewModel()
        {
            Content = new ReverseEngravingView();
        }


        protected override async Task BeforeApplyAsync(ReverseEngravingParameter parameter)
        {
            
        }
    }
}
