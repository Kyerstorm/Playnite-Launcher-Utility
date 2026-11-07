using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LauncherUtility.Settings
{
    public partial class LauncherSettingsView : UserControl
    {
        public LauncherSettingsView()
        {
            InitializeComponent();
        }

        private void OnToolDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is SettingsToolRow row &&
                DataContext is LauncherSettingsViewModel vm &&
                vm.EditToolCommand.CanExecute(row))
            {
                vm.EditToolCommand.Execute(row);
            }
        }
    }
}
