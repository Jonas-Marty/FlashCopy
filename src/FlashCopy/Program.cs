using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alphaleonis.Win32.Filesystem;
using MoreLinq;
using System.Speech.Synthesis;
using DirectoryInfo = Alphaleonis.Win32.Filesystem.DirectoryInfo;
using DriveInfo = Alphaleonis.Win32.Filesystem.DriveInfo;

namespace FlashCopy
{
    class Program
    {
        private static DriveInfo[] _initialDrives;
        private static readonly List<DriveInfo> _drivesInProgress = new List<DriveInfo>();
        private static DirectoryInfo _sourceFolder;
        private static readonly ConcurrentQueue<DriveInfo> _askForPositionQueue = new ConcurrentQueue<DriveInfo>();
        private static readonly ConcurrentQueue<Tuple<DriveInfo, string>> _readyToCopyQueue = new ConcurrentQueue<Tuple<DriveInfo, string>>();
        private static readonly ConcurrentQueue<Tuple<DriveInfo, string>> _finishedQueue = new ConcurrentQueue<Tuple<DriveInfo, string>>();
        private static readonly ConcurrentQueue<Tuple<DriveInfo, string, Exception>> _errorQueue = new ConcurrentQueue<Tuple<DriveInfo, string, Exception>>();
        private static readonly ConcurrentQueue<Tuple<DriveInfo, string, int>> _progressQueue = new ConcurrentQueue<Tuple<DriveInfo, string, int>>();
        private static SpeechSynthesizer _synth;

        static void Main(string[] args)
        {
            ConfigureSpeech();
            Console.BufferWidth = 150;
            Console.WindowWidth = 150;
            _sourceFolder = new DirectoryInfo(args[0]);
            _initialDrives = DriveInfo.GetDrives();
            Console.WriteLine($"Initial Drives: {_initialDrives.Select(d => d.Name).ToDelimitedString(", ")}");
            Task.Run(() => MonitorDrives());
            Console.WriteLine($"Started Monitorind Drives...");
            Task.Run(() => MonitorReadyToCopyQueue());
            Console.WriteLine($"Ready to copy data from {_sourceFolder} to your USB sticks, put them in these thiny wholes :P");

            while (true)
            {
                if (_askForPositionQueue.TryDequeue(out var driveToProcess))
                {
                    Console.Write($"New USB Stick detected (Label: '{driveToProcess.VolumeLabel}', Name: '{driveToProcess.Name}') please enter location: ");
                    var location = Console.ReadLine();
                    Console.WriteLine($"Start copy from '{_sourceFolder.FullName}' to '{driveToProcess.Name}', location: {location}");
                    _readyToCopyQueue.Enqueue(Tuple.Create(driveToProcess, location));
                }

                if (_finishedQueue.TryDequeue(out var info))
                {
                    var x = info ?? null;
                    Console.WriteLine($"Copy finished on '{ x.Item1.VolumeLabel}', Name: '{x.Item1.Name}') at location: '{x.Item2}'");
                    Console.Write("Please remove Drive and press any key to confirm.");
                    _synth.Speak("Please confirm you fagot.");
                    Console.ReadKey(intercept: true);
                    _drivesInProgress.RemoveAll(d => string.Equals(d.Name, x.Item1.Name, StringComparison.CurrentCultureIgnoreCase));
                    Console.WriteLine("Tanks for your confirmation and see you soon :D");
                }

                if (_errorQueue.TryDequeue(out var error))
                {
                    var x = error ?? null;
                    Console.WriteLine($"Something happened to '{x.Item1.VolumeLabel}', Name: '{x.Item1.Name}' at location '{x.Item2}'");
                    Console.WriteLine(x.Item3);
                    Console.Write("Please remove Drive and press any key to confirm.");
                    _synth.Speak("Error! Error! Please confirm you fagot.");
                    Console.Beep();
                    Console.ReadKey(intercept: true);
                    _drivesInProgress.RemoveAll(d => string.Equals(d.Name, x.Item1.Name, StringComparison.CurrentCultureIgnoreCase));
                    Console.WriteLine("Tanks for your confirmation please fix drive on another PC and then try again.");
                }

                if (_progressQueue.TryDequeue(out var progress))
                {
                    var x = progress ?? null;
                    Console.WriteLine($"Progress update: {progress.Item3}% '{x.Item1.VolumeLabel}', Name: '{x.Item1.Name}', Location '{x.Item2} ");
                }

                Thread.Sleep(100);
            }
        }

        private static void ConfigureSpeech()
        {
            _synth = new SpeechSynthesizer();

            // Configure the audio output. 
            _synth.SetOutputToDefaultAudioDevice();
            _synth.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Teen);
        }

        private static void MonitorReadyToCopyQueue()
        {
            while (true)
            {
                if (_readyToCopyQueue.TryDequeue(out var infos))
                {
                    var x = infos ?? null;
                    Task.Run(() => StartCopy(x));
                }
                Thread.Sleep(500);
            }
        }

        private static void StartCopy(Tuple<DriveInfo, string> infos)
        {
            try
            {
                _sourceFolder.CopyTo(infos.Item1.Name, CopyOptions.None, _progressHandler, new ProgressHolder { DriveInfo = infos.Item1, Name = infos.Item2 });
                infos.Item1.VolumeLabel = "USB_PAG";
                _finishedQueue.Enqueue(infos);
            }
            catch (Exception ex)
            {
                _errorQueue.Enqueue(Tuple.Create(infos.Item1, infos.Item2, ex));
            }
        }

        static CopyMoveProgressRoutine _progressHandler = new CopyMoveProgressRoutine(ProgressHandler);

        private static CopyMoveProgressResult ProgressHandler(long totalFileSize, long totalBytesTransferred, long streamSize, long streamBytesTransferred, int streamNumber, CopyMoveProgressCallbackReason callbackReason, object userData)
        {
            if (callbackReason == CopyMoveProgressCallbackReason.StreamSwitch)
            {
                return CopyMoveProgressResult.Continue;
            }

            var progressHolder = (ProgressHolder) userData;

            var currentProgressInPercent = totalBytesTransferred * 100 / totalFileSize;
            if (currentProgressInPercent >= progressHolder.Progress + 10)
            {
                progressHolder.Progress = (int)currentProgressInPercent;
                if (_progressQueue.All(p => p.Item1.Name != progressHolder.Name))
                {
                    _progressQueue.Enqueue(Tuple.Create(progressHolder.DriveInfo, progressHolder.Name, progressHolder.Progress));
                }
            }

            return CopyMoveProgressResult.Continue;
        }

        private static void MonitorDrives()
        {
            while (true)
            {
                foreach (var driveInfo in DriveInfo.GetDrives()
                    .ExceptBy(_initialDrives, d => d.Name)
                    .ExceptBy(_drivesInProgress, d => d.Name)
                    .Where(d => d.IsReady && d.VolumeLabel == "USB DISK" && d.DriveType == DriveType.Removable))
                {
                    _drivesInProgress.Add(driveInfo);
                    _askForPositionQueue.Enqueue(driveInfo);
                }

                Thread.Sleep(500);
            }
        }

        public class ProgressHolder
        {
            public int Progress { get; set; }

            public DriveInfo DriveInfo { get; set; }

            public string Name { get; set; }
        }
    }
}
