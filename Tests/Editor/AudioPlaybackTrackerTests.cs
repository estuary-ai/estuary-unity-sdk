using System.Collections.Generic;
using Estuary.Models;
using Estuary.Utilities;
using NUnit.Framework;

namespace Estuary.Tests
{
    public class AudioPlaybackTrackerTests
    {
        [Test]
        public void UnderrunIsNotCompletionEvenAfterLongSilence()
        {
            var tracker = new AudioPlaybackTracker();
            tracker.Enqueue("m", 100);
            tracker.Consume(100, 0, 1000, 0.05);
            Assert.IsEmpty(tracker.TakeCompleted(100));
            tracker.MarkFinal("m");
            CollectionAssert.AreEqual(new[] { "m" }, tracker.TakeCompleted(100));
            Assert.IsEmpty(tracker.TakeCompleted(101));
        }

        [Test]
        public void ShortFinalChunkWaitsForLocalRenderDeadline()
        {
            var tracker = new AudioPlaybackTracker();
            tracker.Enqueue("m", 10); tracker.MarkFinal("m");
            Assert.IsTrue(tracker.HasFinal);
            Assert.IsEmpty(tracker.TakeCompleted(10));
            tracker.Consume(10, 1, 1000, 0.1);
            Assert.IsEmpty(tracker.TakeCompleted(1.109));
            CollectionAssert.AreEqual(new[] { "m" }, tracker.TakeCompleted(1.111));
        }

        [Test]
        public void MessagesCompleteIndependentlyAndEmptyFinalDoesNotOwnAudio()
        {
            var tracker = new AudioPlaybackTracker();
            tracker.Enqueue("a", 100); tracker.MarkFinal("a");
            tracker.Enqueue("b", 200); tracker.MarkFinal("b");
            tracker.MarkFinal("text-only");
            tracker.Consume(300, 0, 1000, 0);
            CollectionAssert.AreEqual(new[] { "a" }, tracker.TakeCompleted(0.11));
            CollectionAssert.AreEqual(new[] { "b" }, tracker.TakeCompleted(0.31));
            Assert.AreEqual(0, tracker.PendingCount);
        }

        [Test]
        public void ResetOnDisconnectDropsPendingCompletions()
        {
            var tracker = new AudioPlaybackTracker();
            tracker.Enqueue("old", 100); tracker.MarkFinal("old");
            tracker.Consume(100, 0, 1000, 0.1);
            tracker.Reset();
            tracker.Enqueue("new", 50); tracker.MarkFinal("new");
            Assert.IsEmpty(tracker.TakeCompleted(10));
            tracker.Consume(50, 10, 1000, 0);
            CollectionAssert.AreEqual(new[] { "new" }, tracker.TakeCompleted(11));
        }

        [Test]
        public void RedactionOnlyClearsMatchingUnconsumedSamples()
        {
            var tracker = new AudioPlaybackTracker();
            tracker.Enqueue("a", 100); tracker.MarkFinal("a");
            tracker.Enqueue("b", 200); tracker.MarkFinal("b");
            tracker.Consume(40, 0, 1000, 0);
            var ranges = new List<(int, int)>();
            tracker.Redact("a", (offset, count) => ranges.Add((offset, count)));
            CollectionAssert.AreEqual(new[] { (0, 60) }, ranges);
            tracker.Consume(260, 1, 1000, 0);
            CollectionAssert.AreEqual(new[] { "b" }, tracker.TakeCompleted(5));
        }

        [Test]
        public void BotVoicePreservesFinalIncludingEmptyTerminator()
        {
            var voice = BotVoice.FromJson("{\"message_id\":\"m\",\"is_final\":true,\"audio\":\"\"}");
            Assert.IsTrue(voice.IsFinal);
            Assert.AreEqual("m", voice.MessageId);
            Assert.IsTrue(string.IsNullOrEmpty(voice.Audio));
        }
    }
}
