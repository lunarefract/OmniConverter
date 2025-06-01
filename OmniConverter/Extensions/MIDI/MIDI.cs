using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using MIDIModificationFramework;
using MIDIModificationFramework.MIDIEvents;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OmniConverter
{
    public class EventTools
    {
        enum MIDIEventType
        {
            NoteOff = 0x80,
            NoteOn = 0x90,
            Aftertouch = 0xA0,
            CC = 0xB0,
            PatchChange = 0xC0,
            ChannelPressure = 0xD0,
            PitchBend = 0xE0,

            SystemMessageStart = 0xF0,
            SystemMessageEnd = 0xF7,

            MIDITCQF = 0xF1,
            SongPositionPointer = 0xF2,
            SongSelect = 0xF3,
            TuneRequest = 0xF6,
            TimingClock = 0xF8,
            Start = 0xFA,
            Continue = 0xFB,
            Stop = 0xFC,
            ActiveSensing = 0xFE,
            SystemReset = 0xFF,

            Unknown1 = 0xF4,
            Unknown2 = 0xF5,
            Unknown3 = 0xF9,
            Unknown4 = 0xFD
        };

        static uint GetStatus(uint ev) { return (ev & 0xFF); }
        static uint GetCommand(uint ev) { return (ev & 0xF0); }
        static uint GetChannel(uint ev) { return (ev & 0xF); }
        static uint GetFirstParam(uint ev) { return ((ev >> 8) & 0xFF); }
        static uint GetSecondParam(uint ev) { return ((ev >> 16) & 0xFF); }
    }

    public class MIDI : ObservableObject
    {
        public string Name { get => _name; }
        public long ID { get => _id; }
        public string Path { get => _path; }
        public TimeSpan Length { get => _timeLength; }
        public int Tracks { get => _tracks; }
        public long Notes { get => _noteCount; }
        public ulong Size { get => _fileSize; }
        public string HumanReadableSize { get => MiscFunctions.BytesToHumanReadableSize(_fileSize); }
        public MidiFile? LoadedFile { get => _loadedFile; }
        public ulong[] EventCountsMulti { get => _eventCountsMulti; }
        public double PPQ { get => _ppqn; }
        public ulong TotalEventCountSingle {
            get
            {
                ulong sum = 0;
                for (int i = 0; i <  _eventCountsSingle.Length; i++)
                    sum += _eventCountsSingle[i];
                return sum;
            }
        }
        public ulong TotalEventCountMulti
        {
            get
            {
                ulong sum = 0;
                for (int i = 0; i < _eventCountsMulti.Length; i++)
                    sum += _eventCountsMulti[i];
                return sum;
            }
        }
        public bool[] TrackHasNotes => _trackHasNotes;

        public string HumanReadableTime { get => MiscFunctions.TimeSpanToHumanReadableTime(_timeLength); }

        private MidiFile? _loadedFile;
        private IEnumerable<MIDIEvent>[] _metaEvent;
        private bool _disposed = false;
        private string _name;
        private long _id;
        private string _path;
        private TimeSpan _timeLength;
        private int _tracks;
        private long _noteCount;
        private ulong _fileSize;
        private ulong[] _eventCountsSingle;
        private ulong[] _eventCountsMulti;
        private double _ppqn;
        private bool[] _trackHasNotes;

        public MIDI(MidiFile? loadedFile, IEnumerable<MIDIEvent>[] metaEvent, string name, long id, string path, TimeSpan timeLength, int tracks, long noteCount, ulong fileSize, ulong[] eventCountsSingle, ulong[] eventCountsMulti, double ppqn, bool[] trackHasNotes)
        {
            _loadedFile = loadedFile;
            _metaEvent = metaEvent;
            _name = name;
            _id = id;
            _path = path;
            _timeLength = timeLength;
            _tracks = tracks;
            _noteCount = noteCount;
            _fileSize = fileSize;
            _eventCountsSingle = eventCountsSingle;
            _eventCountsMulti = eventCountsMulti;
            _ppqn = ppqn;
            _trackHasNotes = trackHasNotes;
        }

        public MIDI(string test)
        {
            _name = test;
        }

        public IEnumerable<MIDIEvent> GetFullMIDITimeBased() =>
            _loadedFile.IterateTracks().MergeAll().MakeTimeBased(_loadedFile.PPQ);

        public IEnumerable<IEnumerable<MIDIEvent>> GetIterateTracksTimeBased() =>
            _loadedFile.IterateTracks().Select((track, i) =>
            {
                // Try to pre-merge these events to avoid unnecessary allocations during conversion
                var before = _metaEvent[..i].MergeAll().ToArray().AsEnumerable();
                var after = _metaEvent[(i + 1)..].MergeAll().ToArray().AsEnumerable();

                return new[] { before, track, after }.MergeAll().MakeTimeBased(_loadedFile.PPQ);
            });

        public static (bool, IEnumerable<MIDIEvent>[]) GetMetaEvents(IEnumerable<IEnumerable<MIDIEvent>> tracks, ParallelOptions parallelOptions, ref double maxTicks, ref long noteCount, out ulong[] eventCountsSingle, out ulong[] eventCountsMulti, out bool[] trackHasNotes, Action<int, int> progressCallback)
        {
            object l = new object();

            bool corrupted = false;
            var trackCount = tracks.Count();
            var midiMetaEvents = new IEnumerable<MIDIEvent>[trackCount];
            int tracksParsed = 0;
            var metaEventCounts = new ulong[trackCount];
            long tNoteCount = 0;
            double tMaxTicks = 0;
            var tEventCountsSingle = new ulong[trackCount];
            var tEventCountsMulti = new ulong[trackCount];
            var tTrackHasNotes = new bool[trackCount];

            // loop over all tracks in parallel
            Parallel.For(trackCount, parallelOptions, T =>
            {
                double time = 0.0;
                int nc = 0;
                double delta = 0.0;
                var trackMetaEvents = new List<MIDIEvent>();
                ulong eventCount = 0;
                ulong metaEventCount = 0;

                try
                {
                    var track = tracks.ElementAt(T);
                    if (track != null)
                    {
                        foreach (var ev in track)
                        {
                            try
                            {
                                if (parallelOptions.CancellationToken.IsCancellationRequested)
                                    parallelOptions.CancellationToken.ThrowIfCancellationRequested();
                            }
                            catch { break; }

                            time += ev.DeltaTime;

                            switch (ev)
                            {
                                case NoteOnEvent fev:
                                    nc++;
                                    delta += ev.DeltaTime;
                                    eventCount++;
                                    break;

                                case TempoEvent tev:
                                case ControlChangeEvent ccev:
                                case ProgramChangeEvent pcev:
                                case ChannelPressureEvent cpev:
                                case SystemExclusiveMessageEvent sysexev:
                                case PolyphonicKeyPressureEvent pkpev:
                                    ev.DeltaTime += delta;
                                    delta = 0;
                                    trackMetaEvents.Add(ev);
                                    if (!(ev is TempoEvent))
                                        metaEventCount++;
                                    break;

                                default:
                                    delta += ev.DeltaTime;
                                    eventCount++;
                                    break;
                            }
                        }

                        midiMetaEvents[T] = trackMetaEvents;
                        tEventCountsSingle[T] = eventCount + metaEventCount;
                        tEventCountsMulti[T] = eventCount;
                        metaEventCounts[T] = metaEventCount;
                        tTrackHasNotes[T] = nc != 0;
                    }
                }
                catch (Exception e)
                {
                    Debug.PrintToConsole(Debug.LogType.Error, e.ToString());
                    corrupted = true;
                }

                Interlocked.Increment(ref tracksParsed);
                Interlocked.Add(ref tNoteCount, nc);
                lock (l)
                {
                    if (tMaxTicks < time) tMaxTicks = time;
                    progressCallback(tracksParsed, trackCount);
                }
            });

            ulong totalMeta = 0;
            for (int i = 0; i < trackCount; i++)
                totalMeta += metaEventCounts[i];
            for (int i = 0; i < trackCount; i++)
                tEventCountsMulti[i] += totalMeta;

            noteCount = tNoteCount;
            maxTicks = tMaxTicks;
            eventCountsSingle = tEventCountsSingle;
            eventCountsMulti = tEventCountsMulti;
            trackHasNotes = tTrackHasNotes;

            return (!corrupted, midiMetaEvents);
        }

        public static MIDI? Load(long id, string filepath, string name, ParallelOptions parallelOptions, Action<int, int> progressCallback)
        {
            var file = new MidiFile(filepath);
            ulong fileSize = (ulong)new FileInfo(filepath).Length;
            file.ZeroVelocityNoteOns = Program.Settings.Event.ZeroVelocityNoteOns;

            try
            {
                double maxTicks = 0;
                long noteCount = 0;

                var Tracks = file.IterateTracks();
                var getMetaRet = GetMetaEvents(Tracks, parallelOptions, ref maxTicks, ref noteCount, out ulong[] eventCountsSingle, out ulong[] eventCountsMulti, out bool[] trackHasNotes, progressCallback);

                var midiSuccess = getMetaRet.Item1;
                var midiMetaEvents = getMetaRet.Item2;

                var mergedMetaEvents = midiMetaEvents.MergeAll();

                // get midi length in seconds
                var mergedWithLength = mergedMetaEvents.MergeWith([new EndOfExclusiveEvent(maxTicks)]);
                double seconds = 0.0;
                foreach (var e in mergedWithLength.MakeTimeBased(file.PPQ))
                {
                    seconds += e.DeltaTime;
                }

                return new MIDI(file, midiMetaEvents, name, id, filepath, TimeSpan.FromSeconds(seconds), file.TrackCount, noteCount, fileSize, eventCountsSingle, eventCountsMulti, file.PPQ, trackHasNotes);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.PrintToConsole(Debug.LogType.Error, e.ToString());
            }

            file.Dispose();
            return null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing && _loadedFile != null)
                _loadedFile.Dispose();

            _metaEvent = Array.Empty<IEnumerable<MIDIEvent>>();
            _id = 0;
            _name = string.Empty;
            _path = string.Empty;
            _timeLength = TimeSpan.Zero;
            _tracks = 0;
            _noteCount = 0;
            _fileSize = 0;

            _disposed = true;
        }

        public static FilePickerFileType MidiAll { get; } = new("MIDIs")
        {
            Patterns = new[] { "*.mid", "*.midi", "*.mff", "*.smf", "*.kar" },
            AppleUniformTypeIdentifiers = new[] { "midi" },
            MimeTypes = new[] { "audio/midi" }
        };

        public void EnablePooling()
        {
            _loadedFile.Pooled = true;
        }
    }

    public class MIDIValidator
    {
        private string _currentMidi;
        private ulong _valid;
        private ulong _notvalid;
        private ulong _total;

        private ulong _midiEvents;
        private ulong _curMidiEvents;

        private List<ulong> _events;
        private ulong _processedEvents;

        private int _tracks;
        private int _curTrack;

        public MIDIValidator(ulong total)
        {
            _currentMidi = "";
            _valid = 0;
            _notvalid = 0;
            _midiEvents = 0;
            _curMidiEvents = 0;
            _events = new();
            _processedEvents = 0;
            _tracks = 0;
            _curTrack = 0;
            _total = total;
        }

        public void SetCurrentMIDI(string midi) { _currentMidi = midi; }
        public string GetCurrentMIDI() { return _currentMidi; }
        public void AddValidMIDI() { _valid++; }
        public void AddInvalidMIDI() { _notvalid++; }
        public ulong GetValidMIDIs() { return _valid; }
        public ulong GetInvalidMIDIs() { return _notvalid; }
        public ulong GetTotalMIDIs() { return _total; }

        public void SetTotalEventsCount(List<ulong> events) { _events = events; }
        public ulong AddEvent() { return Interlocked.Increment(ref _processedEvents); }
        public ulong AddEvents(ulong events) { return Interlocked.Add(ref _processedEvents, events); }
        public ulong GetTotalEvents() {
            // No LINQ sum for ulong[]...
            ulong sum = 0;
            for (int i = 0; i < _events.Count; i++)
                sum += _events[i];
            return sum;
        }
        public ulong GetProcessedEvents() { return _processedEvents; }

        public void SetTotalMIDIEvents(ulong events) { _midiEvents = events; _curMidiEvents = 0; }
        public ulong AddMIDIEvent() { return Interlocked.Increment(ref _curMidiEvents); }
        public ulong AddMIDIEvents(ulong events) { return Interlocked.Add(ref _curMidiEvents, events); }
        public ulong GetTotalMIDIEvents() { return _midiEvents; }
        public ulong GetProcessedMIDIEvents() { return _curMidiEvents; }

        public void SetTotalTracks(int tracks) { _tracks = tracks; _curTrack = 0; }
        public void AddTrack() { _curTrack++; }
        public int GetTotalTracks() { return _tracks; }
        public int GetCurrentTrack() { return _curTrack; }
    }

    public abstract class MIDIWorker : IDisposable
    {
        public abstract void Dispose();
        public abstract string GetCustomTitle();
        public abstract string GetStatus();
        public abstract double GetProgress();

        public abstract bool IsRunning();
        public abstract void TogglePause(bool toggle);

        public abstract bool StartWork();
        public abstract void RestoreWork();
        public abstract void CancelWork();
    }
}
