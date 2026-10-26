using System;
using System.Collections.Generic;

namespace Estuary.Utilities
{
    // Accessed under EstuaryAudioSource's audio lock. Keeps completion tied to a
    // message's final packet AND its local render deadline, including short clips.
    internal sealed class AudioPlaybackTracker
    {
        private sealed class Message
        {
            public string Id;
            public long End;
            public bool Final;
            public double RenderedAt = double.PositiveInfinity;
            public readonly List<(long Start, long End)> Ranges = new List<(long, long)>();
        }

        private readonly List<Message> _messages = new List<Message>();
        private long _written;
        private long _read;
        public int PendingCount => _messages.Count;
        public bool HasFinal => _messages.Exists(m => m.Final);

        public void Enqueue(string id, int samples)
        {
            if (samples <= 0) return;
            var message = _messages.Find(m => m.Id == id);
            if (message == null)
            {
                message = new Message { Id = id };
                _messages.Add(message);
            }
            message.Ranges.Add((_written, _written + samples));
            _written += samples;
            message.End = _written;
            message.RenderedAt = double.PositiveInfinity;
        }

        public void MarkFinal(string id)
        {
            var message = _messages.Find(m => m.Id == id);
            // Text-only turns can send an audio terminator with no PCM. They do
            // not own local audio and must not complete another speaking turn.
            if (message != null) message.Final = true;
        }

        public void Consume(int samples, double blockStart, double samplesPerSecond, double outputLatency)
        {
            var previous = _read;
            _read += samples;
            foreach (var message in _messages)
                if (message.End > previous && message.End <= _read)
                    message.RenderedAt = blockStart + outputLatency + (message.End - previous) / samplesPerSecond;
        }

        public List<string> TakeCompleted(double dspTime)
        {
            var completed = new List<string>();
            for (int i = 0; i < _messages.Count;)
            {
                var message = _messages[i];
                if (message.Final && message.End <= _read && message.RenderedAt <= dspTime)
                {
                    completed.Add(message.Id);
                    _messages.RemoveAt(i);
                }
                else i++;
            }
            return completed;
        }

        public void Redact(string id, Action<int, int> clearQueuedSamples)
        {
            var message = _messages.Find(m => m.Id == id);
            if (message == null) return;
            foreach (var range in message.Ranges)
            {
                long start = Math.Max(_read, range.Start);
                if (range.End > start) clearQueuedSamples((int)(start - _read), (int)(range.End - start));
            }
            _messages.Remove(message);
        }

        public void Reset()
        {
            _messages.Clear();
            _written = _read = 0;
        }
    }
}
