namespace Elysium.WorkStation.Services
{
    public class ToastService : IToastService
    {
        private Border _toastBorder;
        private Label _toastLabel;
        private bool _isShowing;

        public async Task ShowAsync(string message, int durationMs = 2000)
        {
            if (_isShowing) return;

            if (Shell.Current?.CurrentPage is not ContentPage page
                || page.Content is not Layout rootLayout)
                return;

            _isShowing = true;

            try
            {
                EnsureToastCreated();
                _toastLabel.Text = message;
                _toastBorder.Opacity = 0;
                _toastBorder.IsVisible = true;

                if (!rootLayout.Children.Contains(_toastBorder))
                {
                    rootLayout.Children.Add(_toastBorder);

                    if (rootLayout is Grid grid)
                    {
                        int rows = Math.Max(grid.RowDefinitions.Count, 1);
                        int cols = Math.Max(grid.ColumnDefinitions.Count, 1);
                        Grid.SetRowSpan(_toastBorder, rows);
                        Grid.SetColumnSpan(_toastBorder, cols);
                    }
                }

                await _toastBorder.FadeTo(1, 200, Easing.CubicIn);
                await Task.Delay(durationMs);
                await _toastBorder.FadeTo(0, 300, Easing.CubicOut);

                _toastBorder.IsVisible = false;
                rootLayout.Children.Remove(_toastBorder);
            }
            finally
            {
                _isShowing = false;
            }
        }

        private void EnsureToastCreated()
        {
            _toastLabel ??= new Label
            {
                TextColor = Colors.White,
                FontFamily = "OpenSansSemibold",
                FontSize = 13,
                HorizontalTextAlignment = TextAlignment.Center
            };

            _toastBorder ??= new Border
            {
                IsVisible = false,
                Opacity = 0,
                BackgroundColor = Color.FromArgb("#E0333333"),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(18, 10),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(0, 0, 0, 24),
                Content = _toastLabel
            };
        }
    }
}
