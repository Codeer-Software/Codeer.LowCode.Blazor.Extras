using System.Windows.Controls;

namespace Codeer.LowCode.Blazor.Extras.Designer.Controls
{
    /// <summary>
    /// ApprovalFlowField 専用の検索コントロール。
    /// ApprovalFlowFieldDesign.GetSearchControlTypeFullName がこの型名を返し、
    /// デザイナの条件エディタが Activator で生成する (DataContext = IMatchConditionData)。
    /// </summary>
    public partial class ApprovalFlowSearchControl : UserControl
    {
        public ApprovalFlowSearchControl()
        {
            InitializeComponent();
        }
    }
}
