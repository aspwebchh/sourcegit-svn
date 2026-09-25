using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class SvnWorkingCopy : UserControl
    {
        public SvnWorkingCopy()
        {
            InitializeComponent();
        }

        private void OnIncludeSelectedButtonClicked(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SvnWorkingCopy { SelectedChanges: { Count: > 0 } selection } vm)
                vm.Include(selection.Changes);

            e.Handled = true;
        }

        private void OnExcludeSelectedButtonClicked(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SvnWorkingCopy { SelectedIncluded: { Count: > 0 } selection } vm)
                vm.Exclude(selection.Changes);

            e.Handled = true;
        }

        private void OnChangeDoubleTapped(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SvnWorkingCopy { SelectedChanges: { Count: > 0 } selection } vm)
            {
                vm.Include(selection.Changes);
                ChangesView.TakeFocus();
                e.Handled = true;
            }
        }

        private void OnIncludedDoubleTapped(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SvnWorkingCopy { SelectedIncluded: { Count: > 0 } selection } vm)
            {
                vm.Exclude(selection.Changes);
                IncludedView.TakeFocus();
                e.Handled = true;
            }
        }

        private void OnChangesKeyDown(object _, KeyEventArgs e)
        {
            if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Space or Key.Enter &&
                DataContext is ViewModels.SvnWorkingCopy { SelectedChanges: { Count: > 0 } selection } vm)
            {
                vm.Include(selection.Changes);
                ChangesView.TakeFocus();
                e.Handled = true;
            }
        }

        private void OnIncludedKeyDown(object _, KeyEventArgs e)
        {
            if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Space or Key.Enter &&
                DataContext is ViewModels.SvnWorkingCopy { SelectedIncluded: { Count: > 0 } selection } vm)
            {
                vm.Exclude(selection.Changes);
                IncludedView.TakeFocus();
                e.Handled = true;
            }
        }

        private void OnChangesContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is ViewModels.SvnWorkingCopy { SelectedChanges: { Count: > 0 } selection } vm)
            {
                var menu = CreateContextMenu(vm, selection, true);
                menu.Open(sender as Control);
            }

            e.Handled = true;
        }

        private void OnIncludedContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is ViewModels.SvnWorkingCopy { SelectedIncluded: { Count: > 0 } selection } vm)
            {
                var menu = CreateContextMenu(vm, selection, false);
                menu.Open(sender as Control);
            }

            e.Handled = true;
        }

        private async void OnCommit(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SvnWorkingCopy vm)
                await vm.CommitAsync();

            e.Handled = true;
        }

        private ContextMenu CreateContextMenu(ViewModels.SvnWorkingCopy vm, ViewModels.ChangeSelection selection, bool isChangesList)
        {
            var repo = vm.Repository;
            var changes = selection.Changes;
            var menu = new ContextMenu();

            var hasUnversioned = false;
            var hasVersioned = false;
            var hasMissing = false;
            foreach (var c in changes)
            {
                if (c is Models.SvnChange { IsUnversioned: true })
                    hasUnversioned = true;
                else
                    hasVersioned = true;

                if (c is Models.SvnChange { IsMissing: true })
                    hasMissing = true;
            }

            if (isChangesList)
            {
                var include = new MenuItem();
                include.Header = App.Text("Svn.WorkingCopy.Include");
                include.Icon = this.CreateMenuIcon("Icons.Down");
                include.Click += (_, ev) =>
                {
                    vm.Include(changes);
                    ev.Handled = true;
                };
                menu.Items.Add(include);
            }
            else
            {
                var exclude = new MenuItem();
                exclude.Header = App.Text("Svn.WorkingCopy.Exclude");
                exclude.Icon = this.CreateMenuIcon("Icons.Up");
                exclude.Click += (_, ev) =>
                {
                    vm.Exclude(changes);
                    ev.Handled = true;
                };
                menu.Items.Add(exclude);
            }

            menu.Items.Add(new MenuItem() { Header = "-" });

            if (changes.Count == 1 || selection.IsSingleFolder)
            {
                var relative = selection.IsSingleFolder ? selection.SingleFolderPath : changes[0].Path;
                var fullpath = Native.OS.GetAbsPath(repo.FullPath, relative);

                if (!selection.HasFolder)
                {
                    var open = new MenuItem();
                    open.Header = App.Text("Open.SystemDefaultEditor");
                    open.Icon = this.CreateMenuIcon("Icons.OpenWith");
                    open.IsEnabled = File.Exists(fullpath);
                    open.Click += (_, ev) =>
                    {
                        Native.OS.OpenWithDefaultEditor(fullpath);
                        ev.Handled = true;
                    };
                    menu.Items.Add(open);
                }

                var explore = new MenuItem();
                explore.Header = App.Text("RevealFile");
                explore.Icon = this.CreateMenuIcon("Icons.Explore");
                explore.IsEnabled = Path.Exists(fullpath);
                explore.Click += (_, ev) =>
                {
                    Native.OS.OpenInFileManager(fullpath);
                    ev.Handled = true;
                };
                menu.Items.Add(explore);
                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            if (hasUnversioned)
            {
                var add = new MenuItem();
                add.Header = App.Text("Svn.WorkingCopy.Add");
                add.Icon = this.CreateMenuIcon("Icons.File.Add");
                add.Click += async (_, ev) =>
                {
                    ev.Handled = true;
                    await vm.AddAsync(changes);
                };
                menu.Items.Add(add);
            }

            if (hasMissing)
            {
                var delete = new MenuItem();
                delete.Header = App.Text("Svn.WorkingCopy.MarkAsDeleted");
                delete.Icon = this.CreateMenuIcon("Icons.File.Remove");
                delete.Click += async (_, ev) =>
                {
                    ev.Handled = true;
                    await vm.MarkAsDeletedAsync(changes);
                };
                menu.Items.Add(delete);
            }

            if (hasVersioned)
            {
                var revert = new MenuItem();
                revert.Header = App.Text("Svn.WorkingCopy.Revert");
                revert.Icon = this.CreateMenuIcon("Icons.Undo");
                revert.Click += (_, ev) =>
                {
                    vm.Revert(changes);
                    ev.Handled = true;
                };
                menu.Items.Add(revert);
            }

            if (hasUnversioned)
            {
                var deleteUnversioned = new MenuItem();
                deleteUnversioned.Header = App.Text("Svn.WorkingCopy.DeleteUnversioned");
                deleteUnversioned.Icon = this.CreateMenuIcon("Icons.Clean");
                deleteUnversioned.Click += (_, ev) =>
                {
                    vm.DeleteUnversioned(changes);
                    ev.Handled = true;
                };
                menu.Items.Add(deleteUnversioned);
            }

            menu.Items.Add(new MenuItem() { Header = "-" });

            var copy = new MenuItem();
            copy.Header = App.Text("CopyPath");
            copy.Icon = this.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await this.CopyTextAsync(JoinPaths(changes, null));
            };
            menu.Items.Add(copy);

            var copyFullPath = new MenuItem();
            copyFullPath.Header = App.Text("CopyFullPath");
            copyFullPath.Icon = this.CreateMenuIcon("Icons.Copy");
            copyFullPath.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await this.CopyTextAsync(JoinPaths(changes, repo.FullPath));
            };
            menu.Items.Add(copyFullPath);

            return menu;
        }

        private static string JoinPaths(List<Models.Change> changes, string root)
        {
            var paths = new List<string>();
            foreach (var c in changes)
                paths.Add(string.IsNullOrEmpty(root) ? c.Path : Native.OS.GetAbsPath(root, c.Path));

            return string.Join(Environment.NewLine, paths);
        }
    }
}
