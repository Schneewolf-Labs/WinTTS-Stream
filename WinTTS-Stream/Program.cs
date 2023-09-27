using System;
using System.Collections.Concurrent;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using CSCore;
using CSCore.Codecs.WAV;
using CSCore.SoundOut;
using CSCore.CoreAudioAPI;
using CSCore.XAudio2;

class Program
{
    static ConcurrentQueue<string> ttsQueue = new ConcurrentQueue<string>();

    static void Main(string[] args)
    {
        // Exit if not running on Windows
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            Console.WriteLine("This program only runs on Windows.");
            return;
        }

        using (SpeechSynthesizer synthesizer = new SpeechSynthesizer())
        {
            // Enumerate voices and select the last one (usually Zira) unless a voice is specified in the command line
            var voices = synthesizer.GetInstalledVoices();
            var voiceIdx = args.Length > 0 ? int.Parse(args[0]) : voices.Count - 1;
            synthesizer.SelectVoice(voices[voiceIdx].VoiceInfo.Name);
            Console.WriteLine("Available Voices:");
            int idx = 0;
            foreach (var voice in voices)
            {
                Console.WriteLine($"{idx++}. {voice.VoiceInfo.Name}");
            }
            Console.WriteLine($"Selected Voice: {synthesizer.Voice.Name}");

            // Enumerate audio output devices and select the default unless one is specified in the command line
            Console.WriteLine("Available Audio Output Devices:");
            var deviceEnumerator = new MMDeviceEnumerator();
            var devices = deviceEnumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active);
            var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            int deviceIdx = 0;
            idx = 0;
            foreach (var device in devices)
            {
                Console.WriteLine($"{idx++}. {device.FriendlyName}");
                if (device.Equals(defaultDevice))
                    deviceIdx = idx - 1;
            }
            if (args.Length > 1)
                deviceIdx = int.Parse(args[1]);
            var deviceToUse = devices[deviceIdx];
            Console.WriteLine($"Output Device: {deviceToUse.FriendlyName}");

            // Start a separate thread or task to handle TTS synthesis
            Task.Run(() => ProcessQueue(synthesizer, deviceIdx));

            // Main thread to accept streaming input
            Console.WriteLine("TTS Service Running. Stream text to be spoken:");
            while (true)
            {
                string token = Console.ReadLine();
                if (string.IsNullOrEmpty(token))
                    continue;

                ttsQueue.Enqueue(token);
            }
        }
    }

    static void ProcessQueue(SpeechSynthesizer synthesizer, int outputDevice)
    {
        while (true)
        {
            if (ttsQueue.TryDequeue(out string token))
            {
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    synthesizer.SetOutputToWaveStream(memoryStream);
                    synthesizer.Speak(token);

                    // Reset memory stream position
                    memoryStream.Position = 0;

                    // Play the memory stream using CSCore
                    using (var waveSource = new WaveFileReader(memoryStream))
                    {
                        using (var soundOut = new WasapiOut())
                        {
                            soundOut.Device = new MMDeviceEnumerator().EnumAudioEndpoints(DataFlow.Render, DeviceState.Active)[outputDevice];
                            soundOut.Initialize(waveSource);
                            soundOut.Play();

                            // Use an event to wait for the audio to finish playing
                            bool playbackFinished = false;
                            soundOut.Stopped += (s, e) =>
                            {
                                playbackFinished = true;
                                Console.WriteLine("TOKEN_PLAYBACK_FINISHED");
                            };

                            while (!playbackFinished)
                            {
                                System.Threading.Thread.Sleep(100);
                            }
                        }
                    }
                }
            }
            else
            {
                System.Threading.Thread.Sleep(100); // Avoid busy-waiting
            }
        }
    }
}
