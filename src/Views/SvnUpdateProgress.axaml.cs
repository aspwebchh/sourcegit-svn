using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SourceGit.Views
{
    public partial class SvnUpdateProgress : ChromelessWindow
    {
        public SvnUpdateProgress()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (DataContext is ViewModels.SvnUpdateProgress vm)
            {
                vm.Entries.CollectionChanged += OnEntriesChanged;
                await vm.StartAsync();
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (DataContext is ViewModels.SvnUpdateProgress { IsRunning: true } vm)
            {
                // Users must cancel the update explicitly. Other reasons (such as app shutdown) cancel it directly.
                if (e.CloseReason == WindowCloseReason.WindowClosing)
                {
                    e.Cancel = true;
                    return;
                }

                vm.Cancel();
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is ViewModels.SvnUpdateProgress vm)
                vm.Entries.CollectionChanged -= OnEntriesChanged;

            base.OnClosed(e);
        }

        private void OnEntriesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Merge scroll requests, since `svn update` may print lots of lines in a short time.
            if (_isScrollPending)
                return;

            _isScrollPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _isScrollPending = false;
                if (DataContext is ViewModels.SvnUpdateProgress { IsRunning: true } vm && vm.Entries.Count > 0)
                    EntriesList.ScrollIntoView(vm.Entries.Count - 1);
            }, DispatcherPriority.Background);
        }

        private void OnEntryContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is not Grid { DataContext: Models.SvnUpdateEntry entry } grid)
                return;

            var copy = new MenuItem();
            copy.Header = App.Text(entry.IsFile ? "CopyPath" : "Copy");
            copy.Icon = this.CreateMenuIcon("Icons.Copy");
            copy.Tag = OperatingSystem.IsMacOS() ? "⌘+C" : "Ctrl+C";
            copy.Click += async (_, ev) =>
            {
                await this.CopyTextAsync(entry.Path);
                ev.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Items.Add(copy);
            menu.Open(grid);

            e.Handled = true;
        }

        private async void OnEntriesKeyDown(object sender, KeyEventArgs e)
        {
            var cmdKey = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (e.Key == Key.C && e.KeyModifiers == cmdKey && EntriesList.SelectedItem is Models.SvnUpdateEntry entry)
            {
                e.Handled = true;
                await this.CopyTextAsync(entry.Path);
            }
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SvnUpdateProgress vm)
                vm.Cancel();

            e.Handled = true;
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            Close();
            e.Handled = true;
        }

        private bool _isScrollPending = false;
    }
}
