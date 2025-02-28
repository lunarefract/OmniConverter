using CSCore;
using System;

namespace OmniConverter
{
    enum EventType : int
    {
        NoteOff = 0x80,
        NoteOn = 0x90,
        KeyPressure = 0xA0,
        Controller = 0xB0,
        ProgramChange = 0xC0,
        ChannelPressure = 0xD0,
        PitchBend = 0xE0,

        SystemExclusive = 0xF0,
        EOX = 0xF7,

        MIDITCQF = 0xF1,
        SongPositionPointer = 0xF2,
        SongSelect = 0xF3,
        TuneRequest = 0xF6,
        TimingClock = 0xF8,
        Start = 0xFA,
        Continue = 0xFB,
        Stop = 0xFC,
        ActiveSensing = 0xFE,
        MetaEvent = 0xFF,
        SystemReset = MetaEvent,

        Unknown1 = 0xF4,
        Unknown2 = 0xF5,
        Unknown3 = 0xF9,
        Unknown4 = 0xFD
    };

    enum ControllerType : int
    {
        BankSelect = 0x0,
        ModulationWheel = 0x1,
        BreathController = 0x2,
        FootPedal = 0x4,
        PortamentoTime = 0x5,
        DataEntry = 0x6,
        Volume = 0x7,
        Balance = 0x8,
        Pan = 0xA,
        Expression = 0xB,
        EffectCtrl1 = 0xC,
        EffectCtrl2 = 0xD,
        GenPurposeMask = 0x10,
        LSBCtrl0Mask = 0x20,
        Damper = 0x40,
        Portamento = 0x41,
        Sostenuto = 0x42,
        SoftPedal = 0x43,
        Legato = 0x44,
        Hold2 = 0x45,
        SoundCtrl = 0x46,
        SoundCtrl2 = 0x47,
        SoundCtrl3 = 0x48,
        SoundCtrl4 = 0x49,
        SoundCtrl5 = 0x4A,
        SoundCtrl6 = 0x4B,
        SoundCtrl7 = 0x4C,
        SoundCtrl8 = 0x4D,
        SoundCtrl9 = 0x4E,
        SoundCtrl10 = 0x4F,
        DecayGeneric = 0x50,
        HiPassFltFreq = 0x51,
        OnOffGen1 = 0x52,
        OnOffGen2 = 0x53,
        PortamentoCtrl = 0x54,
        HiResVelPrefix = 0x58,
        ReverbCtrl = 0x5B,
        TremoloCtrl = 0x5C,
        ChorusCtrl = 0x5D,
        DetuneCtrl = 0x5E,
        PhaserCtrl = 0x5F,
        DataIncrement = 0x60,
        DataDecrement = 0x61,
        LSBNRPN = 0x62,
        MSBNRPN = 0x63,
        LSBRPN = 0x64,
        MSBRPN = 0x65,
        AllSoundsOff = 0x78,
        ResetAllCtrls = 0x79,
        AllNotesOff = 0x7B,
        MonoMode = 0x7E,
        PolyMode = 0x7F
    }

    public enum EngineID : int
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
        protected AudioEngine _audioEngine;
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
            _audioEngine = audioEngine;
            WaveFormat = _audioEngine.GetWaveFormat(); 
            _cachedSettings = _audioEngine.GetCachedSettings(); 
            Initialized = defaultInt; 
        }

        public abstract void SystemReset();
        public abstract bool SendCustomCC(int channel, short reverb, short chorus);
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
