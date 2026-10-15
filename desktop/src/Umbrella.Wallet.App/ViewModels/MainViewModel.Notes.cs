using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The user's own words on their history: a private note on a transaction, and a name for the address
/// on its other side.
///
/// Notes are sealed at rest with a seed-derived key (<see cref="TxNoteStore"/>) and keyed by transaction
/// id; names live in the address book, which is encrypted the same way. Neither ever touches the network.
///
/// They are written where they are read — in an editor that opens under the row itself — and a note can
/// be given to a transfer while it is being made, or on its receipt. The editor used to open at the top
/// of the page, out of sight of the row it was for, and only for transactions an explorer had already
/// listed: a transfer just made could not be written on at all.
/// </summary>
public partial class MainViewModel
{
    // The decrypted note map for the active wallet (txid -> note), loaded on unlock. Empty when locked.
    private Dictionary<string, string> _txNotes = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>The row being written on (its <see cref="ActivityRowViewModel.EditKey"/>), or empty.</summary>
    [ObservableProperty] private string _noteEditingKey = string.Empty;

    /// <summary>The transaction whose note is being edited; empty when that row has no id to key a note by.</summary>
    [ObservableProperty] private string _noteEditingTxId = string.Empty;

    /// <summary>The address being named, in full; empty when the row does not know it.</summary>
    private string _noteEditingAddress = string.Empty;

    /// <summary>The coin the row is about — what a newly named address is filed under.</summary>
    private string _noteEditingAsset = string.Empty;

    /// <summary>The note text in the inline editor.</summary>
    [ObservableProperty] private string _noteDraft = string.Empty;

    /// <summary>The name for the row's other side, in the inline editor.</summary>
    [ObservableProperty] private string _noteLabelDraft = string.Empty;

    public bool IsEditingNote => !string.IsNullOrEmpty(NoteEditingKey);

    partial void OnNoteEditingKeyChanged(string value) => OnPropertyChanged(nameof(IsEditingNote));

    private TxNoteStore NoteStore() => new(_registry.Active?.Id ?? "default");

    /// <summary>The saved note for a transaction, or null. Used when building activity rows.</summary>
    private string? TxNoteFor(string? txId) =>
        !string.IsNullOrWhiteSpace(txId) && _txNotes.TryGetValue(txId, out var note) ? note : null;

    /// <summary>Decrypts this wallet's notes into memory (called on unlock / before the feed is built).</summary>
    private async Task LoadTxNotesAsync()
    {
        if (_unlockedMnemonic is null) { _txNotes = new(System.StringComparer.OrdinalIgnoreCase); return; }
        var epoch = _lockEpoch;
        try
        {
            var loaded = await NoteStore().LoadAsync(_unlockedMnemonic);
            // Decrypted for the wallet that was open when this started. If it was locked or switched
            // meanwhile, these notes belong to another wallet: keeping them would show them there — and
            // the next note saved would write them into that wallet's encrypted file.
            if (epoch != _lockEpoch) return;
            _txNotes = new Dictionary<string, string>(loaded, System.StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            if (epoch == _lockEpoch) _txNotes = new(System.StringComparer.OrdinalIgnoreCase);
        }

        if (epoch == _lockEpoch) ApplyNotesToLocalRows();
    }

    /// <summary>
    /// The rows the wallet wrote itself are on screen before the notes are decrypted: once they are,
    /// each row whose transaction has a note is given it.
    /// </summary>
    private void ApplyNotesToLocalRows()
    {
        var changed = false;
        for (var i = 0; i < Activity.Count; i++)
        {
            var row = Activity[i];
            var note = TxNoteFor(row.TxId);
            if (note == row.Note) continue;
            Activity[i] = row with { Note = note };
            changed = true;
        }

        if (!changed) return;
        RebuildFilteredActivity();
        RebuildRecentActivity();
    }

    /// <summary>Opens the editor under a transaction row: its note, and the name of its other side.</summary>
    [RelayCommand]
    private void BeginEditNote(ActivityRowViewModel? row)
    {
        if (row is null || !row.CanAnnotate) return;

        // The same button closes it again.
        if (IsEditingNote && NoteEditingKey == row.EditKey) { CancelEditNote(); return; }

        NoteEditingTxId = row.CanHaveNote ? row.TxId! : string.Empty;
        _noteEditingAddress = row.CanLabel ? row.CounterpartyFull!.Trim() : string.Empty;
        _noteEditingAsset = row.Asset;
        NoteDraft = TxNoteFor(row.TxId) ?? string.Empty;
        NoteLabelDraft = _noteEditingAddress.Length > 0 && AddressNames().TryGetValue(_noteEditingAddress, out var name)
            ? name
            : string.Empty;
        NoteEditingKey = row.EditKey;
        RebuildAnnotatedFeeds();
    }

    [RelayCommand]
    private void CancelEditNote()
    {
        if (!IsEditingNote) return;
        NoteEditingKey = string.Empty;
        NoteEditingTxId = string.Empty;
        _noteEditingAddress = string.Empty;
        _noteEditingAsset = string.Empty;
        NoteDraft = string.Empty;
        NoteLabelDraft = string.Empty;
        RebuildAnnotatedFeeds();
    }

    /// <summary>
    /// Saves what the editor holds: the note (a blank one removes it) and the name (a blank one removes
    /// the address from the book). Nothing is fetched; the lists are redrawn from what is in memory.
    /// </summary>
    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (!IsEditingNote || _unlockedMnemonic is null) { CancelEditNote(); return; }

        if (NoteEditingTxId.Length > 0) await StoreNoteAsync(NoteEditingTxId, NoteDraft);
        if (_noteEditingAddress.Length > 0) NameAddress(_noteEditingAddress, _noteEditingAsset, NoteLabelDraft);

        ShowToast(Loc.Instance["notes.saved"], isError: false);
        CancelEditNote();   // closes the editor and redraws the rows with what was saved
    }

    /// <summary>Writes one note (blank removes it) into the encrypted store and onto the wallet's own rows.</summary>
    private async Task StoreNoteAsync(string txId, string? text)
    {
        if (string.IsNullOrWhiteSpace(txId) || _unlockedMnemonic is null) return;

        var note = (text ?? string.Empty).Trim();
        if (note.Length == 0) _txNotes.Remove(txId);
        else _txNotes[txId] = note;

        try { await NoteStore().SaveAsync(_txNotes, _unlockedMnemonic); }
        catch { /* non-fatal: the note just won't persist this time */ }

        ApplyNoteToFeeds(txId, note.Length == 0 ? null : note);
    }

    /// <summary>Gives an address a name in the address book — or, with a blank name, takes it out.</summary>
    private void NameAddress(string address, string asset, string? name)
    {
        var label = (name ?? string.Empty).Trim();
        var existed = _addressBookAll.RemoveAll(e =>
            string.Equals(e.Address?.Trim(), address, System.StringComparison.OrdinalIgnoreCase)) > 0;
        if (label.Length == 0 && !existed) return;
        if (label.Length > 0) _addressBookAll.Add(new AddressBookEntry(label, address, asset));
        SaveAddressBook();
        RebuildSendAddressBook();
        EvaluateSendSafety();   // a named address is a known one
    }

    /// <summary>Replaces the note on every copy of a transaction row (records are immutable, so we
    /// swap in <c>row with { Note = … }</c> rather than mutate).</summary>
    private void ApplyNoteToFeeds(string txId, string? note)
    {
        for (var i = 0; i < _onChainRows.Count; i++)
        {
            if (string.Equals(_onChainRows[i].TxId, txId, System.StringComparison.OrdinalIgnoreCase))
                _onChainRows[i] = _onChainRows[i] with { Note = note };
        }

        foreach (var feed in new[] { Activity, Transactions, FilteredActivity, RecentActivity, AssetActivity })
        {
            for (var i = 0; i < feed.Count; i++)
            {
                if (string.Equals(feed[i].TxId, txId, System.StringComparison.OrdinalIgnoreCase))
                    feed[i] = feed[i] with { Note = note };
            }
        }
    }

    /// <summary>The lists that show notes, names and the open editor, redrawn from memory.</summary>
    private void RebuildAnnotatedFeeds()
    {
        RebuildFilteredActivity();
        RebuildRecentActivity();
    }
}
