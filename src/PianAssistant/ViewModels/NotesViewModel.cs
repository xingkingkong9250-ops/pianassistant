using System.Collections.ObjectModel;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.ViewModels;

/// <summary>
/// Timestamped notes for the current video. The note's position is
/// supplied by a <see cref="Func{T}"/> delegate so the VM has no direct
/// dependency on the player VM.
/// </summary>
public sealed class NotesViewModel : ObservableObject
{
    private readonly Func<TimeSpan> _getPosition;
    private string _newNoteText = string.Empty;
    private NoteEntry? _selectedNote;

    public ObservableCollection<NoteEntry> Items { get; } = new();

    public string NewNoteText
    {
        get => _newNoteText;
        set => SetProperty(ref _newNoteText, value);
    }

    public NoteEntry? SelectedNote
    {
        get => _selectedNote;
        set => SetProperty(ref _selectedNote, value);
    }

    public NotesViewModel(Func<TimeSpan> getPosition)
    {
        ArgumentNullException.ThrowIfNull(getPosition);
        _getPosition = getPosition;
    }

    /// <summary>
    /// Create a note at the current playback position with the text
    /// in <see cref="NewNoteText"/>, then clears the input.
    /// </summary>
    public void AddNote(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(_newNoteText)) return;
        var path = sourcePath ?? string.Empty;
        var note = new NoteEntry(path, _getPosition(), _newNoteText.Trim());
        Items.Add(note);
        NewNoteText = string.Empty;
    }

    /// <summary>Add a pre-built note (used by tests / import).</summary>
    public void Add(NoteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Items.Add(entry);
    }

    public void Remove(NoteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Items.Remove(entry);
    }

    /// <summary>Update an existing note's text (inline edit).</summary>
    public void Update(NoteEntry entry, string newText)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Text = newText ?? string.Empty;
        entry.UpdatedAt = DateTime.Now;
    }

    public void Clear()
    {
        Items.Clear();
        SelectedNote = null;
    }

    /// <summary>Jump the player to the note's position (Shell wires this to PlayerVM.Seek).</summary>
    public event EventHandler<TimeSpan>? SeekRequested;

    public void JumpTo(NoteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        SeekRequested?.Invoke(this, entry.Position);
    }
}
