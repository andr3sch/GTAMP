using System;

namespace GTAMP.Shared
{
    /// <summary>
    /// Codec G.711 u-law puro em C# (sem DLL nativa).
    /// Comprime PCM 16-bit (2 bytes/amostra) em 1 byte/amostra (reducao de 50% na banda de voz).
    /// </summary>
    public static class MuLawCodec
    {
        private const int Bias = 0x84;
        private const int Clip = 32635;

        public static byte[] Encode(byte[] pcm16, int count)
        {
            int samples = count / 2;
            byte[] output = new byte[samples];
            for (int i = 0; i < samples; i++)
            {
                short sample = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
                output[i] = LinearToMuLaw(sample);
            }
            return output;
        }

        public static byte[] Decode(byte[] mulaw, int count, float volumeGain)
        {
            byte[] pcm = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                int sample = (int)(MuLawToLinear(mulaw[i]) * volumeGain);
                if (sample > short.MaxValue) sample = short.MaxValue;
                else if (sample < short.MinValue) sample = short.MinValue;
                pcm[i * 2] = (byte)(sample & 0xFF);
                pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            return pcm;
        }

        /// <summary>
        /// Decodifica u-law diretamente para floats normalizados [-1..1] (ideal para mixer 3D stereo).
        /// </summary>
        public static float[] DecodeToFloats(byte[] mulaw, int count, float volumeGain)
        {
            float[] output = new float[count];
            float scale = volumeGain / 32768f;
            for (int i = 0; i < count; i++)
            {
                float s = MuLawToLinear(mulaw[i]) * scale;
                if (s > 1f) s = 1f;
                else if (s < -1f) s = -1f;
                output[i] = s;
            }
            return output;
        }

        /// <summary>
        /// Calcula o nivel RMS [0..1] de um buffer PCM 16-bit para Voice Activity Detection (VAD).
        /// </summary>
        public static float ComputeRmsLevel(byte[] pcm16, int count)
        {
            int samples = count / 2;
            if (samples <= 0) return 0f;
            double sumSq = 0.0;
            for (int i = 0; i < samples; i++)
            {
                short s = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
                double norm = s / 32768.0;
                sumSq += norm * norm;
            }
            return (float)Math.Sqrt(sumSq / samples);
        }

        private static byte LinearToMuLaw(short sample)
        {
            int sign = (sample >> 8) & 0x80;
            if (sign != 0) sample = (short)-sample;
            if (sample > Clip) sample = Clip;
            sample = (short)(sample + Bias);

            int exponent = 7;
            for (int mask = 0x4000; (sample & mask) == 0 && exponent > 0; exponent--, mask >>= 1) { }

            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        private static short MuLawToLinear(byte ulaw)
        {
            ulaw = (byte)~ulaw;
            int sign = ulaw & 0x80;
            int exponent = (ulaw >> 4) & 0x07;
            int mantissa = ulaw & 0x0F;
            int sample = ((mantissa << 3) + Bias) << exponent;
            sample -= Bias;
            return (short)(sign != 0 ? -sample : sample);
        }
    }
}
