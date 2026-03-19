namespace Elysium.WorkStation
{
    public partial class App : Application
    {
#if WINDOWS
        private readonly Services.IWebHostService _webHostService;
        private readonly Services.IMouseService _mouseService;

        public App(Services.IWebHostService webHostService, Services.IMouseService mouseService)
        {
            _webHostService = webHostService;
            _mouseService   = mouseService;
            InitializeComponent();
        }
#else
        public App()
        {
            InitializeComponent();
        }
#endif

        protected override Window CreateWindow(IActivationState activationState)
        {
            var window = new Window(new AppShell());

#if WINDOWS
            window.Created    += async (s, e) => await _webHostService.StartAsync();
            window.Created    += (s, e) => _mouseService.Start(1);
            window.Destroying += async (s, e) => await _webHostService.StopAsync();
            window.Destroying += (s, e) => _mouseService.Stop();
#endif

            return window;
        }
    }
}