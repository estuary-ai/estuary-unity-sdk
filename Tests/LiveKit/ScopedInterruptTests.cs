using System.Reflection;
using NUnit.Framework;

namespace Estuary.Tests
{
    public class ScopedInterruptTests
    {
        [TestCase("old", false)]
        [TestCase("current", true)]
        public void DelayedInterruptCannotMuteANewerUtterance(string messageId, bool shouldMute)
        {
            using (var voice = new LiveKitVoiceManager())
            {
                // Simulate room state without opening a network connection or audio device.
                typeof(LiveKitVoiceManager).GetProperty(nameof(voice.IsConnected))
                    .GetSetMethod(true).Invoke(voice, new object[] { true });
                typeof(LiveKitVoiceManager).GetField("_currentMessageId", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(voice, "current");
                voice.SignalInterruptAsync(messageId).GetAwaiter().GetResult();
                Assert.AreEqual(shouldMute, voice.IsBotAudioMuted);
            }
        }
    }
}
