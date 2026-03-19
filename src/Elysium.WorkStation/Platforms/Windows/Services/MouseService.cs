namespace Elysium.WorkStation.Services
{
    using System.Runtime.InteropServices;

    public class MouseService : IMouseService
    {
        private bool _isRunning;
        private CancellationTokenSource _cts;

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

#pragma warning disable IDE1006 // Naming Styles
        const uint MOUSEEVENTF_MOVE = 0x0001;
#pragma warning restore IDE1006 // Naming Styles

        [StructLayout(LayoutKind.Sequential)]
        struct POINT       
        {
            public int X;
            public int Y;
        }

        public void Start(int intervalSeconds = 30)
        {
            if (_isRunning)
                return;

            _cts = new CancellationTokenSource();
            _isRunning = true;
            Task.Run(() => MoveMouse(TimeSpan.FromSeconds(intervalSeconds), _cts.Token));
        }

        public void Stop()
        {
            _cts.Cancel();
            _isRunning = false;
        }

        private async Task MoveMouse(TimeSpan interval, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (GetCursorPos(out POINT currentPos))
                {
                    // Mover el cursor ligeramente (alterna para que no sea obvio)
                    int newX = currentPos.X + 1;
                    int newY = currentPos.Y + 1;

                    // Establecer la nueva posición del cursor
                    SetCursorPos(newX, newY);

                    // Simular movimiento usando eventos de mouse
                    mouse_event(MOUSEEVENTF_MOVE, 0, 0, 0, UIntPtr.Zero);
                }

                await Task.Delay(interval);
            }
        }
    }
}
