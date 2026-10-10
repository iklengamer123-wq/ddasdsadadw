using System;
using Godot;
using CustomRhythia.Map;

namespace CustomRhythia.Game;

/// <summary>
/// Game loop: advances progress, starts the song when gameplay begins,
/// corrects drift gently, resolves note hits.
///
/// A LEAD_IN_SECONDS countdown runs before the map advances, so the player
/// has time to get ready. Health: 5 HP max, hit = +1, miss = -1, fail at 0.
/// </summary>
public partial class Runner : Node
{
    [Signal] public delegate void StatsUpdatedEventHandler();
    [Signal] public delegate void AttemptFinishedEventHandler();

    public Attempt Attempt { get; private set; }
    public bool IsPlaying => Attempt != null && Attempt.Playing;
    public bool IsCountingDown => _countingDown;
    public double CountdownRemaining => _countdownRemaining;

    private AudioStreamPlayer _song;
    private AudioStreamPlayer[] _hitSounds = Array.Empty<AudioStreamPlayer>();
    private int _hitSoundIndex = 0;

    private ulong _lastFrameUs;
    private bool _firstFrame = true;

    private bool _songStarted = false;
    private ulong _songStartedUs = 0;
    private const ulong SongSettleUs = 700_000;

    private bool _countingDown = true;
    private double _countdownRemaining = Constants.LEAD_IN_SECONDS;

    private int _scanStart = 0;
    private int _scanEnd = 0;

    private double _cursorX;
    private double _cursorY;

    public void Setup(Attempt attempt, AudioStreamPlayer song, AudioStreamPlayer[] hitSounds)
    {
        Attempt = attempt;
        _song = song;
        _hitSounds = hitSounds ?? Array.Empty<AudioStreamPlayer>();
        _hitSoundIndex = 0;

        _lastFrameUs = Time.GetTicksUsec();
        _firstFrame = true;
        _songStarted = false;
        _songStartedUs = 0;

        _countingDown = true;
        _countdownRemaining = Constants.LEAD_IN_SECONDS;

        if (attempt != null && attempt.Map != null && attempt.Map.Notes != null)
        {
            for (int i = 0; i < attempt.Map.Notes.Length; i++)
            {
                attempt.Map.Notes[i].LastResult = HitResult.None;
                attempt.Map.Notes[i].HitOffset = 0f;
            }
        }

        _scanStart = 0;
        _scanEnd = 0;

        if (attempt != null && attempt.StartFrom > 0 && attempt.Map != null && attempt.Map.Notes != null)
        {
            for (int i = 0; i < attempt.Map.Notes.Length; i++)
            {
                if (attempt.Map.Notes[i].Millisecond >= attempt.StartFrom - 500)
                {
                    _scanStart = i;
                    _scanEnd = i;
                    break;
                }
            }
        }
    }

    public void NotifyResumed()
    {
        _lastFrameUs = Time.GetTicksUsec();
        _firstFrame = true;
        _songStartedUs = Time.GetTicksUsec();
    }

    public void SetCursor(double x, double y)
    {
        _cursorX = x;
        _cursorY = y;
    }

    public override void _Process(double delta)
    {
        if (Attempt == null || !Attempt.Playing || Attempt.Stopped) return;

        ulong now = Time.GetTicksUsec();
        if (_firstFrame)
        {
            _lastFrameUs = now;
            _firstFrame = false;
            return;
        }

        double dt = (now - _lastFrameUs) / 1000000.0;
        _lastFrameUs = now;

        if (dt > 0.1) dt = 0.1;

        // Countdown phase: freeze gameplay, don't advance Progress, don't
        // start the song. The HUD reads IsCountingDown / CountdownRemaining
        // to show a big centered number.
        if (_countingDown)
        {
            _countdownRemaining -= dt;
            if (_countdownRemaining <= 0)
            {
                _countingDown = false;
                _countdownRemaining = 0;
                // Push the frame clock forward so the first real frame after
                // the countdown doesn't compute a giant dt.
                _lastFrameUs = now;
            }
            return;
        }

        Attempt.Progress = Attempt.Progress + dt * 1000.0 * Attempt.Speed;

        StartSongIfNeeded(now);
        UpdateAudioSync(now);
        ResolveNotes();

        if (Attempt.Progress >= Attempt.Length && !Attempt.Stopped)
        {
            Attempt.Stopped = true;
            Attempt.Playing = false;
            EmitSignal(SignalName.AttemptFinished);
        }
    }

    private void StartSongIfNeeded(ulong now)
    {
        if (_songStarted) return;
        if (_song == null || _song.Stream == null) return;

        // With the countdown already done, the song should start as soon as
        // the lead-in window is up — i.e. Progress reaches StartFrom.
        if (Attempt.Progress < Attempt.StartFrom) return;

        _song.Play();
        if (Attempt.StartFrom > 0.001)
        {
            _song.Seek((float)(Attempt.StartFrom / 1000.0));
        }
        _song.PitchScale = (float)Attempt.Speed;

        _songStarted = true;
        _songStartedUs = now;
    }

    private void UpdateAudioSync(ulong now)
    {
        if (!_songStarted || _song == null) return;
        if (_song.StreamPaused) return;
        if (!_song.Playing) return;
        if (now - _songStartedUs < SongSettleUs) return;

        double expected = Attempt.Progress;
        double actual = 1000.0 * _song.GetPlaybackPosition();
        double offset = expected - actual;

        if (Math.Abs(offset) > 300.0)
        {
            _song.Seek((float)(expected / 1000.0));
            _song.PitchScale = (float)Attempt.Speed;
            _songStartedUs = now;
            return;
        }

        if (Math.Abs(offset) > 40.0)
        {
            double target = Attempt.Speed + offset / 5000.0;
            double lo = Attempt.Speed * 0.98;
            double hi = Attempt.Speed * 1.02;
            if (target < lo) target = lo;
            if (target > hi) target = hi;
            _song.PitchScale = (float)target;
        }
        else
        {
            _song.PitchScale = (float)Attempt.Speed;
        }
    }

    private void ResolveNotes()
    {
        var notes = Attempt.Map.Notes;
        if (notes == null || notes.Length == 0) return;

        while (_scanStart < notes.Length && notes[_scanStart].LastResult != HitResult.None)
        {
            _scanStart = _scanStart + 1;
        }

        double hitWindow = Constants.HIT_WINDOW * Attempt.Speed;

        if (_scanEnd < _scanStart) _scanEnd = _scanStart;
        double lookahead = hitWindow + 64.0;
        while (_scanEnd < notes.Length && notes[_scanEnd].Millisecond <= Attempt.Progress + lookahead)
        {
            _scanEnd = _scanEnd + 1;
        }

        for (int i = _scanStart; i < _scanEnd; i++)
        {
            Note note = notes[i];
            if (note.LastResult != HitResult.None) continue;

            double dt = Attempt.Progress - note.Millisecond;
            if (dt < 0) continue;

            bool cursorInside = note.IsCursorInside((float)_cursorX, (float)_cursorY);

            if (cursorInside)
            {
                RegisterHit(note);
            }
            else if (dt > hitWindow)
            {
                RegisterMiss(note);
            }
        }
    }

    public void Stop()
    {
        if (Attempt == null || Attempt.Stopped) return;
        Attempt.Stopped = true;
        Attempt.Playing = false;
        EmitSignal(SignalName.AttemptFinished);
    }

    public void Skip()
    {
        if (Attempt == null) return;

        if (Attempt.Progress >= Attempt.Length - 2000)
        {
            Stop();
            return;
        }

        var notes = Attempt.Map.Notes;
        double target = Attempt.Progress + 4000;

        for (int i = _scanStart; i < notes.Length; i++)
        {
            Note n = notes[i];
            if (n.LastResult != HitResult.None) continue;
            if (n.Millisecond > Attempt.Progress)
            {
                double candidate = n.Millisecond - 1000.0 * Attempt.Speed;
                double minTarget = Attempt.Progress + 1000;
                target = candidate > minTarget ? candidate : minTarget;
                break;
            }
        }

        Attempt.Progress = target;
        _scanStart = 0;
        _scanEnd = 0;

        if (_song != null && _songStarted && !_song.StreamPaused)
        {
            _song.Seek((float)(Attempt.Progress / 1000.0));
            _songStartedUs = Time.GetTicksUsec();
        }
    }

    private void RegisterHit(Note note)
    {
        note.LastResult = HitResult.Hit;
        note.HitOffset = (float)(Attempt.Progress - note.Millisecond);

        Attempt.Hits = Attempt.Hits + 1;
        Attempt.Sum = Attempt.Sum + 1;
        Attempt.Combo = Attempt.Combo + 1;
        Attempt.ComboMultiplierProgress = Attempt.ComboMultiplierProgress + 1;

        if (Attempt.Combo > Attempt.MaxCombo) Attempt.MaxCombo = Attempt.Combo;

        if (Attempt.ComboMultiplierProgress >= Attempt.ComboMultiplierIncrement
            && Attempt.ComboMultiplier < 8)
        {
            Attempt.ComboMultiplierProgress = 0;
            Attempt.ComboMultiplier = Attempt.ComboMultiplier + 1;
        }

        double speedFactor = (Attempt.Speed - 1) / 2.5 + 1;
        uint score = (uint)(100 * Attempt.ComboMultiplier * speedFactor);
        Attempt.Score = Attempt.Score + score;

        double healed = Attempt.Health + 1;
        Attempt.Health = healed > Attempt.MaxHealth ? Attempt.MaxHealth : healed;

        PlayHitSound();

        EmitSignal(SignalName.StatsUpdated);
    }

    private void PlayHitSound()
    {
        if (_hitSounds.Length == 0) return;
        if (_hitSounds[0] == null || _hitSounds[0].Stream == null) return;

        var player = _hitSounds[_hitSoundIndex];
        _hitSoundIndex = (_hitSoundIndex + 1) % _hitSounds.Length;
        if (player != null) player.Play();
    }

    private void RegisterMiss(Note note)
    {
        note.LastResult = HitResult.Miss;
        note.HitOffset = -1;

        Attempt.Misses = Attempt.Misses + 1;
        Attempt.Sum = Attempt.Sum + 1;
        Attempt.Combo = 0;
        Attempt.ComboMultiplierProgress = 0;

        uint mult = Attempt.ComboMultiplier;
        if (mult > 1) mult = mult - 1;
        Attempt.ComboMultiplier = mult;

        double dmg = Attempt.Health - 1;
        Attempt.Health = dmg > 0 ? dmg : 0;

        if (Attempt.Health <= 0 && Attempt.Alive)
        {
            Attempt.Alive = false;
            Attempt.Qualifies = false;
            Attempt.Playing = false;
            Attempt.Stopped = true;
            EmitSignal(SignalName.AttemptFinished);
        }

        EmitSignal(SignalName.StatsUpdated);
    }
}
