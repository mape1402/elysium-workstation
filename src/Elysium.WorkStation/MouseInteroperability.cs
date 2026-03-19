namespace Elysium.WorkStation
{
    using System;
    using System.Runtime.InteropServices;

    public static class MouseInteroperability
    {
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

#pragma warning disable IDE1006 // Naming Styles
        public const uint MOUSEEVENTF_MOVE = 0x0001;
#pragma warning restore IDE1006 // Naming Styles

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
