using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SourceGit.Converters
{
    public static class SvnUpdateActionConverters
    {
        public static readonly FuncValueConverter<Models.SvnUpdateAction, IBrush> ToBrush =
            new(v =>
            {
                return v switch
                {
                    Models.SvnUpdateAction.Added => Brushes.LimeGreen,
                    Models.SvnUpdateAction.Deleted => Brushes.Tomato,
                    Models.SvnUpdateAction.Updated => Brushes.Goldenrod,
                    Models.SvnUpdateAction.Merged => Brushes.Goldenrod,
                    Models.SvnUpdateAction.Conflicted => Brushes.OrangeRed,
                    Models.SvnUpdateAction.Replaced => Brushes.Orchid,
                    Models.SvnUpdateAction.Existed => Brushes.SkyBlue,
                    Models.SvnUpdateAction.Restored => Brushes.SkyBlue,
                    Models.SvnUpdateAction.Error => Brushes.Red,
                    _ => Brushes.Gray,
                };
            });

        public static readonly FuncValueConverter<Models.SvnUpdateAction, string> ToName =
            new(v => v < Models.SvnUpdateAction.Message ? App.Text($"Svn.UpdateProgress.Action.{v}") : string.Empty);
    }
}
