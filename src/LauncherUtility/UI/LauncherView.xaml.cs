using System.Windows.Controls;
using System.Windows.Input;

namespace LauncherUtility.UI
{
    public partial class LauncherView : UserControl
    {
        public LauncherView()
        {
            InitializeComponent();
        }

        private LauncherViewModel ViewModel => DataContext as LauncherViewModel;

        public void FocusSearch()
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    ViewModel?.MoveSelection(1);
                    ScrollSelectedIntoView();
                    e.Handled = true;
                    break;
                case Key.Up:
                    ViewModel?.MoveSelection(-1);
                    ScrollSelectedIntoView();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    ViewModel?.LaunchSelected();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (!string.IsNullOrEmpty(SearchBox.Text))
                    {
                        SearchBox.Clear();
                    }
                    else
                    {
                        ViewModel?.RequestClose();
                    }

                    e.Handled = true;
                    break;
            }
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel?.LaunchSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ViewModel?.RequestClose();
                e.Handled = true;
            }
        }

        private void OnItemClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is ToolRowViewModel row)
            {
                ViewModel?.Launch(row);
                e.Handled = true;
            }
        }

        private void ScrollSelectedIntoView()
        {
            if (ViewModel?.Selected != null)
            {
                ToolList.ScrollIntoView(ViewModel.Selected);
            }
        }
    }
}
