using System;
using System.Collections.Generic;
using GTA.Math;
using GTAMP.Shared;
using GTAMP.Shared.Packets;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GTAMP.Client
{
    /// <summary>
    /// Voz por proximidade e radio 2.0 (NAudio + G.711 u-law 16 kHz wideband):
    /// - Mixer unico (MixingSampleProvider) em stereo para todos os jogadores (evita estouro de WaveOutEvent).
    /// - Audio 3D espacial (panning esquerdo/direito relativo a camera + atenuacao suave).
    /// - Modos de alcance (Sussurrar / Normal / Gritar) + Radio (walkie-talkie com filtro + chiado).
    /// - Suporte a Push-to-Talk e Voice Activity Detection (VAD) configuravel.
    /// </summary>
    internal sealed class VoiceManager : IDisposable
    {
        private sealed class SpeakerChannel
        {
            public BufferedWaveProvider Buffer;
            public ISampleProvider SampleProvider;
            public float PrevIn;
            public float PrevOut;
            public long LastPacketMs;
        }

        private static readonly WaveFormat CaptureFormat = new WaveFormat(Protocol.VoiceSampleRate, 16, 1);
        private static readonly WaveFormat StereoFloatFormat = WaveFormat.CreateIeeeFloatWaveFormat(Protocol.VoiceSampleRate, 2);

        private readonly Action<byte[]> _onAudioCaptured;
        private readonly Dictionary<int, SpeakerChannel> _speakers = new Dictionary<int, SpeakerChannel>();
        private readonly object _sync = new object();
        private readonly Random _rng = new Random();

        private WaveInEvent _waveIn;
        private WaveOutEvent _waveOut;
        private MixingSampleProvider _mixer;

        private bool _isRecording;
        private bool _pttHeld;
        private bool _vadEnabled;
        private float _vadThreshold = 0.03f;
        private int _micDeviceIndex;
        private int _outputDeviceIndex = -1;
        private long _vadHoldUntilMs;
        private float _serverVoiceRange = Protocol.DefaultVoiceRange;

        public bool IsTransmitting
        {
            get
            {
                if (!_isRecording) return false;
                if (_pttHeld) return true;
                return _vadEnabled && unchecked(Environment.TickCount - _vadHoldUntilMs) < 0;
            }
        }

        public VoiceManager(Action<byte[]> onAudioCaptured)
        {
            _onAudioCaptured = onAudioCaptured;
        }

        public void Configure(ClientConfig config)
        {
            _vadEnabled = config.VoiceActivation;
            _vadThreshold = config.VadThreshold;
            _micDeviceIndex = config.MicDeviceIndex;
            _outputDeviceIndex = config.OutputDeviceIndex;

            if (_vadEnabled)
                EnsureRecordingStarted();
            else if (!_pttHeld)
                StopRecording();
        }

        public void SetServerVoiceRange(float range)
        {
            _serverVoiceRange = range > 2f ? range : Protocol.DefaultVoiceRange;
        }

        public void StartTalking()
        {
            _pttHeld = true;
            EnsureRecordingStarted();
        }

        public void StopTalking()
        {
            _pttHeld = false;
            if (!_vadEnabled)
                StopRecording();
        }

        private void EnsureRecordingStarted()
        {
            if (_isRecording)
                return;

            try
            {
                if (WaveInEvent.DeviceCount <= 0)
                    return;

                int dev = _micDeviceIndex;
                if (dev < 0 || dev >= WaveInEvent.DeviceCount)
                    dev = 0;

                _waveIn = new WaveInEvent
                {
                    DeviceNumber = dev,
                    WaveFormat = CaptureFormat,
                    BufferMilliseconds = 50
                };
                _waveIn.DataAvailable += OnMicData;
                _waveIn.StartRecording();
                _isRecording = true;
            }
            catch
            {
                StopRecording();
            }
        }

        private void StopRecording()
        {
            _isRecording = false;
            if (_waveIn != null)
            {
                try
                {
                    _waveIn.DataAvailable -= OnMicData;
                    _waveIn.StopRecording();
                    _waveIn.Dispose();
                }
                catch { }
                _waveIn = null;
            }
        }

        private void OnMicData(object sender, WaveInEventArgs e)
        {
            if (!_isRecording || e.BytesRecorded <= 0)
                return;

            bool shouldSend = _pttHeld;
            if (!shouldSend && _vadEnabled)
            {
                float rms = MuLawCodec.ComputeRmsLevel(e.Buffer, e.BytesRecorded);
                int now = Environment.TickCount;
                if (rms >= _vadThreshold)
                {
                    _vadHoldUntilMs = unchecked(now + 350);
                    shouldSend = true;
                }
                else if (unchecked(now - _vadHoldUntilMs) < 0)
                {
                    shouldSend = true;
                }
            }

            if (!shouldSend)
                return;

            try
            {
                byte[] encoded = MuLawCodec.Encode(e.Buffer, e.BytesRecorded);
                if (encoded.Length > 0 && encoded.Length <= Protocol.MaxVoicePayloadBytes)
                    _onAudioCaptured(encoded);
            }
            catch { }
        }

        private void EnsureMixerStarted()
        {
            if (_waveOut != null && _mixer != null)
                return;

            try
            {
                _mixer = new MixingSampleProvider(StereoFloatFormat)
                {
                    ReadFully = true
                };
                _waveOut = new WaveOutEvent
                {
                    DeviceNumber = _outputDeviceIndex,
                    DesiredLatency = 70
                };
                _waveOut.Init(_mixer);
                _waveOut.Play();
            }
            catch
            {
                DisposeOutput();
            }
        }

        public void PlayRemoteAudio(VoiceDataPacket pkt, Vector3 camPos, Vector3 camRotDeg)
        {
            if (pkt == null || pkt.AudioData == null || pkt.AudioData.Length == 0)
                return;

            bool isRadio = pkt.RadioChannel > 0;
            float gain;
            float pan = 0f; // -1 = esquerda, 0 = centro, +1 = direita

            if (isRadio)
            {
                gain = 0.85f;
                pan = 0f;
            }
            else
            {
                float maxRange = _serverVoiceRange;
                if (pkt.Mode == VoiceRangeMode.Whisper) maxRange = Protocol.WhisperVoiceRange;
                else if (pkt.Mode == VoiceRangeMode.Shout) maxRange = Math.Max(_serverVoiceRange * 1.8f, Protocol.ShoutVoiceRange);

                float norm = Math.Max(0f, 1f - (pkt.Distance / Math.Max(1f, maxRange)));
                // Curva suave (quadratica leve) para atenuacao mais natural
                gain = norm * norm * 0.7f + norm * 0.3f;
                if (gain <= 0.02f)
                    return;

                // Calcula stereo panning 3D baseado na direcao da camera
                Vector3 speakerPos = Natives.ToGta(pkt.SpeakerPos);
                Vector3 toSpeaker = speakerPos - camPos;
                toSpeaker.Z = 0f;
                float horizLen = toSpeaker.Length();
                if (horizLen > 0.5f)
                {
                    double rz = camRotDeg.Z * Math.PI / 180.0;
                    // Vetor Right da camera no GTA V (Forward = (-sin rz, cos rz), Right = (cos rz, sin rz))
                    float rightX = (float)Math.Cos(rz);
                    float rightY = (float)Math.Sin(rz);
                    float dot = (toSpeaker.X * rightX + toSpeaker.Y * rightY) / horizLen;
                    pan = Math.Max(-0.85f, Math.Min(0.85f, dot));
                }
            }

            float[] mono = MuLawCodec.DecodeToFloats(pkt.AudioData, pkt.AudioData.Length, gain);

            lock (_sync)
            {
                EnsureMixerStarted();
                if (_mixer == null)
                    return;

                SpeakerChannel ch;
                if (!_speakers.TryGetValue(pkt.SpeakerPlayerId, out ch))
                {
                    var buf = new BufferedWaveProvider(StereoFloatFormat)
                    {
                        DiscardOnBufferOverflow = true,
                        BufferDuration = TimeSpan.FromMilliseconds(450)
                    };
                    ISampleProvider sp = buf.ToSampleProvider();
                    _mixer.AddMixerInput(sp);
                    ch = new SpeakerChannel { Buffer = buf, SampleProvider = sp };
                    _speakers[pkt.SpeakerPlayerId] = ch;
                }

                ch.LastPacketMs = Environment.TickCount;

                // Efeito de walkie-talkie (filtro passa-alta simples + leve estatica)
                if (isRadio)
                {
                    float prevIn = ch.PrevIn;
                    float prevOut = ch.PrevOut;
                    for (int i = 0; i < mono.Length; i++)
                    {
                        float x = mono[i];
                        float y = 0.78f * (prevOut + x - prevIn);
                        prevIn = x;
                        prevOut = y;
                        // Clip suave + chiado discreto
                        float shaped = y * 1.35f;
                        if (shaped > 0.75f) shaped = 0.75f;
                        else if (shaped < -0.75f) shaped = -0.75f;
                        float noise = (float)(_rng.NextDouble() * 2.0 - 1.0) * 0.012f;
                        mono[i] = shaped + noise;
                    }
                    ch.PrevIn = prevIn;
                    ch.PrevOut = prevOut;
                }

                // Equal-power stereo panning -> escreve floats intercalados (L, R) em bytes
                float leftGain = (float)Math.Sqrt(0.5 * (1.0 - pan));
                float rightGain = (float)Math.Sqrt(0.5 * (1.0 + pan));
                byte[] stereoBytes = new byte[mono.Length * 8];
                for (int i = 0; i < mono.Length; i++)
                {
                    float s = mono[i];
                    Buffer.BlockCopy(BitConverter.GetBytes(s * leftGain), 0, stereoBytes, i * 8, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(s * rightGain), 0, stereoBytes, i * 8 + 4, 4);
                }

                try { ch.Buffer.AddSamples(stereoBytes, 0, stereoBytes.Length); }
                catch { }
            }
        }

        public void RemoveSpeaker(int speakerId)
        {
            lock (_sync)
            {
                SpeakerChannel ch;
                if (_speakers.TryGetValue(speakerId, out ch))
                {
                    try { if (_mixer != null) _mixer.RemoveMixerInput(ch.SampleProvider); } catch { }
                    _speakers.Remove(speakerId);
                }
            }
        }

        private void DisposeOutput()
        {
            try { if (_waveOut != null) { _waveOut.Stop(); _waveOut.Dispose(); } } catch { }
            _waveOut = null;
            _mixer = null;
            _speakers.Clear();
        }

        public void Dispose()
        {
            _pttHeld = false;
            _vadEnabled = false;
            StopRecording();
            lock (_sync)
            {
                DisposeOutput();
            }
        }
    }
}
