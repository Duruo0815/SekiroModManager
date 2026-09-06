using System.Windows;
using SekiroModManager.App.Theme;
using SekiroModManager.Core.Models;

namespace SekiroModManager.App.Views;

public partial class PlanWindow : Window
{
    private readonly DeployPlan _plan;
    private readonly Action _onDeploy;

    public PlanWindow(DeployPlan plan, Action onDeploy)
    {
        InitializeComponent();
        _plan = plan;
        _onDeploy = onDeploy;

        Loaded += (s, e) => WindowTitleBarHelper.ApplyThemeToWindow(this, ThemeManager.IsDark);
        LoadData();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTitleBarHelper.ApplyThemeToWindow(this, ThemeManager.IsDark);
    }

    private void LoadData()
    {
        TxtTotalFiles.Text = _plan.TotalSourceFiles.ToString();
        TxtDeployFiles.Text = _plan.Files.Count.ToString();
        TxtConflictFiles.Text = _plan.Conflicts.Count.ToString();

        TabConflicts.Header = $"⚠️ 冲突明细 ({_plan.Conflicts.Count})";
        TabFiles.Header = $"📄 生效清单 ({_plan.Files.Count})";

        GridConflicts.ItemsSource = _plan.Conflicts;
        GridFiles.ItemsSource = _plan.Files;

        if (_plan.Warnings.Count > 0)
        {
            TxtWarningNotice.Text = "提示：" + string.Join("；", _plan.Warnings);
        }
        else
        {
            TxtWarningNotice.Text = "";
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnDeploy_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _onDeploy();
    }
}
