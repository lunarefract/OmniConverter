using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media;
using NFluidsynth;

namespace OmniConverter
{
    public class FluidSynthEngine : AudioEngine
    {
        private List<NFluidsynth.SoundFont> _managedSfArray = [];
        private NFluidsynth.Settings _fluidSynthSettings;
        private Settings _cachedSettings;

        public unsafe FluidSynthEngine(CSCore.WaveFormat waveFormat, Settings settings) : base(waveFormat, settings, false)
        {
            Debug.PrintToConsole(Debug.LogType.Message, $"Preparing FluidSynth settings...");

            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            _fluidSynthSettings = new();

            _fluidSynthSettings[ConfigurationKeys.SynthAudioChannels].IntValue = 1;
            _fluidSynthSettings[ConfigurationKeys.SynthSampleRate].DoubleValue = settings.Synth.SampleRate;
            _fluidSynthSettings[ConfigurationKeys.SynthPolyphony].IntValue = settings.Synth.MaxVoices.LimitToRange(1, 65535);
            _fluidSynthSettings[ConfigurationKeys.SynthThreadSafeApi].IntValue = 1;
            _fluidSynthSettings[ConfigurationKeys.SynthMinNoteLength].IntValue = 0;

            _cachedSettings = settings;

            Initialized = true;

            Debug.PrintToConsole(Debug.LogType.Message, $"FluidSynth settings prepared...");

            return;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (Initialized)
            {
                _fluidSynthSettings.Dispose();
            }

            _disposed = true;
        }

        public NFluidsynth.Settings GetFluidSynthSettings() => _fluidSynthSettings;
        public Settings GetConverterSettings() => _cachedSettings;
    }

    public class FluidSynthRenderer : MIDIRenderer
    {
        public Synth? handle { get; private set; } = null;
        private long length = 0;
        private ulong sfCount = 0;
        private List<uint> _managedSfArray = [];

        private float[]? outL = null, outR = null;
        private FluidSynthEngine reference;

        public FluidSynthRenderer(FluidSynthEngine fluidsynth) : base(fluidsynth.WaveFormat, fluidsynth.CachedSettings.Synth.Volume, false)
        {
            reference = fluidsynth;

            if (UniqueID == string.Empty)
                return;

            if (fluidsynth == null)
                return;

            Debug.PrintToConsole(Debug.LogType.Message, $"Stream unique ID: {UniqueID}");

            handle = new(reference.GetFluidSynthSettings());
            var tmp = reference.GetConverterSettings();

            foreach (var sf in tmp.SoundFontsList)
            {
                var sfhandle = handle.LoadSoundFont(sf.SoundFontPath, true);

                if (sfhandle != 0)
                    _managedSfArray.Add(sfhandle);
            }
            
            if (_managedSfArray.Count > 0)
            {
                Debug.PrintToConsole(Debug.LogType.Message, $"{UniqueID} - Stream is open.");

                Initialized = true;
            }
        }

        private bool IsError(string Error)
        {
            return false;
        }

        public override unsafe int Read(float[] buffer, int offset, long delta, int count)
        {
            if (handle == null)
                return 0;

            if (outL == null)
                outL = new float[count / 2];

            if (outR == null)
                outR = new float[count / 2];

            lock (Lock)
            {
                fixed (float* buff = buffer)
                {
                    fixed (float* toutL = outL)
                    {
                        fixed (float* toutR = outR)
                        {
                            var offsetBuff = buff + offset;

                            // Zero out the buffer
                            for (int i = 0; i < count / 2; i++)
                            {
                                toutL[i] = 0.0f;
                                toutR[i] = 0.0f;
                            }

                            float*[] whatisthis = { toutL, toutR };

                            fixed (float** hell = whatisthis)
                                handle.Process(count / 2, 2, hell, 2, hell);

                            // Copy it in a way that makes F*****G SENSE,
                            // CHRIST FLUIDSYNTH, CAN'T YOU JUST BE F*****G NORMAL?????
                            for (int i = 0; i < count / 2; i++)
                            {
                                offsetBuff[i * 2] = toutL[i];
                                offsetBuff[i * 2 + 1] = toutR[i];
                            }
                        }

                    }
                }           
            }

            length += count;
            return count;
        }

        public override void SystemReset()
        {
            if (handle == null)
                return;

            handle.SystemReset();
        }

        public override bool SendCustomFXEvents(int channel, short reverb, short chorus)
        {
            return true;
        }

        public override void SendEvent(byte[] data)
        {
            if (handle == null)
                return;

            var status = data[0];
            var chan = status & 0xF;
            var param1 = data[1];
            var param2 = data.Length >= 3 ? data[2] : (byte)0;

            switch ((MIDIEventType)(status & 0xF0))
            {
                case MIDIEventType.NoteOn:
                    if (reference.CachedSettings.Event.FilterVelocity && param2 >= reference.CachedSettings.Event.VelocityLow && param2 <= reference.CachedSettings.Event.VelocityHigh)
                        return;
                    if (reference.CachedSettings.Event.FilterKey && (param1 < reference.CachedSettings.Event.KeyLow || param1 > reference.CachedSettings.Event.KeyHigh))
                        return;

                    if (param1 == 0)
                    {
                        handle.NoteOff(chan, param1);
                    }
                    else handle.NoteOn(chan, param1, param2);
                    return;

                case MIDIEventType.NoteOff:
                    if (reference.CachedSettings.Event.FilterKey && (param1 < reference.CachedSettings.Event.KeyLow || param1 > reference.CachedSettings.Event.KeyHigh))
                        return;

                    handle.NoteOff(chan, param1);
                    return;

                case MIDIEventType.PatchChange:
                    handle.ProgramChange(chan, param1);
                    return;

                case MIDIEventType.ChannelPressure:
                    handle.ChannelPressure(chan, param1);
                    return;

                case MIDIEventType.Aftertouch:
                    handle.KeyPressure(chan, param1, param2);
                    return;

                case MIDIEventType.CC:
                    handle.CC(chan, param1, param2);
                    return;

                case MIDIEventType.PitchBend:
                    handle.PitchBend(chan, (param2 << 7) | param1);
                    return;

                case MIDIEventType.SystemMessageStart:
                    {
                        switch ((MIDIEventType)status)
                        {
                            case MIDIEventType.SystemMessageStart:
                                {
                                    string sysexbuf = string.Empty;

                                    foreach (byte ch in data)
                                        sysexbuf += $"{ch:X}";

                                    try
                                    {
                                        if (handle.Sysex(data, null, false))
                                            Debug.PrintToConsole(Debug.LogType.Message, $"SysEx parsed! >> {sysexbuf}");
                                    }
                                    catch
                                    {

                                        Debug.PrintToConsole(Debug.LogType.Error, $"Invalid SysEx! >> {sysexbuf}");
                                    }
                          
                                }
                                return;

                            default:
                                break;
                        }

                        break;
                    }

                default:
                    break;
            }
        }

        public override void RefreshInfo()
        {
            if (handle == null)
                return;

            ActiveVoices = (ulong)handle.ActiveVoiceCount;
        }

        public override void SendEndEvent()
        {
            if (handle == null)
                return;

            for (int i = 0; i < 16; i++)
                handle.AllNotesOff(i);

            SystemReset();
        }

        public override long Position
        {
            get { return length; }
            set { throw new NotSupportedException("Can't set position."); }
        }

        public override long Length
        {
            get { return length; }
        }

        protected override void Dispose(bool disposing)
        {
            if (Disposed)
                return;

            if (_managedSfArray.Count > 0)
            {
                for (int i = 0; i < _managedSfArray.Count; i++)
                    handle?.UnloadSoundFont(_managedSfArray[i], false);
            }

            if (handle != null)
                handle.Dispose();

            UniqueID = string.Empty;
            CanSeek = false;

            Initialized = false;
            Disposed = true;
        }
    }

}
