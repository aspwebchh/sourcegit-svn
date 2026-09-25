using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SourceGit.Views
{
    public partial class SvnHistories : UserControl
    {
        public SvnHistories()
        {
            InitializeComponent();
        }

        private void OnRevisionContextRequested(object sender, ContextRequestedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ViewModels.SvnHistories { SelectedRevision: { } revision })
                return;

            var copyRevision = new MenuItem();
            copyRevision.Header = App.Text("Svn.Log.CopyRevision");
            copyRevision.Icon = this.CreateMenuIcon("Icons.Copy");
            copyRevision.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await this.CopyTextAsync(revision.Revision.ToString());
            };

            var copyMessage = new MenuItem();
            copyMessage.Header = App.Text("Svn.Log.CopyMessage");
            copyMessage.Icon = this.CreateMenuIcon("Icons.Copy");
            copyMessage.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await this.CopyTextAsync(revision.Message);
            };

            var menu = new ContextMenu();
            menu.Items.Add(copyRevision);
            menu.Items.Add(copyMessage);
            menu.Open(sender as Control);
        }

        private void OnChangeContextRequested(object sender, ContextRequestedEventArgs e)
        {
            e.Handled = true;

            if (sender is not ChangeCollectionView { Selection: { Count: > 0 } selection } view)
                return;

            if (DataContext is not ViewModels.SvnHistories { SelectedRevision: not null } vm)
                return;

            var changes = selection.Changes;
            var singleFile = changes.Count == 1 && !selection.HasFolder ? changes[0] as Models.SvnRevisionChange : null;
            var menu = new ContextMenu();

            if (singleFile != null)
            {
                var openWith = new MenuItem();
                openWith.Header = App.Text("Open");
                openWith.Icon = this.CreateMenuIcon("Icons.OpenWith");
                openWith.IsEnabled = !singleFile.IsDirectory && singleFile.WorkTree != Models.ChangeState.Deleted;
                if (openWith.IsEnabled)
                {
                    var defaultEditor = new MenuItem();
                    defaultEditor.Header = App.Text("Open.SystemDefaultEditor");
                    defaultEditor.Click += async (_, ev) =>
                    {
                        ev.Handled = true;
                        await vm.OpenRevisionFileAsync(singleFile, null);
                    };
                    openWith.Items.Add(defaultEditor);

                    var tools = Native.OS.ExternalTools;
                    if (tools.Count > 0)
                    {
                        openWith.Items.Add(new MenuItem() { Header = "-" });

                        foreach (var tool in tools)
                        {
                            var item = new MenuItem();
                            item.Header = tool.Name;
                            item.Icon = new Image { Width = 16, Height = 16, Source = tool.IconImage };
                            item.Click += async (_, ev) =>
                            {
                                ev.Handled = true;
                                await vm.OpenRevisionFileAsync(singleFile, tool);
                            };
                            openWith.Items.Add(item);
                        }
                    }
                }

                menu.Items.Add(openWith);
            }

            if (singleFile != null || selection.IsSingleFolder)
            {
                var localPath = vm.GetLocalPath(selection.IsSingleFolder ? selection.SingleFolderPath : singleFile.Path);
                var explore = new MenuItem();
                explore.Header = App.Text("RevealFile");
                explore.Icon = this.CreateMenuIcon("Icons.Explore");
                explore.IsEnabled = !string.IsNullOrEmpty(localPath) && Path.Exists(localPath);
                explore.Click += (_, ev) =>
                {
                    Native.OS.OpenInFileManager(localPath);
                    ev.Handled = true;
                };
                menu.Items.Add(explore);
                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            var patch = new MenuItem();
            patch.Header = App.Text("FileCM.SaveAsPatch");
            patch.Icon = this.CreateMenuIcon("Icons.Save");
            patch.Click += async (_, ev) =>
            {
                ev.Handled = true;

                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null)
                    return;

                var options = new FilePickerSaveOptions();
                options.Title = App.Text("FileCM.SaveAsPatch");
                options.DefaultExtension = ".patch";
                options.FileTypeChoices = [new FilePickerFileType("Patch File") { Patterns = ["*.patch"] }];

                try
                {
                    var storageFile = await storageProvider.SaveFilePickerAsync(options);
                    if (storageFile != null)
                        await vm.SaveChangesAsPatchAsync(changes, storageFile.Path.LocalPath);
                }
                catch (Exception exception)
                {
                    vm.Repository.SendNotification($"Failed to save as patch: {exception.Message}", true);
                }
            };
            menu.Items.Add(patch);
            menu.Items.Add(new MenuItem() { Header = "-" });

            // Paths shown in the list (or the selected folder), and the same paths relative to repository root.
            var displayPaths = new List<string>();
            if (selection.IsSingleFolder)
            {
                displayPaths.Add(selection.SingleFolderPath);
            }
            else
            {
                foreach (var c in changes)
                    displayPaths.Add(c.Path);
            }

            var localPaths = new List<string>();
            var urls = new List<string>();
            foreach (var p in displayPaths)
            {
                localPaths.Add(vm.GetLocalPath(p));
                urls.Add(vm.GetUrl(vm.GetRepositoryPath(p)));
            }

            var copyPath = new MenuItem();
            copyPath.Header = App.Text("CopyPath");
            copyPath.Icon = this.CreateMenuIcon("Icons.Copy");
            copyPath.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await this.CopyTextAsync(string.Join(Environment.NewLine, displayPaths));
            };
            menu.Items.Add(copyPath);

            var copyFullPath = new MenuItem();
            copyFullPath.Header = App.Text("CopyFullPath");
            copyFullPath.Icon = this.CreateMenuIcon("Icons.Copy");
            copyFullPath.IsEnabled = !localPaths.Contains(null);
            copyFullPath.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await this.CopyTextAsync(string.Join(Environment.NewLine, localPaths));
            };
            menu.Items.Add(copyFullPath);

            var copyUrl = new MenuItem();
            copyUrl.Header = App.Text("Svn.Log.CopyUrl");
            copyUrl.Icon = this.CreateMenuIcon("Icons.Copy");
            copyUrl.IsEnabled = !urls.Contains(string.Empty);
            copyUrl.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await this.CopyTextAsync(string.Join(Environment.NewLine, urls));
            };
            menu.Items.Add(copyUrl);

            menu.Open(view);
        }
    }
}
