using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation.Views
{
    [QueryProperty(nameof(EditId), "id")]
    public partial class NoteEditorPage : ContentPage
    {
        private readonly INoteRepository _noteRepository;
        private int? _editId;

        public List<string> AvailableColors { get; } = [.. NoteEntry.AvailableColors];

        private string _noteTitle = string.Empty;
        public string NoteTitle
        {
            get => _noteTitle;
            set { _noteTitle = value; OnPropertyChanged(); }
        }

        private string _noteText = string.Empty;
        public string NoteText
        {
            get => _noteText;
            set { _noteText = value; OnPropertyChanged(); }
        }

        private string _selectedColorHex;
        public Color SelectedColor => Color.FromArgb(_selectedColorHex);

        public string EditId
        {
            set
            {
                if (int.TryParse(value, out int id))
                    _editId = id;
            }
        }

        public Command<string> SelectColorCommand { get; }
        public Command SaveCommand { get; }
        public Command CancelCommand { get; }

        public NoteEditorPage(INoteRepository noteRepository)
        {
            _noteRepository = noteRepository;
            _selectedColorHex = NoteEntry.RandomColor();

            SelectColorCommand = new Command<string>(hex =>
            {
                _selectedColorHex = hex;
                OnPropertyChanged(nameof(SelectedColor));
            });

            SaveCommand = new Command(async () => await SaveAsync());
            CancelCommand = new Command(async () => await Shell.Current.GoToAsync(".."));

            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_editId is int id)
            {
                var all = await _noteRepository.GetAllAsync();
                var existing = all.FirstOrDefault(n => n.Id == id);
                if (existing is not null)
                {
                    NoteTitle = existing.Title;
                    NoteText = existing.Text;
                    _selectedColorHex = existing.ColorHex;
                    OnPropertyChanged(nameof(SelectedColor));
                    Title = "Editar nota";
                }
            }
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(NoteTitle))
            {
                await DisplayAlert("Nota", "El título no puede estar vacío.", "OK");
                return;
            }

            if (_editId is int id)
            {
                var entry = new NoteEntry
                {
                    Id = id,
                    Title = NoteTitle.Trim(),
                    Text = NoteText.Trim(),
                    ColorHex = _selectedColorHex,
                    Timestamp = DateTime.Now
                };
                await _noteRepository.UpdateAsync(entry);
            }
            else
            {
                var entry = new NoteEntry
                {
                    Title = NoteTitle.Trim(),
                    Text = NoteText.Trim(),
                    ColorHex = _selectedColorHex,
                    Timestamp = DateTime.Now
                };
                await _noteRepository.SaveAsync(entry);
            }

            await Shell.Current.GoToAsync("..");
        }
    }
}
