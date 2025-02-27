using CSCore.DirectSound;
using NFluidsynth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace OmniConverter
{
    public class FluidSynthEngine : AudioEngine
    {
        private NFluidsynth.Settings _fluidSynthSettings;

        public readonly object SFLock = new object();

        public unsafe FluidSynthEngine(Settings settings) : base(settings, false)
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

            _init = true;

            Debug.PrintToConsole(Debug.LogType.Message, $"FluidSynth settings prepared...");

            return;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (_init)
            {
                _fluidSynthSettings.Dispose();
            }

            _disposed = true;
        }

        public NFluidsynth.Settings GetFluidSynthSettings() => _fluidSynthSettings;
        public Settings GetConverterSettings() => _cachedSettings;
    }

    public class FluidSynthRenderer : AudioRenderer
    {
        public Synth? handle { get; private set; } = null;
        private bool noMoreData = false;
        private bool noFx = false;

        private List<int> _managedSfArray = [];

        private float[]? bufOutL = null, bufOutR = null;
        private unsafe float* pbufOutL = null, pbufOutR = null;
        private unsafe float*[]? ptrToBufs = null;
        private unsafe float** bufPtr = null;
        private GCHandle? gcHandleBufL = null, gcHandleBufR = null, gcHandleBufPtr = null;

        private FluidSynthEngine reference;

        public FluidSynthRenderer(FluidSynthEngine fluidsynth) : base(fluidsynth, false)
        {
            reference = fluidsynth;

            if (UniqueID == string.Empty)
                return;

            if (fluidsynth == null)
                return;

            Debug.PrintToConsole(Debug.LogType.Message, $"Stream unique ID: {UniqueID}");

            handle = new(reference.GetFluidSynthSettings());
            var tmp = reference.GetConverterSettings();
            var interp = FluidInterpolation.Linear;

            switch (tmp.Synth.Interpolation)
            {
                case GlobalSynthSettings.InterpolationType.None:
                    interp = FluidInterpolation.None;
                    break;

                case GlobalSynthSettings.InterpolationType.Point8:
                case GlobalSynthSettings.InterpolationType.Point16:
                    interp = FluidInterpolation.FourthOrder;
                    break;

                case GlobalSynthSettings.InterpolationType.Point32:
                case GlobalSynthSettings.InterpolationType.Point64:
                    interp = FluidInterpolation.SeventhOrder;
                    break;

                case GlobalSynthSettings.InterpolationType.Linear:
                default:
                    break;
            }

            handle.Gain = (float)tmp.Synth.Volume;
            handle.SetInterpolationMethod(-1, interp);

            noFx = _cachedSettings.Synth.DisableEffects;

            // FluidSynth "thread-safe API" moment
            lock (reference.SFLock)
            {
                foreach (var sf in tmp.SoundFontsList)
                {
                    var sfhandle = handle.LoadSoundFont(sf.SoundFontPath, true);

                    if (sfhandle != 0)
                    {
                        if (sf.SourceBank != -1)
                            handle.SetBankOffset(sfhandle, sf.SourceBank);

                        if (sf.SourcePreset != -1)
                        {
                            for (int i = 0; i < 16; i++)
                                handle.ProgramSelect(i, sfhandle, 0, (uint)sf.SourcePreset);
                        }

                        _managedSfArray.Add(sfhandle);
                    }
                }
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

            if (bufOutL == null || bufOutR == null)
            {
                bufOutL = new float[buffer.Length / 2];
                bufOutR = new float[buffer.Length / 2];

                gcHandleBufR = GCHandle.Alloc(bufOutL, GCHandleType.Pinned);
                gcHandleBufL = GCHandle.Alloc(bufOutR, GCHandleType.Pinned);

                pbufOutL = (float*)gcHandleBufR.Value.AddrOfPinnedObject().ToPointer();
                pbufOutR = (float*)gcHandleBufL.Value.AddrOfPinnedObject().ToPointer();

                ptrToBufs = [pbufOutL, pbufOutR];

                gcHandleBufPtr = GCHandle.Alloc(ptrToBufs, GCHandleType.Pinned);
                bufPtr = (float**)gcHandleBufPtr.Value.AddrOfPinnedObject().ToPointer();
            }

            // Zero out the buffer
            Array.Clear(bufOutL, 0, bufOutL.Length);
            Array.Clear(bufOutR, 0, bufOutR.Length);

            lock (_lock)
            {
                fixed (float* buff = buffer)
                {
                    var offsetBuff = buff + offset;

                    handle.Process(count / 2, noFx ? 0 : 2, noFx ? null : bufPtr, 2, bufPtr);

                    for (int i = 0; i < count / 2; i++)
                    {
                        offsetBuff[i * 2] = bufOutL[i];
                        offsetBuff[i * 2 + 1] = bufOutR[i];
                    }
                }           
            }

            _streamLength += count;
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
            if (handle != null)
            {
                handle?.CC(channel, 0x5B, reverb);
                handle?.CC(channel, 0x5D, reverb);

                return true;
            }

            return false;
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
                    if (param1 == 0)
                    {
                        handle.NoteOff(chan, param1);
                    }
                    else handle.NoteOn(chan, param1, param2);
                    return;

                case MIDIEventType.NoteOff:
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
                                    byte[] refactor = new byte[data.Length - 2];
                                    byte[] dummy = new byte[refactor.Length];

                                    for (int i = 1; i < data.Length - 1; i++)
                                        refactor[i - 1] = data[i];
                     
                                    if (!handle.Sysex(refactor, 0, refactor.Length, dummy, 0, dummy.Length))
                                    {
                                        string sysexbuf = string.Empty;

                                        foreach (byte ch in refactor)
                                            sysexbuf += $"{ch:X}";

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

            ActiveVoices = noMoreData ? 0 : (ulong)handle.ActiveVoiceCount;
        }

        public override void SendEndEvent()
        {
            if (handle == null)
                return;

            for (int i = 0; i < 16; i++)
            {
                handle.CC(i, 0x40, 0);
                handle.CC(i, 0x42, 0);
                handle.CC(i, 0x7B, 0);
            }

            handle.AllNotesOff(-1);

            noMoreData = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (Disposed)
                return;

            if (gcHandleBufL != null)
                gcHandleBufL.Value.Free();

            if (gcHandleBufR != null)
                gcHandleBufR.Value.Free();

            if (gcHandleBufPtr != null)
                gcHandleBufPtr.Value.Free();

            if (handle != null)
            {
                if (_managedSfArray.Count > 0)
                {
                    for (int i = 0; i < _managedSfArray.Count; i++)
                        handle.UnloadSoundFont(_managedSfArray[i], false);

                    handle.Dispose();
                }
            }

            UniqueID = string.Empty;
            CanSeek = false;

            Initialized = false;
            Disposed = true;
        }
    }

}
