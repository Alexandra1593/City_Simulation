using System.Diagnostics;

namespace ProiectSPG
{
    public class GameTimer
    {
        private readonly double secondsPerCount;
        private double deltaTime;

        private long baseTime;
        private long pausedTime;
        private long stopTime;
        private long previousTime;
        private long currentTime;

        private bool isStopped;

        public GameTimer()
        {
            Debug.Assert(Stopwatch.IsHighResolution, "System does not support high-resolution performance counter");

            secondsPerCount = 0.0;
            deltaTime = -1.0;
            baseTime = 0;
            pausedTime = 0;
            previousTime = 0;
            currentTime = 0;
            isStopped = false;

            long countsPerSec = Stopwatch.Frequency;
            secondsPerCount = 1.0 / countsPerSec;
        }

        public float TotalTime
        {
            get
            {
                if (isStopped)
                {
                    return (float)(((stopTime - pausedTime) - baseTime) * secondsPerCount);
                }
                return (float)(((currentTime - pausedTime) - baseTime) * secondsPerCount);
            }
        }

        public float DeltaTime => (float)deltaTime;

        public void Reset()
        {
            long _currentTime = Stopwatch.GetTimestamp();
            baseTime = _currentTime;
            previousTime = _currentTime;
            stopTime = 0;
            isStopped = false;
        }

        public void Start()
        {
            long startTime = Stopwatch.GetTimestamp();
            if (isStopped)
            {
                pausedTime += (startTime - stopTime);
                previousTime = startTime;
                stopTime = 0;
                isStopped = false;
            }
        }

        public void Stop()
        {
            if (!isStopped)
            {
                long _currentTime = Stopwatch.GetTimestamp();
                stopTime = _currentTime;
                isStopped = true;
            }
        }

        public void Tick()
        {
            if (isStopped)
            {
                deltaTime = 0.0;
                return;
            }

            long _currentTime = Stopwatch.GetTimestamp();
            currentTime = _currentTime;
            deltaTime = (currentTime - previousTime) * secondsPerCount;

            previousTime = currentTime;
            if (deltaTime < 0.0)
            {
                deltaTime = 0.0;
            }
        }
    }
}
