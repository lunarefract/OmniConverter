using CSCore;
using System;

namespace OmniConverter
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

    public enum EngineID
    {
        Unknown = -1,
        BASS = 0,
        XSynth = 1,
        FluidSynth = 2,
        MAX = FluidSynth
    }

    public abstract class AudioEngine : IDisposable
    {
        protected bool _disposed = false;
        protected bool _init = false;
        protected WaveFormat _waveFormat;
        protected Settings _cachedSettings;

        public AudioEngine(Settings settings, bool defaultInit = true)
        {
            _cachedSettings = settings;
            _waveFormat = new(settings.Synth.SampleRate, 32, 2, AudioEncoding.IeeeFloat);
            _init = defaultInit;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public Settings GetCachedSettings() => _cachedSettings;
        public WaveFormat GetWaveFormat() => _waveFormat;
        public bool IsInitialized() => _init;

        protected abstract void Dispose(bool disposing);
    }

    public abstract class AudioRenderer : ISampleSource
    {
        protected Settings _cachedSettings;
        protected readonly object _lock = new object();
        protected long _streamLength = 0;

        public string UniqueID { get; protected set; } = IDGenerator.GetID();
        public bool CanSeek { get; protected set; } = false;
        public WaveFormat WaveFormat { get; protected set; }
        public bool Initialized { get; protected set; }
        public bool Disposed { get; protected set; } = false;
        public ulong ActiveVoices { get; protected set; } = 0;
        public float RenderingTime { get; protected set; } = 0.0f;

        public AudioRenderer(AudioEngine audioEngine, bool defaultInt = true) 
        { 
            WaveFormat = audioEngine.GetWaveFormat(); 
            _cachedSettings = audioEngine.GetCachedSettings(); 
            Initialized = defaultInt; 
        }

        public abstract void SystemReset();
        public abstract bool SendCustomFXEvents(int channel, short reverb, short chorus);
        public abstract void SendEvent(byte[] data);
        public abstract unsafe int Read(float[] buffer, int offset, long delta, int count);
        public abstract void RefreshInfo();
        public abstract void SendEndEvent();
        public virtual long Position { get { return _streamLength; } set { } }
        public virtual long Length { get { return _streamLength; } }

        public int Read(float[] buffer, int offset, int count) => Read(buffer, offset, 0, count);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected abstract void Dispose(bool disposing);
    }
}
