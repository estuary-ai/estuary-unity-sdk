#import <AVFoundation/AVFoundation.h>

/// Native Objective-C++ bridge for configuring AVAudioSession on iOS.
/// Called from C# via [DllImport("__Internal")] in iOSAudioConfiguration.cs.
///
/// Enables hardware AEC (Acoustic Echo Cancellation) by setting the audio session
/// to .playAndRecord category with .voiceChat mode. This is the iOS-standard way
/// to get echo cancellation for voice communication apps.

extern "C" {

    /// Configure AVAudioSession for voice chat with hardware AEC enabled.
    /// Sets category to PlayAndRecord with DefaultToSpeaker and AllowBluetooth options,
    /// and mode to VoiceChat which enables hardware AEC on iOS.
    /// Returns 1 on success, 0 on failure.
    int EstuaryiOS_ConfigureForVoiceChat()
    {
        @autoreleasepool {
            NSLog(@"[iOSAudioBridge] Configuring AVAudioSession for voice chat...");

            AVAudioSession *session = [AVAudioSession sharedInstance];
            NSError *error = nil;

            // Set category to PlayAndRecord with speaker and Bluetooth options
            // - DefaultToSpeaker: routes audio to speaker instead of earpiece
            // - AllowBluetooth: allows Bluetooth headsets (which have their own AEC)
            BOOL categorySuccess = [session setCategory:AVAudioSessionCategoryPlayAndRecord
                                            withOptions:(AVAudioSessionCategoryOptionDefaultToSpeaker |
                                                         AVAudioSessionCategoryOptionAllowBluetooth)
                                                  error:&error];
            if (!categorySuccess)
            {
                NSLog(@"[iOSAudioBridge] Failed to set audio category: %@", error.localizedDescription);
                return 0;
            }

            // Set mode to VoiceChat — this is the critical line that enables hardware AEC
            // iOS applies signal processing optimized for voice communication including:
            // - Acoustic Echo Cancellation (AEC)
            // - Noise Suppression (NS)
            // - Auto Gain Control (AGC)
            error = nil;
            BOOL modeSuccess = [session setMode:AVAudioSessionModeVoiceChat error:&error];
            if (!modeSuccess)
            {
                NSLog(@"[iOSAudioBridge] Failed to set voice chat mode: %@", error.localizedDescription);
                return 0;
            }

            // Activate the audio session
            error = nil;
            BOOL activateSuccess = [session setActive:YES error:&error];
            if (!activateSuccess)
            {
                NSLog(@"[iOSAudioBridge] Failed to activate audio session: %@", error.localizedDescription);
                return 0;
            }

            NSLog(@"[iOSAudioBridge] AVAudioSession configured successfully:");
            NSLog(@"[iOSAudioBridge]   Category: PlayAndRecord");
            NSLog(@"[iOSAudioBridge]   Mode: VoiceChat (hardware AEC enabled)");
            NSLog(@"[iOSAudioBridge]   Sample Rate: %.0f Hz", session.sampleRate);
            NSLog(@"[iOSAudioBridge]   Input Channels: %ld", (long)session.inputNumberOfChannels);
            NSLog(@"[iOSAudioBridge]   Output Channels: %ld", (long)session.outputNumberOfChannels);

            return 1;
        }
    }

    /// Reset AVAudioSession to Unity's default configuration.
    /// Sets category back to Playback and mode to Default.
    /// Returns 1 on success, 0 on failure.
    int EstuaryiOS_ResetAudioConfiguration()
    {
        @autoreleasepool {
            NSLog(@"[iOSAudioBridge] Resetting AVAudioSession to default...");

            AVAudioSession *session = [AVAudioSession sharedInstance];
            NSError *error = nil;

            // Reset mode to Default first
            BOOL modeSuccess = [session setMode:AVAudioSessionModeDefault error:&error];
            if (!modeSuccess)
            {
                NSLog(@"[iOSAudioBridge] Failed to reset audio mode: %@", error.localizedDescription);
                return 0;
            }

            // Reset category to Playback (Unity's default)
            error = nil;
            BOOL categorySuccess = [session setCategory:AVAudioSessionCategoryPlayback error:&error];
            if (!categorySuccess)
            {
                NSLog(@"[iOSAudioBridge] Failed to reset audio category: %@", error.localizedDescription);
                return 0;
            }

            NSLog(@"[iOSAudioBridge] AVAudioSession reset to default (Playback/Default)");
            return 1;
        }
    }

    /// Check if AEC is available. On iOS, hardware AEC is always available
    /// when using VoiceChat mode, so this always returns 1.
    int EstuaryiOS_IsAecAvailable()
    {
        // Hardware AEC is always available on iOS when using VoiceChat mode.
        // Unlike Android where AEC depends on device hardware/drivers,
        // iOS guarantees AEC in the VoiceChat audio session mode.
        return 1;
    }
}
