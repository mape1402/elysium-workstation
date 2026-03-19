namespace Elysium.WorkStation
{
    public partial class App : Application
    {
#if WINDOWS
        private readonly Services.IWebHostService _webHostService;

        public App(Services.IWebHostService webHostService)
        {
            _webHostService = webHostService;
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
            window.Destroying += async (s, e) => await _webHostService.StopAsync();
#endif

            return window;
        }
    }
}