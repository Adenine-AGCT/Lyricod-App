using System.ComponentModel;
using Lyricod.Core;

namespace Lyricod;

public sealed partial class PlaybackModel
{
    /// <summary>Searches again for this song, skipping the saved lyrics.</summary>
    public void SearchAgain()
    {
        if (Track is not { } t || !AutomaticLookupAllowed) return;
        _provider.ClearCache(t);
        LoadLyrics(t, ignoreCache: true);
    }

    /// <summary>Searches by hand without changing the lyrics in use.</summary>
    public async Task<List<LyricsCandidate>> LyricsCandidates(string query)
    {
        if (Track is not { } track) return [];
        LyricsSource[] preferred = PlayerName?.Contains("QQ", StringComparison.OrdinalIgnoreCase) == true
            ? [LyricsSource.Qq, LyricsSource.Netease, LyricsSource.Lrclib]
            : [LyricsSource.Netease, LyricsSource.Qq, LyricsSource.Lrclib];
        try { return await _provider.Candidates(query, track, preferred); }
        catch (Exception e) { LastError = "candidates: " + e.Message; return []; }
    }

    /// <summary>Previews the source's lyrics without changing playback or the cache.</summary>
    public async Task<Lyrics?> PreviewLyrics(LyricsRef reference)
    {
        try { return (await _provider.Fetch(reference))?.Parse(); }
        catch (Exception e) { LastError = "preview: " + e.Message; return null; }
    }

    public bool TryPinLyrics(LyricsRef reference)
    {
        if (Track is not { } track) return false;
        if (!TrySaveSongChoice(track, choice => choice.Lyrics = reference)) return false;
        LoadLyrics(track, ignoreCache: true);
        return true;
    }

    /// <summary>Searches afresh only after the manual choice has been removed successfully.</summary>
    public bool TryUnpinLyrics()
    {
        if (Track is not { } track) return false;
        if (!TrySaveSongChoice(track, choice => choice.Lyrics = null)) return false;
        _provider.ClearCache(track);
        LoadLyrics(track, ignoreCache: true);
        return true;
    }

    bool TrySaveSongChoice(Track track, Action<TrackOverride> edit)
    {
        if (!_overrides.TryGet(track, out var choice)) return false;
        edit(choice);
        return _overrides.TrySet(track, choice);
    }

    public void ClearSavedLyrics() => _provider.ClearAll();

    void LoadLyrics(Track track, bool ignoreCache)
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        _allLyrics = null;
        Lyrics = null;
        CurrentIndex = null;
        BestMatchScore = null;
        var search = new LyricsSearch(IsMusicPlayer, _settings.LookUpOtherPlayers);
        var pinned = _overrides[track].Lyrics;
        IsSearching = search.AutomaticLookupAllowed || pinned != null;
        Changed?.Invoke(Change.Lyrics | Change.Line);
        _ = Task.Run(async () =>
        {
            Lyrics? found = null, early = null;
            try
            {
                found = await _provider.GetLyrics(track, ignoreCache, pinned, cts.Token, cached =>
                {
                    early = cached;
                    try { _romanizer.Fill(cached); }
                    catch (Exception e) { LastError = "pronunciation: " + e.Message; }
                    var score = search.BestScore;
                    Post(() =>
                    {
                        if (cts.IsCancellationRequested || Track?.TrackId != track.TrackId) return;
                        IsSearching = false;
                        BestMatchScore = score;
                        _allLyrics = cached;
                        ApplyDisplayFilters();
                    });
                }, search);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { LastError = "lyrics: " + e.Message; }
            // The saved copy is already showing, and the check found nothing better (or failed).
            if (early != null && (found == null || ReferenceEquals(found, early)))
            {
                Post(() =>
                {
                    if (cts.IsCancellationRequested || Track?.TrackId != track.TrackId) return;
                    if (BestMatchScore == search.BestScore) return;
                    BestMatchScore = search.BestScore;
                    Changed?.Invoke(Change.Diagnostics);
                });
                return;
            }
            // Pronunciation is extra: if working it out fails, the lyrics still show.
            if (found != null)
            {
                try { _romanizer.Fill(found); } // the analysers take a few ms a line
                catch (Exception e) { LastError = "pronunciation: " + e.GetType().Name + ": " + e.Message; }
            }
            Post(() =>
            {
                if (cts.IsCancellationRequested || Track?.TrackId != track.TrackId) return;
                IsSearching = false;
                BestMatchScore = search.BestScore;
                _allLyrics = found;
                ApplyDisplayFilters();
            });
        });
    }

    /// <summary>Builds <see cref="Lyrics"/> from what the source returned, per the settings.</summary>
    void ApplyDisplayFilters()
    {
        if (_allLyrics is not { } all) { Lyrics = null; CurrentIndex = null; Changed?.Invoke(Change.Lyrics | Change.Line); return; }
        var languages = _settings.PronunciationLanguages.ToHashSet();
        var lines = all.Lines.Where(l => _settings.ShowCredits || !l.IsCredit)
            .Select((l, i) => l with
            {
                Id = i, // ids are row indices
                Pronunciation = l.Pronunciation is { Language: { } lang } p && languages.Contains(lang) ? p : null,
            }).ToList();
        Lyrics = lines.Count == 0 ? null : new Lyrics(lines, all.Source, all.IsSynced) { Ref = all.Ref };
        CurrentIndex = null;
        Changed?.Invoke(Change.Lyrics);
        Tick();
    }

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            switch (e.PropertyName)
            {
                case nameof(Settings.ShowCredits) or nameof(Settings.PronunciationLanguages):
                    ApplyDisplayFilters();
                    break;
                case nameof(Settings.LookUpOtherPlayers):
                    if (Track is { } track) LoadLyrics(track, ignoreCache: false);
                    break;
                case nameof(Settings.Offset):
                    ApplyOffset();
                    break;
                case nameof(Settings.PlayerChoice):
                    _media.Choice = _settings.PlayerChoice;
                    break;
            }
        }
        catch (Exception error) { LastError = "settings: " + error.Message; }
    }
}
