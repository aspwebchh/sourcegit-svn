using Avalonia.Controls;
using Avalonia.Input;

namespace SourceGit.Views
{
    public partial class SvnRepositoryToolbar : UserControl
    {
        public SvnRepositoryToolbar()
        {
            InitializeComponent();
        }

        private async void OnUpdateTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.SvnRepository repo)
            {
                await repo.UpdateAsync(e.KeyModifiers is KeyModifiers.Control);
                e.Handled = true;
            }
        }
    }
}
