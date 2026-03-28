#if WINDOWS
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Elysium.WorkStation.Controls;

public static class GlobalButtonAnimations
{
    private sealed class State
    {
        public bool IsPointerInside;
        public PointerEventHandler PressedHandler;
        public PointerEventHandler ReleasedHandler;
        public PointerEventHandler PointerEnteredHandler;
        public PointerEventHandler PointerExitedHandler;
    }

    private static readonly Dictionary<Microsoft.UI.Xaml.Controls.Button, State> States = new();

    public static void Attach(Microsoft.UI.Xaml.Controls.Button platformButton, VisualElement element)
    {
        if (States.TryGetValue(platformButton, out var existing))
        {
            platformButton.PointerPressed -= existing.PressedHandler;
            platformButton.PointerReleased -= existing.ReleasedHandler;
            platformButton.PointerEntered -= existing.PointerEnteredHandler;
            platformButton.PointerExited -= existing.PointerExitedHandler;
            States.Remove(platformButton);
        }

        var state = new State();

        state.PressedHandler = async (_, _) =>
        {
            element.CancelAnimations();
            await Task.WhenAll(
                element.ScaleTo(0.95, 90, Easing.CubicOut),
                element.FadeTo(0.9, 90, Easing.CubicOut));
        };

        state.ReleasedHandler = async (_, _) =>
        {
            element.CancelAnimations();
            var targetScale = state.IsPointerInside ? 1.02 : 1;
            await Task.WhenAll(
                element.ScaleTo(targetScale, 120, Easing.CubicOut),
                element.FadeTo(1, 120, Easing.CubicOut));
        };

        state.PointerEnteredHandler = async (_, _) =>
        {
            state.IsPointerInside = true;
            element.CancelAnimations();
            await Task.WhenAll(
                element.ScaleTo(1.02, 120, Easing.CubicOut),
                element.FadeTo(1, 120, Easing.CubicOut));
        };

        state.PointerExitedHandler = async (_, _) =>
        {
            state.IsPointerInside = false;
            element.CancelAnimations();
            await Task.WhenAll(
                element.ScaleTo(1, 120, Easing.CubicOut),
                element.FadeTo(1, 120, Easing.CubicOut));
        };

        platformButton.PointerPressed += state.PressedHandler;
        platformButton.PointerReleased += state.ReleasedHandler;
        platformButton.PointerEntered += state.PointerEnteredHandler;
        platformButton.PointerExited += state.PointerExitedHandler;

        States[platformButton] = state;
    }
}
#endif
