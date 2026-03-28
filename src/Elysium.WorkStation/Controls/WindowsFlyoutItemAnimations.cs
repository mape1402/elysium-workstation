#if WINDOWS
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Elysium.WorkStation.Controls;

public sealed class WindowsFlyoutItemAnimations
{
    private const int RootRefreshThrottleMs = 250;

    private sealed class ItemState
    {
        public bool IsPointerInside;
        public ScaleTransform ScaleTransform;
        public PointerEventHandler PointerEnteredHandler;
        public PointerEventHandler PointerExitedHandler;
        public PointerEventHandler PointerPressedHandler;
        public PointerEventHandler PointerReleasedHandler;
        public RoutedEventHandler UnloadedHandler;
    }

    private sealed class RootState
    {
        public long LastRefreshTick;
        public RoutedEventHandler LoadedHandler;
        public EventHandler<object> LayoutUpdatedHandler;
        public RoutedEventHandler UnloadedHandler;
    }

    private readonly Dictionary<NavigationViewItem, ItemState> _states = new();
    private readonly Dictionary<FrameworkElement, RootState> _rootStates = new();

    public void AttachFromRoot(object content)
    {
        if (content is not DependencyObject root)
        {
            return;
        }

        AttachItems(root);

        if (root is FrameworkElement element)
        {
            AttachRootListeners(element);
        }
    }

    private void AttachItems(DependencyObject root)
    {
        foreach (var item in FindNavigationItems(root))
        {
            Attach(item);
        }
    }

    private void AttachRootListeners(FrameworkElement root)
    {
        if (_rootStates.ContainsKey(root))
        {
            return;
        }

        var state = new RootState();

        state.LoadedHandler = (_, _) =>
        {
            AttachItems(root);
        };

        state.LayoutUpdatedHandler = (_, _) =>
        {
            var now = Environment.TickCount64;
            if (now - state.LastRefreshTick < RootRefreshThrottleMs)
            {
                return;
            }

            state.LastRefreshTick = now;
            AttachItems(root);
        };

        state.UnloadedHandler = (_, _) =>
        {
            DetachRootListeners(root);
        };

        root.Loaded += state.LoadedHandler;
        root.LayoutUpdated += state.LayoutUpdatedHandler;
        root.Unloaded += state.UnloadedHandler;

        _rootStates[root] = state;
    }

    private void DetachRootListeners(FrameworkElement root)
    {
        if (!_rootStates.TryGetValue(root, out var state))
        {
            return;
        }

        root.Loaded -= state.LoadedHandler;
        root.LayoutUpdated -= state.LayoutUpdatedHandler;
        root.Unloaded -= state.UnloadedHandler;
        _rootStates.Remove(root);
    }

    private void Attach(NavigationViewItem item)
    {
        if (_states.ContainsKey(item))
        {
            return;
        }

        var scale = item.RenderTransform as ScaleTransform;
        if (scale is null)
        {
            scale = new ScaleTransform
            {
                ScaleX = 1,
                ScaleY = 1
            };
            item.RenderTransform = scale;
        }

        item.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

        var state = new ItemState
        {
            ScaleTransform = scale
        };

        state.PointerEnteredHandler = (_, _) =>
        {
            state.IsPointerInside = true;
            Animate(item, state.ScaleTransform, 1.03, 1.0, 120);
        };

        state.PointerExitedHandler = (_, _) =>
        {
            state.IsPointerInside = false;
            Animate(item, state.ScaleTransform, 1.0, 1.0, 120);
        };

        state.PointerPressedHandler = (_, _) =>
        {
            Animate(item, state.ScaleTransform, 0.96, 0.88, 80);
        };

        state.PointerReleasedHandler = (_, _) =>
        {
            var targetScale = state.IsPointerInside ? 1.03 : 1.0;
            Animate(item, state.ScaleTransform, targetScale, 1.0, 120);
        };

        state.UnloadedHandler = (_, _) =>
        {
            Detach(item);
        };

        item.PointerEntered += state.PointerEnteredHandler;
        item.PointerExited += state.PointerExitedHandler;
        item.PointerPressed += state.PointerPressedHandler;
        item.PointerReleased += state.PointerReleasedHandler;
        item.Unloaded += state.UnloadedHandler;

        _states[item] = state;
    }

    private void Detach(NavigationViewItem item)
    {
        if (!_states.TryGetValue(item, out var state))
        {
            return;
        }

        item.PointerEntered -= state.PointerEnteredHandler;
        item.PointerExited -= state.PointerExitedHandler;
        item.PointerPressed -= state.PointerPressedHandler;
        item.PointerReleased -= state.PointerReleasedHandler;
        item.Unloaded -= state.UnloadedHandler;
        _states.Remove(item);
    }

    private static IEnumerable<NavigationViewItem> FindNavigationItems(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is NavigationViewItem item)
            {
                yield return item;
            }

            foreach (var nested in FindNavigationItems(child))
            {
                yield return nested;
            }
        }
    }

    private static void Animate(UIElement item, ScaleTransform scaleTransform, double targetScale, double targetOpacity, int durationMs)
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var scaleX = new DoubleAnimation
        {
            To = targetScale,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing
        };
        Storyboard.SetTarget(scaleX, scaleTransform);
        Storyboard.SetTargetProperty(scaleX, nameof(ScaleTransform.ScaleX));

        var scaleY = new DoubleAnimation
        {
            To = targetScale,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing
        };
        Storyboard.SetTarget(scaleY, scaleTransform);
        Storyboard.SetTargetProperty(scaleY, nameof(ScaleTransform.ScaleY));

        var opacity = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing
        };
        Storyboard.SetTarget(opacity, item);
        Storyboard.SetTargetProperty(opacity, nameof(UIElement.Opacity));

        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Children.Add(opacity);
        storyboard.Begin();
    }
}
#endif
