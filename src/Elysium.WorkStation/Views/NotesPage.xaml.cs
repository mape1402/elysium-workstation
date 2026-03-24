using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Views
{
    public partial class NotesPage : ContentPage
    {
        private readonly INoteRepository _noteRepository;

        public ObservableCollection<NoteEntry> Notes { get; } = [];

        public string CountText => Notes.Count == 0
            ? "Sin notas"
            : $"{Notes.Count} nota{(Notes.Count == 1 ? "" : "s")}";

        public Command AddCommand { get; }
        public Command ClearCommand { get; }
        public Command<NoteEntry> DeleteCommand { get; }
        public Command<NoteEntry> EditCommand { get; }
        public Command<NoteEntry> CopyCommand { get; }

        public NotesPage(INoteRepository noteRepository)
        {
            _noteRepository = noteRepository;

            AddCommand = new Command(async () => await Shell.Current.GoToAsync("note-editor"));
            ClearCommand = new Command(async () => await ClearNotesAsync());
            DeleteCommand = new Command<NoteEntry>(async (note) => await DeleteNoteAsync(note));
            EditCommand = new Command<NoteEntry>(async (note) =>
            {
                if (note is not null)
                    await Shell.Current.GoToAsync($"note-editor?id={note.Id}");
            });
            CopyCommand = new Command<NoteEntry>(async (note) =>
            {
                if (note is null) return;
                await Clipboard.Default.SetTextAsync(note.Text);
                await ShowToastAsync("📋 Copiado al portapapeles");
            });

            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadNotesAsync();
        }

        private async Task LoadNotesAsync()
        {
            var entries = await _noteRepository.GetAllAsync();
            Notes.Clear();
            foreach (var entry in entries)
                Notes.Add(entry);
            OnPropertyChanged(nameof(CountText));
        }

        private async Task DeleteNoteAsync(NoteEntry note)
        {
            if (note is null) return;

            bool confirm = await DisplayAlert("Eliminar nota", $"¿Eliminar \"{note.Title}\"?", "Sí", "No");
            if (!confirm) return;

            await _noteRepository.DeleteAsync(note.Id);
            Notes.Remove(note);
            OnPropertyChanged(nameof(CountText));
        }

        private async Task ClearNotesAsync()
        {
            if (Notes.Count == 0) return;

            bool confirm = await DisplayAlert("Confirmar", "¿Eliminar todas las notas?", "Sí", "No");
            if (!confirm) return;

            await _noteRepository.DeleteAllAsync();
            Notes.Clear();
            OnPropertyChanged(nameof(CountText));
        }

        private async Task ShowToastAsync(string message, int durationMs = 2000)
        {
            ToastLabel.Text = message;
            ToastBorder.IsVisible = true;
            await ToastBorder.FadeTo(1, 200, Easing.CubicIn);
            await Task.Delay(durationMs);
            await ToastBorder.FadeTo(0, 300, Easing.CubicOut);
            ToastBorder.IsVisible = false;
        }
    }
}
